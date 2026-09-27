using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SonoroScore.Scanner;

namespace SonoroScore.Scanner.Cli;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== SONORO-SCORE ECHO SCANNER & TEST SUITE RUNNER ===");
        ScannerConfig.ApplyEnvironment();

        // Parse arguments
        string? targetDir = null;
        int limit = int.MaxValue;
        bool forceRefresh = false;
        string? customOut = null;
        string? exportTacet = null;
        string? exportGood = null;
        bool updateSignatures = false;
        string? signatureUrl = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--dir" && i + 1 < args.Length) targetDir = args[++i];
            else if (args[i] == "--limit" && i + 1 < args.Length && int.TryParse(args[++i], out int l)) limit = l;
            else if (args[i] == "--refresh") forceRefresh = true;
            else if (args[i] == "--out" && i + 1 < args.Length) customOut = args[++i];
            else if (args[i] == "--export-tacet" && i + 1 < args.Length) exportTacet = args[++i];
            else if (args[i] == "--export-tacet-auto") exportTacet = "__auto__";
            else if (args[i] == "--export-good" && i + 1 < args.Length) exportGood = args[++i];
            else if (args[i] == "--export-good-auto") exportGood = "__auto__";
            else if (args[i] == "--no-winocr-fallback") ScannerConfig.UseWindowsOcrFallback = false;
            else if (args[i] == "--name-engine" && i + 1 < args.Length)
                ScannerConfig.NameEngine = args[++i].ToLowerInvariant() switch
                {
                    "tesseract" => ScannerConfig.OcrEnginePreference.TesseractOnly,
                    "windows" => ScannerConfig.OcrEnginePreference.WindowsOnly,
                    _ => ScannerConfig.OcrEnginePreference.Auto,
                };
            else if (args[i] == "--update-signatures") updateSignatures = true;
            else if (args[i] == "--signature-url" && i + 1 < args.Length) signatureUrl = args[++i];
            else if (args[i] == "--diag-tess") return await DiagTesseract();
            else if (args[i] == "--dump-lines" && i + 1 < args.Length) return await DumpLines(args[++i]);
        }

        // Default test suite location
        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            string defaultSession = @"C:\Users\Tina_\Documents\PlayingWithRepo\Sonoro-Score\publish\AlephalSonata\aleph_images\session_20260927_204453";
            if (Directory.Exists(defaultSession))
            {
                targetDir = defaultSession;
            }
            else
            {
                string baseImages = @"C:\Users\Tina_\Documents\PlayingWithRepo\Sonoro-Score\publish\AlephalSonata\aleph_images";
                if (Directory.Exists(baseImages))
                {
                    var dirs = Directory.GetDirectories(baseImages);
                    if (dirs.Length > 0) targetDir = dirs[0];
                }
            }
        }

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Target directory not found: {targetDir}");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"Target directory: {targetDir}");
        Console.WriteLine($"OCR mode: {(ScannerConfig.UseWindowsOcrFallback ? "Tesseract+WinOcr-fallback" : "Tesseract-only (QA)")}");

        // 0. Sonata signature version check (+ optional refresh, Priority 4)
        if (updateSignatures)
        {
            bool ok = await SonataSignatureMatcher.EnsureUpdatedAsync(
                url: signatureUrl, forceRefresh: true, log: m => Console.WriteLine(m));
            Console.WriteLine(ok ? "[SIG] Signatures ready." : "[SIG] Signature update failed; using local file.");
        }
        SonataSignatureMatcher.CheckVersion(m => Console.WriteLine(m));

        // 1. Load database (fetch from nanoka.cc or cache)
        EchoCatalogEntry[] catalog;
        try
        {
            Console.WriteLine("[DB] Checking/updating echo catalog...");
            catalog = await GameDatabase.LoadAsync(forceRefresh: forceRefresh, log: m => Console.WriteLine(m));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[DB] Catalog active: {catalog.Length} echoes ready.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[DB] Failed to load catalog: {ex.Message}");
            Console.ResetColor();
            return 2;
        }

        // 2. Discover images
        var images = Directory.GetFiles(targetDir, "echo_*.png")
            .OrderBy(f => f)
            .Take(limit)
            .ToArray();

        if (images.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"No 'echo_*.png' images found in {targetDir}.");
            Console.ResetColor();
            return 0;
        }

        Console.WriteLine($"Discovered {images.Length} echo capture images. Starting OCR evaluation...");
        Console.WriteLine(new string('-', 70));

        var recognizer = new EchoRecognizer(catalog);
        var results = new System.Collections.Generic.List<EchoScanResult>();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int idx = 0;

        foreach (var imgPath in images)
        {
            idx++;
            var scan = await recognizer.RecognizeAsync(imgPath);
            results.Add(scan);

            string name = scan.EchoName?.Value as string ?? "UNKNOWN";
            string stat = scan.MainStatKey?.Value as string ?? "UNKNOWN";
            object? val = scan.MainStatValue?.Value;
            string stat2 = scan.SecondMainStatKey?.Value as string ?? "—";
            object? val2 = scan.SecondMainStatValue?.Value;
            string sonata = scan.Sonata?.Value as string ?? "UNKNOWN";
            string conf = scan.EchoName?.Confidence.ToString("F2") ?? "0.00";
            int subs = scan.Substats.Count;

            string status = scan.Errors.Count > 0 ? "FAIL" : (scan.IsComplete ? "PASS" : "PARTIAL");
            var color = status switch
            {
                "PASS" => ConsoleColor.Green,
                "PARTIAL" => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };

            Console.ForegroundColor = color;
            Console.Write($"[{idx,3}/{images.Length}] [{status,-7}] ");
            Console.ResetColor();
            Console.WriteLine($"{Path.GetFileName(imgPath)} -> {name} | Sonata: {sonata} | Main: {stat} {val} | Main2: {stat2} {val2} | Subs: {subs}");
        }

        stopwatch.Stop();

        // 3. Aggregate statistics
        int total = results.Count;
        int namesFound = results.Count(r => r.EchoName?.Value != null);
        int statsFound = results.Count(r => r.MainStatKey?.Value != null);
        int stats2Found = results.Count(r => r.SecondMainStatKey?.Value != null);
        int sonatasFound = results.Count(r => r.Sonata?.Value != null);
        int complete = results.Count(r => r.IsComplete);
        float avgSubs = total > 0 ? (float)results.Average(r => r.Substats.Count) : 0;

        Console.WriteLine(new string('=', 70));
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("SCAN EVALUATION SUMMARY:");
        Console.ResetColor();
        Console.WriteLine($"  Total images processed  : {total}");
        Console.WriteLine($"  Elapsed time            : {stopwatch.Elapsed.TotalSeconds:F2}s ({(stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, total)):F0}ms/image)");
        Console.WriteLine($"  Echo names identified   : {namesFound}/{total} ({((float)namesFound / total):P1})");
        Console.WriteLine($"  Main stats detected     : {statsFound}/{total} ({((float)statsFound / total):P1})");
        Console.WriteLine($"  2nd main stats found  : {stats2Found}/{total} ({((float)stats2Found / total):P1})");
        Console.WriteLine($"  Sonata sets detected    : {sonatasFound}/{total} ({((float)sonatasFound / total):P1})");
        Console.WriteLine($"  Fully complete echoes   : {complete}/{total} ({((float)complete / total):P1})");
        Console.WriteLine($"  Average substats / echo : {avgSubs:F2}");

        // Sonata source breakdown (Priority 3 re-run target: icon match should dominate).
        int sonataIcon = results.Count(r => r.Warnings.Any(w => w.StartsWith("Sonata from icon match")));
        int sonataOcr = results.Count(r => r.Warnings.Any(w => w.StartsWith("Sonata from OCR text")));
        Console.WriteLine($"  Sonata via icon match  : {sonataIcon}/{total} ({((float)sonataIcon / Math.Max(1, total)):P1})");
        Console.WriteLine($"  Sonata via OCR fallback : {sonataOcr}/{total} ({((float)sonataOcr / Math.Max(1, total)):P1})");

        // 4. Save JSON results
        var sessionSummary = new ScanSessionResult
        {
            SessionPath = targetDir,
            RunAt = DateTime.UtcNow,
            DatabaseVersion = GameDatabase.DataVersion,
            TotalImages = total,
            SuccessfulScans = namesFound,
            CompleteEchoes = complete,
            NameDetectionRate = (float)namesFound / total,
            MainStatDetectionRate = (float)statsFound / total,
            SecondMainStatDetectionRate = (float)stats2Found / total,
            SubstatAvg = avgSubs,
            Results = results
        };

        string outPath = customOut ?? Path.Combine(targetDir, $"test_scan_results_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var opts = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(sessionSummary, opts));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[OK] Results successfully saved to:\n  {outPath}");
        Console.ResetColor();

        // 5. 1-click exports (Priority 4 / README roadmap)
        if (exportTacet != null)
        {
            string tacetPath = exportTacet == "__auto__"
                ? Path.Combine(targetDir, $"tacet-lab-backup_{DateTime.Now:yyyyMMdd_HHmmss}.json")
                : exportTacet;
            string json = TacetLabExporter.ExportScans(results, out int skipped);
            await File.WriteAllTextAsync(tacetPath, json);
            Console.WriteLine($"[OK] Tacet-Lab backup saved to:\n  {tacetPath} ({results.Count - skipped} echoes, {skipped} skipped)");
        }
        if (exportGood != null)
        {
            string goodPath = exportGood == "__auto__"
                ? Path.Combine(targetDir, $"sonoro-good_{DateTime.Now:yyyyMMdd_HHmmss}.json")
                : exportGood;
            string json = GoodExporter.ExportScans(results, out int skipped);
            await File.WriteAllTextAsync(goodPath, json);
            Console.WriteLine($"[OK] GOOD file saved to:\n  {goodPath} ({results.Count - skipped} echoes, {skipped} skipped)");
        }

        return 0;
    }

    /// <summary>Hidden diagnostic: why won't the Tesseract engine initialise here?</summary>
    static async Task<int> DiagTesseract()
    {
        await Task.Yield();
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        Console.WriteLine($"bitness={(Environment.Is64BitProcess ? "x64" : "x86")}");
        Console.WriteLine($"baseDir={baseDir}");
        Console.WriteLine($"psm: name={ScannerConfig.NameRegionPsm} substats={ScannerConfig.SubstatBlockPsm} strips=SingleLine zones:sonata=SingleBlock,owner=SingleLine");
        Console.WriteLine($"preprocessing: tesseract=EchoFieldPreprocessor(gray,p2-98norm,polarity,Otsu,x3,black-on-white) winocr=legacy Enhance+Upscale2x");
        Console.WriteLine($"TESSDATA_PREFIX={Environment.GetEnvironmentVariable("TESSDATA_PREFIX") ?? "(unset)"}");

        string[] candidates =
        [
            Path.Combine(baseDir, "tessdata"),
            @"C:\tessshort",
        ];
        foreach (string dp in candidates)
        {
            string file = Path.Combine(dp, "eng.traineddata");
            Console.WriteLine($"--- datapath={dp}");
            try
            {
                var fi = new FileInfo(file);
                Console.WriteLine($"  exists={fi.Exists} len={(fi.Exists ? fi.Length : -1)} attrs={(fi.Exists ? fi.Attributes.ToString() : "-")}");
                using var fs = File.OpenRead(file);
                byte[] magic = new byte[8];
                fs.ReadExactly(magic);
                Console.WriteLine($"  managed-read-ok magic={BitConverter.ToString(magic)}");
            }
            catch (Exception ex) { Console.WriteLine($"  managed-read-FAIL: {ex.GetType().Name}: {ex.Message}"); }

            try
            {
                using var engine = new Tesseract.TesseractEngine(dp, "eng", Tesseract.EngineMode.Default);
                Console.WriteLine("  ENGINE OK");
            }
            catch (Exception ex) { Console.WriteLine($"  ENGINE FAIL: {ex}"); }
        }

        foreach (var mod in System.Diagnostics.Process.GetCurrentProcess().Modules.Cast<System.Diagnostics.ProcessModule>()
            .Where(m => m.ModuleName.Contains("tesseract", StringComparison.OrdinalIgnoreCase)
                     || m.ModuleName.Contains("leptonica", StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"native: {mod.ModuleName} <- {mod.FileName}");
        return 0;
    }

    /// <summary>
    /// Hidden diagnostic: crop the substats block of one image exactly like the
    /// pipeline does, then print what each OCR engine sees (text + Y/X bounds).
    /// </summary>
    static async Task<int> DumpLines(string imagePath)
    {
        if (!File.Exists(imagePath)) { Console.WriteLine($"Not found: {imagePath}"); return 1; }
        using var full = new System.Drawing.Bitmap(imagePath);
        using var panel = EchoRegions.ExtractPanel(full);
        Console.WriteLine($"panel={panel.Width}x{panel.Height} block={EchoRegions.SubstatsBlock}");
        // WinOcr legacy path input:
        using var crop = EchoRegions.CropRegion(panel, EchoRegions.SubstatsBlock);
        using var up = ImagePreprocessor.Upscale2x(ImagePreprocessor.EnhanceForOcr(crop));
        Console.WriteLine($"legacy-upscaled={up.Width}x{up.Height} tesseractAvailable={TesseractOcr.IsAvailable}");

        if (TesseractOcr.IsAvailable)
        {
            // New field-preprocessed path (what the pipeline actually feeds Tesseract):
            using var tessBmp = EchoFieldPreprocessor.Process(panel, EchoRegions.SubstatsBlock, FieldStrategy.Substat);
            Console.WriteLine($"tess-preprocessed={tessBmp.Width}x{tessBmp.Height} psm={ScannerConfig.SubstatBlockPsm}");
            foreach (var mode in new[] { ScannerConfig.SubstatBlockPsm, Tesseract.PageSegMode.Auto })
            {
                var lines = await TesseractOcr.RecognizeLinesWithBoundsAsync(tessBmp, mode, TesseractOcr.TextWhitelist);
                Console.WriteLine($"-- Tesseract {mode}: {lines.Count} lines");
                foreach (var l in lines)
                    Console.WriteLine($"   [y={l.Y:F0} x={l.X:F0} w={l.Width:F0} h={l.Height:F0}] \"{l.Text}\"");
            }
        }
        var wlines = await WinOcr.RecognizeLinesWithBoundsAsync(up);
        Console.WriteLine($"-- WinOcr: {wlines.Count} lines");
        foreach (var l in wlines)
            Console.WriteLine($"   [y={l.Y:F0} x={l.X:F0} w={l.Width:F0} h={l.Height:F0}] \"{l.Text}\"");
        return 0;
    }
}
