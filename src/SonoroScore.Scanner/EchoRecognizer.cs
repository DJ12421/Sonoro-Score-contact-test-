using System.Drawing;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

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

        try
        {
            // ── 2. OCR individual regions ───────────────────────────────────
            string nameOcr       = await OcrRegionAsync(panel, EchoRegions.EchoName,       upscale: true);
            string levelOcr      = await OcrRegionAsync(panel, EchoRegions.Level,          upscale: true);
            string costOcr       = await OcrRegionAsync(panel, EchoRegions.Cost,           upscale: true);
            string mainStatLine  = await OcrRegionAsync(panel, EchoRegions.MainStatStrip,  upscale: true);
            // Zone B: Sonata Effect region (starts below skill text)
            string sonataZoneOcr = await OcrRegionAsync(panel, EchoRegions.SonataZone,     upscale: true);
            // Zone C: Owner strip
            string ownerZoneOcr  = await OcrRegionAsync(panel, EchoRegions.OwnerZone,      upscale: true);

            _log?.Invoke($"    Name OCR: \"{nameOcr.Replace('\n',' ')}\"");

            // ── 3. Rarity (pixel, no OCR) ───────────────────────────────────
            int rarity;
            using (var rarityBmp = EchoRegions.CropRegion(panel, EchoRegions.RarityBand))
                rarity = RarityClassifier.Classify(rarityBmp);

            // ── 4. Echo name (fuzzy catalog match) ──────────────────────────
            string cleanedName = CleanOcrText(nameOcr);
            var (nameEntry, nameScore) = FuzzyMatcher.ClosestMatch(
                cleanedName, _catalog, e => e.Name, threshold: 0.68f);

            // Try 2-line windows if score is low
            if (nameScore < 0.75f)
            {
                var lines = cleanedName.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length - 1 && nameScore < 0.90f; i++)
                {
                    string window = lines[i] + " " + lines[i + 1];
                    var (e2, s2) = FuzzyMatcher.ClosestMatch(window, _catalog, e => e.Name, 0.68f);
                    if (s2 > nameScore) { nameEntry = e2; nameScore = s2; }
                }
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

            // ── 8. Substats (Unified Block with Y-Clustering & Pixel Fallback) ──
            using var substatsBmp = EchoRegions.CropRegion(panel, EchoRegions.SubstatsBlock);
            using var substatsUp  = ImagePreprocessor.Upscale2x(ImagePreprocessor.EnhanceForOcr(substatsBmp));
            var ocrLines = await WinOcr.RecognizeLinesWithBoundsAsync(substatsUp);

            double slotHeight = substatsUp.Height / 5.0;
            var slotLabels = new string?[5];
            var slotValues = new float?[5];
            var slotPercents = new bool[5];
            var slotValStrs = new string?[5];

            foreach (var line in ocrLines)
            {
                int slot = (int)Math.Clamp(Math.Floor(line.Y / slotHeight), 0, 4);
                string text = line.Text.Trim();
                string normalized = StatParser.NormalizeOcrArtifacts(text);

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


            // ── 9. Sonata detection (Zone B OCR) ────────────────────────────
            // OCR the sonata zone. Find the "Sonata Effect" header line.
            // The very next non-empty line is the sonata set name (e.g. "Trailblazing Star @ (2/2)").
            // Strip the " @ (N/N)" icon artifact → get the set name text.
            string? sonataName    = null;
            float   sonataConf    = 0f;
            string  rawSonataOcr  = sonataZoneOcr.Trim();

            var sonataLines = sonataZoneOcr
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            int sonataHdrIdx = Array.FindIndex(sonataLines,
                l => l.Contains("Sonata", StringComparison.OrdinalIgnoreCase) &&
                     l.Contains("Effect", StringComparison.OrdinalIgnoreCase));

            if (sonataHdrIdx >= 0 && sonataHdrIdx + 1 < sonataLines.Length)
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
                        cleanedSonata, GameDatabase.KnownSonatas, s => s, threshold: 0.55f);

                    if (matched != null)
                    {
                        sonataName = matched;
                        sonataConf = score;
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
                Substats    = substats,
                RawNameOcr       = nameOcr.Trim(),
                RawMainStatOcr   = mainStatLine.Trim(),
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

    private static async Task<string> OcrRegionAsync(Bitmap panel, System.Drawing.RectangleF region, bool upscale)
    {
        using var crop = EchoRegions.CropRegion(panel, region);
        using var enhanced = ImagePreprocessor.EnhanceForOcr(crop);
        if (upscale)
        {
            using var up = ImagePreprocessor.Upscale2x(enhanced);
            return await WinOcr.RecognizeAsync(up);
        }
        return await WinOcr.RecognizeAsync(enhanced);
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
