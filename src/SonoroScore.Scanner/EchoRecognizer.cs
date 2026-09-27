using System.Drawing;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Tesseract;

namespace SonoroScore.Scanner;

/// <summary>
/// Main echo recognition pipeline — adapted from Tacet-Lab's recognize.ts + parser.ts.
///
/// Pipeline per image:
///   1. Extract panel strip from full screenshot.
///   2. Crop named regions (name, level, cost, rarity-band, main-stat-label/value, substats).
///   3. Enhance + OCR each text crop via Windows.Media.Ocr.
///   4. Pixel-classify rarity from star band.
///   5. Fuzzy-match echo name against catalog.
///   6. Parse stat lines; snap substat values to tunable rolls.
///   7. Return EchoScanResult with per-field confidence.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public class EchoRecognizer
{
    private readonly EchoCatalogEntry[] _catalog;
    private readonly Action<string>? _log;

    public EchoRecognizer(EchoCatalogEntry[] catalog, Action<string>? log = null)
    {
        _catalog = catalog;
        _log = log;
    }

    public async Task<EchoScanResult> RecognizeAsync(string imagePath)
    {
        _log?.Invoke($"  [OCR] Scanning {Path.GetFileName(imagePath)}");

        Bitmap full;
        try { full = new Bitmap(imagePath); }
        catch (Exception ex)
        {
            return Error(imagePath, $"Cannot open image: {ex.Message}");
        }

        using (full)
        {
            return await RecognizeBitmapAsync(imagePath, full);
        }
    }

    private async Task<EchoScanResult> RecognizeBitmapAsync(string imagePath, Bitmap full)
    {
        // ── 1. Extract panel ────────────────────────────────────────────────
        // Our captured images ARE already the full WuWa window frame.
        // The echo detail panel is at the right side (~78% x, 12% y).
        Bitmap panel = EchoRegions.ExtractPanel(full);

        var errors   = new List<string>();
        var warnings = new List<string>();

        // ── 0. Signature version check (Priority 4) ─────────────────────────
        // Warns when sonata_signatures.json is stale vs. GameDatabase.DataVersion.
        if (!SonataSignatureMatcher.CheckVersion(_log))
            warnings.Add($"Sonata signature version mismatch (loaded='{SonataSignatureMatcher.LoadedVersion ?? "none"}', " +
                         $"expected='{SonataSignatureMatcher.ExpectedSignatureVersion}', db='{GameDatabase.DataVersion}').");

        // ── 0b. OCR engine mode (Priority 3/4 QA gate) ──────────────────────
        string ocrMode = TesseractOcr.IsAvailable
            ? (ScannerConfig.UseWindowsOcrFallback ? "Tesseract+WinOcr-fallback" : "Tesseract-only")
            : (ScannerConfig.UseWindowsOcrFallback ? "WinOcr-only (no tessdata)" : "NO-OCR-ENGINE");
        _log?.Invoke($"  [OCR] Engine mode: {ocrMode}");
        if (ocrMode == "NO-OCR-ENGINE")
            warnings.Add("No OCR engine available (Tesseract missing and Windows fallback disabled).");

        try
        {
            // ── 2. OCR individual regions ───────────────────────────────────
            // Strategy/PSM/whitelist per region shape (Tacet ocr-pool mapping):
            // name→Name/SINGLE_BLOCK, level→number/SINGLE_LINE, strips→Label/
            // SINGLE_LINE, zones→Text, substats→Substat/SINGLE_BLOCK.
            string nameOcr = await OcrNameAsync(panel);
            string levelOcr = await OcrRegionAsync(panel, EchoRegions.Level, FieldStrategy.Text,
                PageSegMode.SingleLine, TesseractOcr.NumberWhitelist,
                s => ParseLevel(s) != null);
            string costOcr = await OcrRegionAsync(panel, EchoRegions.Cost, FieldStrategy.Text,
                PageSegMode.SingleLine, TesseractOcr.TextWhitelist,
                s => ParseCost(s) != null);
            string mainStatLine = await OcrRegionAsync(panel, EchoRegions.MainStatStrip, FieldStrategy.Text,
                PageSegMode.SingleLine, TesseractOcr.TextWhitelist,
                s => StatParser.ParseLine(StatParser.NormalizeOcrArtifacts(s)) != null);
            string secondMainStatLine = await OcrRegionAsync(panel, EchoRegions.SecondMainStat, FieldStrategy.Text,
                PageSegMode.SingleLine, TesseractOcr.TextWhitelist,
                s => StatParser.ParseLine(StatParser.NormalizeOcrArtifacts(s)) != null);
            // Zone B: Sonata Effect region (starts below skill text)
            string sonataZoneOcr = await OcrRegionAsync(panel, EchoRegions.SonataZone, FieldStrategy.Text,
                PageSegMode.SingleBlock, TesseractOcr.TextWhitelist);
            // Zone C: Owner strip
            string ownerZoneOcr = await OcrRegionAsync(panel, EchoRegions.OwnerZone, FieldStrategy.Text,
                PageSegMode.SingleLine, TesseractOcr.TextWhitelist);

            _log?.Invoke($"    Name OCR: \"{nameOcr.Replace('\n',' ')}\"");

            // ── 3. Rarity (pixel, no OCR) ───────────────────────────────────
            // Raw color crop: the hue classifier needs color, never binarized text.
            int rarity;
            using (var rarityBmp = EchoRegions.CropRegion(panel, EchoRegions.RarityBand))
                rarity = RarityClassifier.Classify(rarityBmp);

            // ── 4. Echo name (fuzzy catalog match) ──────────────────────────
            string cleanedName = CleanOcrText(nameOcr);
            var (nameEntry, nameScore) = FuzzyMatcher.ClosestMatch(
                cleanedName, _catalog, e => e.Name, threshold: ScannerConfig.EchoNameMinConfidence);

            // Try 2-line windows if score is low
            if (nameScore < 0.75f)
            {
                var lines = cleanedName.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length - 1 && nameScore < 0.90f; i++)
                {
                    string window = lines[i] + " " + lines[i + 1];
                    var (e2, s2) = FuzzyMatcher.ClosestMatch(window, _catalog, e => e.Name, ScannerConfig.EchoNameMinConfidence);
                    if (s2 > nameScore) { nameEntry = e2; nameScore = s2; }
                }
            }

            // SingleLine PSM can clip wrapped (2-line) names: re-OCR with Auto
            // segmentation as a second chance when the score is still low.
            if (nameScore < 0.75f && TesseractOcr.IsAvailable)
            {
                try
                {
                    string nameOcrAuto = await OcrRegionAsync(panel, EchoRegions.EchoName,
                        FieldStrategy.Name, PageSegMode.Auto, TesseractOcr.TextWhitelist);
                    string cleanedAuto = CleanOcrText(nameOcrAuto);
                    if (!string.IsNullOrWhiteSpace(cleanedAuto))
                    {
                        var (e3, s3) = FuzzyMatcher.ClosestMatch(
                            cleanedAuto, _catalog, e => e.Name, threshold: ScannerConfig.EchoNameMinConfidence);
                        if (s3 > nameScore)
                        {
                            nameEntry = e3; nameScore = s3; cleanedName = cleanedAuto;
                            nameOcr = nameOcrAuto; // keep evidence consistent with the winner
                        }
                        else
                        {
                            foreach (var line in cleanedAuto.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            {
                                var (e4, s4) = FuzzyMatcher.ClosestMatch(line, _catalog, e => e.Name, ScannerConfig.EchoNameMinConfidence);
                                if (s4 > nameScore) { nameEntry = e4; nameScore = s4; }
                            }
                        }
                    }
                }
                catch { /* keep SingleLine result */ }
            }

            if (nameEntry == null)
                warnings.Add($"Echo name not matched (best score {nameScore:F2}): \"{cleanedName}\"");

            // ── 5. Level ────────────────────────────────────────────────────
            int? level = ParseLevel(levelOcr);
            if (level == null) warnings.Add($"Level not parsed from: \"{levelOcr}\"");

            // ── 6. Cost (from catalog or parse) ────────────────────────────
            int? cost = nameEntry?.Cost ?? ParseCost(costOcr);

            // ── 7. Main stat ────────────────────────────────────────────────
            var mainStat = StatParser.ParseLine(mainStatLine);
            if (mainStat == null)
            {
                // Fallback: line might be split across newlines
                foreach (var line in mainStatLine.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    mainStat = StatParser.ParseLine(line);
                    if (mainStat != null) break;
                }
            }

            if (mainStat == null)
                errors.Add($"Main stat not parsed from: \"{mainStatLine.Replace('\n', ' ')}\"");

            // ── 7b. Second main stat (own region under the primary strip) ───
            // Lenient: warns instead of erroring, and stays silent when the
            // region is empty (not every echo shows a second line).
            var secondMainStat = StatParser.ParseLine(secondMainStatLine);
            if (secondMainStat == null && !string.IsNullOrWhiteSpace(secondMainStatLine))
            {
                foreach (var line in secondMainStatLine.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    secondMainStat = StatParser.ParseLine(line);
                    if (secondMainStat != null) break;
                }
                if (secondMainStat == null)
                    warnings.Add($"Second main stat not parsed from: \"{secondMainStatLine.Replace('\n', ' ')}\"");
            }

            // ── 8. Substats (Y-clustered slots + pixel fallback) ────────────
            // substatsBmp is a RAW color crop: StatPixelMatcher needs color,
            // never the binarized text path.
            using var substatsBmp = EchoRegions.CropRegion(panel, EchoRegions.SubstatsBlock);
            var (ocrLines, slotRefH) = await OcrLinesAsync(panel);

            double slotHeight = slotRefH / 5.0;
            var slotLabels = new string?[5];
            var slotValues = new float?[5];
            var slotPercents = new bool[5];
            var slotValStrs = new string?[5];
            var slotWhole = new ParsedStat?[5];

            foreach (var line in ocrLines)
            {
                int slot = (int)Math.Clamp(Math.Floor(line.Y / slotHeight), 0, 4);
                string text = line.Text.Trim();
                string normalized = StatParser.NormalizeOcrArtifacts(text);

                // Tesseract emits merged "Label Value" rows (e.g. "ATK 7.9%");
                // WinOcr emits split label/value lines. Try the merged parse first.
                var whole = StatParser.ParseLine(normalized);
                if (whole != null)
                {
                    slotWhole[slot] = whole;
                    continue;
                }

                // If line is a value (e.g. "8.4%", "150", "10.1%")
                var valMatch = Regex.Match(normalized, @"^[-+]?\s*(\d+(?:[.,]\d+)?)\s*(%)?$");
                if (valMatch.Success)
                {
                    if (float.TryParse(valMatch.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                    {
                        slotValues[slot] = val;
                        slotPercents[slot] = valMatch.Groups[2].Success || normalized.Contains('%');
                        slotValStrs[slot] = normalized;
                    }
                }
                else
                {
                    // Filter out UI headers like "Echo Skill"
                    if (!Regex.IsMatch(normalized, @"echo\s*skill", RegexOptions.IgnoreCase))
                        slotLabels[slot] = text;
                }
            }

            var substats = new List<SubstatResult>();
            for (int s = 0; s < 5; s++)
            {
                // Merged "Label Value" row (Tesseract style): key+value in one hit.
                if (slotWhole[s] is { } whole)
                {
                    if (mainStat != null && whole.Key == mainStat.Key) continue; // no main-stat dup
                    var (snappedW, snapConfW) = TunableRolls.Resolve(whole.Key, whole.Value);
                    substats.Add(new SubstatResult(whole.Key.ToString(), whole.Value, snappedW,
                        Math.Min(0.88f, snapConfW), $"{whole.RawLabel} {whole.RawValue}"));
                    continue;
                }

                if (slotValues[s] == null) continue; // slot is empty (unleveled echo)
                float numVal = slotValues[s]!.Value;
                bool isPercent = slotPercents[s];
                string? lbl = slotLabels[s];

                StatKey? key = null;
                float conf = 0.88f;

                if (!string.IsNullOrWhiteSpace(lbl))
                {
                    string combined = isPercent ? $"{lbl} {numVal}%" : $"{lbl} {numVal}";
                    var parsed = StatParser.ParseLine(combined);
                    if (parsed != null) key = parsed.Key;
                }

                // Pixel fallback: OCR commonly misses short 2-letter "HP" labels without context
                if (key == null)
                {
                    if (StatPixelMatcher.DetectHp(substatsBmp, s, 5))
                    {
                        key = isPercent ? StatKey.HpPercent : StatKey.Hp;
                        lbl = isPercent ? "HP %" : "HP";
                        conf = 0.90f;
                    }
                }

                if (key != null)
                {
                    // Prevent main stat duplication in substats
                    if (mainStat != null && key == mainStat.Key) continue;

                    var (snapped, snapConf) = TunableRolls.Resolve(key.Value, numVal);
                    var sub = new SubstatResult(key.Value.ToString(), numVal, snapped, Math.Min(conf, snapConf), $"{lbl} {numVal}{(isPercent ? "%" : "")}");
                    substats.Add(sub);
                }
            }

            string rawSubstatsOcr = string.Join("\n", ocrLines.Select(l => $"[y={l.Y:F0}, x={l.X:F0}] {l.Text}"));


            // ── 9. Sonata detection ─────────────────────────────────────────
            // Primary: icon pixel-signature match (Tacet-Lab visual.ts port).
            // Fallback: Zone B OCR text parse of the "Sonata Effect" line.
            string? sonataName    = null;
            float   sonataConf    = 0f;
            string  rawSonataOcr  = sonataZoneOcr.Trim();

            using (var iconCrop = EchoRegions.CropRegion(panel, EchoRegions.SonataIcon))
            {
                // Raw color crop: the signature matcher needs color, never binarized text.
                var (sigName, sigConf) = SonataSignatureMatcher.Match(iconCrop);
                if (sigName != null && sigConf > ScannerConfig.SonataIconMinConfidence)
                {
                    sonataName = sigName;
                    sonataConf = (float)sigConf;
                }
            }

            var sonataLines = sonataZoneOcr
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            int sonataHdrIdx = Array.FindIndex(sonataLines,
                l => l.Contains("Sonata", StringComparison.OrdinalIgnoreCase) &&
                     l.Contains("Effect", StringComparison.OrdinalIgnoreCase));

            // Only run the OCR text path when the icon matcher did not already win.
            if (sonataName != null)
            {
                // Icon signature match succeeded — keep it, skip OCR parse.
                // Traceable in test output via warnings (Priority 3 re-run target).
                warnings.Add($"Sonata from icon match: {sonataName} (conf {sonataConf:F2}).");
            }
            else if (sonataHdrIdx >= 0 && sonataHdrIdx + 1 < sonataLines.Length)
            {
                // Take the line immediately after "Sonata Effect"
                string rawSonataCand = sonataLines[sonataHdrIdx + 1];
                // Strip OCR icon artifact patterns like "@ (2/2)", "@ (5/5)", "(2/2)" etc.
                string cleanedSonata = Regex.Replace(rawSonataCand, @"[@\(].*$", "").Trim();
                cleanedSonata = Regex.Replace(cleanedSonata, @"\s+", " ").Trim();

                if (!string.IsNullOrEmpty(cleanedSonata))
                {
                    // Fuzzy match against known sonata names
                    var (matched, score) = FuzzyMatcher.ClosestMatch(
                        cleanedSonata, GameDatabase.KnownSonatas, s => s, threshold: ScannerConfig.SonataTextMinConfidence);

                    if (matched != null)
                    {
                        sonataName = matched;
                        sonataConf = score;
                        warnings.Add($"Sonata from OCR text: {sonataName} (conf {sonataConf:F2}).");
                    }
                    else
                    {
                        // Keep raw cleaned text as best guess with low confidence
                        sonataName = cleanedSonata;
                        sonataConf = 0.30f;
                        warnings.Add($"Sonata fuzzy match failed for: \"{cleanedSonata}\"");
                    }
                }
            }
            else if (sonataHdrIdx < 0)
            {
                // "Sonata Effect" header not found — try catalog fallback
                if (nameEntry?.Sonatas.Length > 0)
                {
                    sonataName = nameEntry.Sonatas[0];
                    sonataConf = 0.50f; // low confidence, catalog-inferred
                    warnings.Add("Sonata Effect header not found; using catalog default.");
                }
                else
                {
                    warnings.Add("Sonata Effect not detected.");
                }
            }

            // ── 10. Owner / Equipped By (Zone C OCR) ───────────────────────
            string? equippedBy = null;
            var ownerMatch = Regex.Match(ownerZoneOcr,
                @"Equipped\s+by\s+(.+)", RegexOptions.IgnoreCase);
            if (ownerMatch.Success)
                equippedBy = ownerMatch.Groups[1].Value.Trim();

            panel.Dispose();

            return new EchoScanResult
            {
                ImageFile   = Path.GetFileName(imagePath),
                ImagePath   = imagePath,
                ScannedAt   = DateTime.UtcNow,
                EchoName    = new FieldResult(nameEntry?.Name, nameScore, nameOcr.Replace('\n',' ').Trim()),
                Cost        = new FieldResult(cost.HasValue ? (object?)cost.Value : null, nameEntry != null ? 0.95f : 0.5f),
                Rarity      = new FieldResult(rarity > 0 ? (object?)rarity : null, rarity > 0 ? 0.88f : 0f),
                Level       = new FieldResult(level.HasValue ? (object?)level.Value : null, level.HasValue ? 0.90f : 0f, levelOcr.Trim()),
                Sonata      = new FieldResult(sonataName, sonataConf, rawSonataOcr),
                EquippedBy  = new FieldResult(equippedBy, equippedBy != null ? 0.90f : 0f, ownerZoneOcr.Trim()),
                MainStatKey   = new FieldResult(mainStat?.Key.ToString(), mainStat != null ? 0.90f : 0f, mainStat?.RawLabel),
                MainStatValue = new FieldResult(mainStat != null ? (object?)mainStat.Value : null, mainStat != null ? 0.88f : 0f, mainStat?.RawValue),
                SecondMainStatKey   = new FieldResult(secondMainStat?.Key.ToString(), secondMainStat != null ? 0.90f : 0f, secondMainStat?.RawLabel),
                SecondMainStatValue = new FieldResult(secondMainStat != null ? (object?)secondMainStat.Value : null, secondMainStat != null ? 0.88f : 0f, secondMainStat?.RawValue),
                Substats    = substats,
                RawNameOcr       = nameOcr.Trim(),
                RawMainStatOcr   = mainStatLine.Trim(),
                RawSecondMainStatOcr = secondMainStatLine.Trim(),
                RawSubstatsOcr   = rawSubstatsOcr,
                RawLevelOcr      = levelOcr.Trim(),
                RawSonataOcr     = rawSonataOcr,
                Errors   = errors,
                Warnings = warnings,
            };
        }
        catch (Exception ex)
        {
            panel.Dispose();
            return Error(imagePath, $"Unexpected error: {ex.Message}");
        }
    }


    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// OCR a region. Tesseract primary runs on field-preprocessed black-on-white
    /// input (<see cref="EchoFieldPreprocessor"/>); Windows OCR fallback runs on the
    /// legacy enhance+upscale path whose numbers are preserved as measured.
    /// Set <c>ScannerConfig.UseWindowsOcrFallback=false</c> for QA Tesseract-only runs.
    /// </summary>
    private static async Task<string> OcrRegionAsync(
        Bitmap panel, RectangleF region, FieldStrategy strategy,
        PageSegMode mode, string? whitelist, Func<string, bool>? accept = null)
    {
        bool fallbackAllowed = ScannerConfig.UseWindowsOcrFallback;
        if (TesseractOcr.IsAvailable)
        {
            string t = "";
            bool attempted = false;
            try
            {
                using var tessBmp = EchoFieldPreprocessor.Process(panel, region, strategy);
                t = await TesseractOcr.RecognizeAsync(tessBmp, mode, whitelist);
                attempted = true;
                if (!fallbackAllowed)
                    return t;
                bool empty = ScannerConfig.FallbackOnEmptyTesseractResult &&
                    (string.IsNullOrWhiteSpace(t) || t.Trim().Length < ScannerConfig.OcrMinTextLength);
                if (!empty && (accept == null || accept(t)))
                    return t;
                // Empty or grammar-rejected → fall through to Windows OCR.
            }
            catch
            {
                if (!fallbackAllowed)
                    return attempted ? t : string.Empty;
                /* fall through to Windows OCR */
            }
            if (fallbackAllowed)
            {
                string w = await WinOcrRegionAsync(panel, region);
                // Prefer whichever attempt satisfies the grammar; Tesseract wins ties.
                if (accept == null) return w;
                if (accept(w)) return w;
                if (accept(t)) return t;
                return w;
            }
            return t;
        }
        else if (!fallbackAllowed)
        {
            return string.Empty;
        }
        return await WinOcrRegionAsync(panel, region);
    }

    /// <summary>Legacy Windows-OCR path (EnhanceForOcr + Upscale2x).</summary>
    private static async Task<string> WinOcrRegionAsync(Bitmap panel, RectangleF region)
    {
        using var bmp = PreprocessForOcr(panel, region);
        return await WinOcr.RecognizeAsync(bmp);
    }

    /// <summary>
    /// Name-strip OCR with per-engine routing (<see cref="ScannerConfig.NameEngine"/>).
    /// Windows-only is the measured default for the stylized name font.
    /// </summary>
    private static async Task<string> OcrNameAsync(Bitmap panel)
    {
        switch (ScannerConfig.NameEngine)
        {
            case ScannerConfig.OcrEnginePreference.WindowsOnly:
                return await WinOcrRegionAsync(panel, EchoRegions.EchoName);
            case ScannerConfig.OcrEnginePreference.TesseractOnly:
                using (var tessBmp = EchoFieldPreprocessor.Process(panel, EchoRegions.EchoName, FieldStrategy.Name))
                    return await TesseractOcr.RecognizeAsync(tessBmp, ScannerConfig.NameRegionPsm, TesseractOcr.TextWhitelist);
            default:
                return await OcrRegionAsync(panel, EchoRegions.EchoName,
                    FieldStrategy.Name, ScannerConfig.NameRegionPsm, TesseractOcr.TextWhitelist);
        }
    }

    private static Bitmap PreprocessForOcr(Bitmap panel, System.Drawing.RectangleF region)
    {
        using var crop = EchoRegions.CropRegion(panel, region);
        using var enhanced = ImagePreprocessor.EnhanceForOcr(crop);
        return ImagePreprocessor.Upscale2x(enhanced);
    }

    private static async Task<(List<OcrLineInfo> Lines, double RefHeight)> OcrLinesAsync(Bitmap panel)
    {
        bool fallbackAllowed = ScannerConfig.UseWindowsOcrFallback;
        if (TesseractOcr.IsAvailable)
        {
            try
            {
                // Substats as one uniform multi-line block on the new text path.
                using var tessBmp = EchoFieldPreprocessor.Process(panel, EchoRegions.SubstatsBlock, FieldStrategy.Substat);
                var lines = await TesseractOcr.RecognizeLinesWithBoundsAsync(
                    tessBmp, ScannerConfig.SubstatBlockPsm, TesseractOcr.TextWhitelist);
                if (!fallbackAllowed)
                    return (lines, tessBmp.Height);
                // Quality gate: every substat row carries digits. A digit-free
                // result means Tesseract's segmentation failed on this crop —
                // fall through to Windows OCR instead of serving junk lines.
                bool usable = lines.Count > 0
                    && lines.Any(l => !string.IsNullOrWhiteSpace(l.Text))
                    && lines.Any(l => l.Text.Any(char.IsDigit));
                if (!ScannerConfig.FallbackOnEmptyTesseractResult || usable)
                    return (lines, tessBmp.Height);
                // else fall through to Windows OCR
            }
            catch
            {
                if (!fallbackAllowed)
                    return ([], 1);
                /* fall through to Windows OCR */
            }
        }
        else if (!fallbackAllowed)
        {
            return ([], 1);
        }
        using var crop = EchoRegions.CropRegion(panel, EchoRegions.SubstatsBlock);
        using var enhanced = ImagePreprocessor.EnhanceForOcr(crop);
        using var up = ImagePreprocessor.Upscale2x(enhanced);
        double h = up.Height;
        var wlines = await WinOcr.RecognizeLinesWithBoundsAsync(up);
        return (wlines, h);
    }

    private static string CleanOcrText(string ocr)
        => Regex.Replace(ocr, @"[^\w\s%\.\-]", " ").Trim();

    private static int? ParseLevel(string ocr)
    {
        var m = Regex.Match(ocr, @"(?:Lv\.?|Level|\+)?\s*(\d{1,2})", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int v) && v is >= 0 and <= 25) return v;
        return null;
    }

    private static int? ParseCost(string ocr)
    {
        var m = Regex.Match(ocr, @"Cost\s*([134])", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out int v)) return v;
        return null;
    }

    private static EchoScanResult Error(string path, string msg) => new()
    {
        ImageFile  = Path.GetFileName(path),
        ImagePath  = path,
        ScannedAt  = DateTime.UtcNow,
        Errors     = [msg],
    };
}
