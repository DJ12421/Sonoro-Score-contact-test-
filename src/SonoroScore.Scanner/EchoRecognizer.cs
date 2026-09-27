using System.Drawing;
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
            string mainStatLine  = await OcrRegionAsync(panel, EchoRegions.MainStatStrip,  upscale: true);
            string substatLabels = await OcrRegionAsync(panel, EchoRegions.SubstatsLabels, upscale: true);
            string substatValues = await OcrRegionAsync(panel, EchoRegions.SubstatsValues, upscale: true);

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
            int? cost = nameEntry?.Cost ?? ParseCost(substatLabels);

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

            // ── 8. Substats (2-column paired parse) ─────────────────────────
            var lblLines = substatLabels.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var valLines = substatValues.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var rawSubstats = StatParser.ParseColumns(lblLines, valLines);

            // Remove main stat if it appeared in the substats
            if (mainStat != null)
                rawSubstats.RemoveAll(s => s.Key == mainStat.Key);

            var substats = rawSubstats
                .Take(5)
                .Select(s =>
                {
                    var (snapped, conf) = TunableRolls.Resolve(s.Key, s.Value);
                    return new SubstatResult(s.Key.ToString(), s.Value, snapped, conf, s.RawValue);
                })
                .ToList();

            if (substats.Count < 4)
                warnings.Add($"Only {substats.Count} substats parsed (expected 4–5).");

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
                Sonata      = new FieldResult(nameEntry?.Sonatas.Length > 0 ? nameEntry.Sonatas[0] : null, nameEntry?.Sonatas.Length > 0 ? 0.80f : 0f),
                MainStatKey   = new FieldResult(mainStat?.Key.ToString(), mainStat != null ? 0.90f : 0f, mainStat?.RawLabel),
                MainStatValue = new FieldResult(mainStat != null ? (object?)mainStat.Value : null, mainStat != null ? 0.88f : 0f, mainStat?.RawValue),
                Substats    = substats,
                RawNameOcr       = nameOcr.Trim(),
                RawMainStatOcr   = mainStatLine.Trim(),
                RawSubstatsOcr   = $"Labels: {string.Join(" | ", lblLines)}\nValues: {string.Join(" | ", valLines)}",
                RawLevelOcr      = levelOcr.Trim(),
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
