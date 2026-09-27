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
            else if (args[i] == "--update-signatures") updateSignatures = true;
            else if (args[i] == "--signature-url" && i + 1 < args.Length) signatureUrl = args[++i];
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
            Console.WriteLine($"{Path.GetFileName(imgPath)} -> {name} | Sonata: {sonata} | Main: {stat} {val} | Subs: {subs}");
        }

        stopwatch.Stop();

        // 3. Aggregate statistics
        int total = results.Count;
        int namesFound = results.Count(r => r.EchoName?.Value != null);
        int statsFound = results.Count(r => r.MainStatKey?.Value != null);
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
}
