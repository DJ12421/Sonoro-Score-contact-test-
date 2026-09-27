using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AlephalSonata.Automation;
using AlephalSonata.Native;
using SonoroScore.Scanner;

namespace AlephalSonata;

internal class Program
{
    private static StreamWriter? _logWriter;

    private static void Log(string level, string message)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string line = $"[{timestamp}] [{level,-5}] {message}";

        var color = level switch
        {
            "ERROR" => ConsoleColor.Red,
            "WARN"  => ConsoleColor.Yellow,
            "INFO"  => ConsoleColor.White,
            "DEBUG" => ConsoleColor.DarkGray,
            "OK"    => ConsoleColor.Green,
            _       => ConsoleColor.Gray
        };

        Console.ForegroundColor = color;
        Console.WriteLine(line);
        Console.ResetColor();

        _logWriter?.WriteLine(line);
        _logWriter?.Flush();
    }

    private static async Task Main(string[] args)
    {
        Console.Title = "Alephal-Sonata (ℵ-Sonata) — Diagnostic Debugger & Process Logger";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Initialize logging and image dataset folders
        string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        string imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aleph_images");
        Directory.CreateDirectory(imagesDir);

        string logPath = Path.Combine(logsDir, $"aleph_trace_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        _logWriter = new StreamWriter(logPath, append: true);

        PrintBanner();
        Log("INFO", $"Diagnostic logger session initialized. Log file: {logPath}");

        PrintAdminStatus();

        using var input = new InputSimulator();
        PrintDriverStatus(input);

        var config = new NavigationConfig();
        var navigator = new AutoNavigator(input, config);

        navigator.StatusChanged += (step, msg) =>
        {
            string level = step switch
            {
                NavigationStep.Aborted => "ERROR",
                NavigationStep.Completed => "OK",
                NavigationStep.ScanningGrid => "INFO",
                NavigationStep.ScrollingPage => "INFO",
                _ => "DEBUG"
            };
            Log(level, $"[STATE: {step}] {msg}");
        };

        navigator.EchoSelected += (page, r, c, x, y) =>
        {
            Log("DEBUG", $"[ACTION: CLICK_CARD] Page {page + 1} | Row {r + 1} Col {c + 1} -> Screen=({x}, {y})");
        };

        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("=== ALEPHAL-SONATA DIAGNOSTIC CONSOLE ===");
            Console.ResetColor();
            Console.WriteLine("1. Inspect Game Window Rect & Handle");
            Console.WriteLine("2. Test Single Click at Cell (0,0) (First Echo Card)");
            Console.WriteLine("3. Test Page Scroll Calibration (-34 ticks)");
            Console.WriteLine("4. Run Full Auto-Navigation with Verbose Tracing");
            Console.WriteLine("5. Run Active Picker Crawl with Verbose Tracing");
            Console.WriteLine("6. Capture Raw Screen Dataset (Pre-Animation Low-Noise Test Suite)");
            Console.WriteLine("7. Test Step-by-Step Nav ('C', Sidebar, Slot) & Timings/Intervals");
            Console.WriteLine("8. Open Images Directory (aleph_images)");
            Console.WriteLine("9. Open Logs Directory");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("A. Run OCR Echo Scanner on aleph_images test suite -> JSON");
            Console.ResetColor();
            Console.WriteLine("0. Exit");
            Console.Write("\nSelect an action [0-9, A]: ");

            var choice = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (choice == "0") break;

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Log("WARN", "[EMERGENCY_STOP] User triggered Ctrl+C abort.");
            };

            switch (choice)
            {
                case "1":
                    InspectWindow();
                    break;

                case "2":
                    await TestFirstCardClick(input, config, cts.Token);
                    break;

                case "3":
                    await TestScrollCalibration(input, config, cts.Token);
                    break;

                case "4":
                    Console.Write("Pages to trace [Default: 3]: ");
                    var p4Str = Console.ReadLine()?.Trim();
                    int p4 = int.TryParse(p4Str, out int v4) && v4 > 0 ? v4 : 3;

                    Console.Write($"Customize nav intervals (current: C={config.AfterKeyCIntervalMs}ms, Tab={config.AfterSidebarClickIntervalMs}ms, Slot={config.AfterSlotClickIntervalMs}ms)? [y/N]: ");
                    var p4Cust = Console.ReadLine()?.Trim().ToLowerInvariant();
                    int cWait4 = config.AfterKeyCIntervalMs;
                    int sbWait4 = config.AfterSidebarClickIntervalMs;
                    int slWait4 = config.AfterSlotClickIntervalMs;
                    if (p4Cust == "y" || p4Cust == "yes")
                    {
                        Console.Write($"Interval after 'C' in ms [Default: {config.AfterKeyCIntervalMs}]: ");
                        var cStr = Console.ReadLine()?.Trim();
                        if (int.TryParse(cStr, out int cw) && cw >= 0) cWait4 = cw;

                        Console.Write($"Interval after Sidebar click in ms [Default: {config.AfterSidebarClickIntervalMs}]: ");
                        var sbStr = Console.ReadLine()?.Trim();
                        if (int.TryParse(sbStr, out int sbw) && sbw >= 0) sbWait4 = sbw;

                        Console.Write($"Interval after Slot click in ms [Default: {config.AfterSlotClickIntervalMs}]: ");
                        var slStr = Console.ReadLine()?.Trim();
                        if (int.TryParse(slStr, out int slw) && slw >= 0) slWait4 = slw;
                    }

                    Log("INFO", $"Starting full navigation (Delays: C={cWait4}ms, Tab={sbWait4}ms, Slot={slWait4}ms | Holds: Sidebar={config.SidebarClickHoldMs}ms, Slot={config.SlotClickHoldMs}ms, Cards={config.DefaultClickHoldMs}ms) in 2 seconds... (Alt+Tab to cancel)");
                    await Task.Delay(2000, cts.Token);
                    var ok = await navigator.NavigateToEchoPickerAsync(null, null, cWait4, sbWait4, slWait4, cts.Token);
                    if (ok)
                    {
                        await navigator.CrawlEchoGridAsync(p4, cts.Token);
                    }
                    break;

                case "5":
                    Console.Write("Pages to trace [Default: 3]: ");
                    var p5Str = Console.ReadLine()?.Trim();
                    int p5 = int.TryParse(p5Str, out int v5) && v5 > 0 ? v5 : 3;

                    Log("INFO", "Bringing game to foreground in 2 seconds...");
                    await WindowManager.EnsureForegroundAsync();
                    await Task.Delay(2000, cts.Token);
                    await navigator.CrawlEchoGridAsync(p5, cts.Token);
                    break;

                case "6":
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("--- RAW SCREEN DATASET CAPTURE (OFFLINE CALIBRATION TEST SUITE) ---");
                    Console.ResetColor();

                    Console.Write("Start mode [1: Overworld -> AutoNav, 2: Active Picker (Default)]: ");
                    var modeStr = Console.ReadLine()?.Trim();
                    bool fromOverworld = modeStr == "1";

                    Console.Write("Pages to capture [Default: 3]: ");
                    var p6Str = Console.ReadLine()?.Trim();
                    int p6 = int.TryParse(p6Str, out int v6) && v6 > 0 ? v6 : 3;

                    Console.Write($"Scroll notches per page [Default: {config.ScrollTicksPerPage}]: ");
                    var scrollStr = Console.ReadLine()?.Trim();
                    int scrollTicks = int.TryParse(scrollStr, out int st) ? st : config.ScrollTicksPerPage;

                    Console.Write("Capture delay after card click in ms [Default: 50 ms (pre-animation shimmer)]: ");
                    var delayStr = Console.ReadLine()?.Trim();
                    int captureDelay = int.TryParse(delayStr, out int cd) && cd >= 0 ? cd : 50;

                    string sessionDir = Path.Combine(imagesDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
                    Directory.CreateDirectory(sessionDir);

                    Log("INFO", $"Dataset destination: {sessionDir}");
                    Log("INFO", $"Capture plan: {p6} pages ({p6 * 15} cards max), scroll: {scrollTicks} ticks, pre-render delay: {captureDelay} ms.");
                    Log("INFO", "Bringing game to foreground in 2 seconds... (Alt+Tab to cancel)");
                    await Task.Delay(2000, cts.Token);

                    if (fromOverworld)
                    {
                        var ready = await navigator.NavigateToEchoPickerAsync(cts.Token);
                        if (!ready) break;
                    }
                    else
                    {
                        if (!await WindowManager.EnsureForegroundAsync())
                        {
                            Log("ERROR", "Failed to focus Wuthering Waves window.");
                            break;
                        }
                    }

                    int captured = await navigator.CaptureEchoGridAsync(
                        sessionDir,
                        maxPages: p6,
                        scrollTicks: scrollTicks,
                        preAnimationCaptureDelayMs: captureDelay,
                        cardCaptured: (page, r, c, path) =>
                        {
                            Log("OK", $"[CAPTURED] Page {page + 1} | Row {r + 1} Col {c + 1} -> {Path.GetFileName(path)}");
                        },
                        ct: cts.Token);

                    Log("OK", $"Raw dataset capture complete! {captured} images saved to: {sessionDir}");

                    // Save session metadata
                    string metaPath = Path.Combine(sessionDir, "session_meta.json");
                    File.WriteAllText(metaPath, System.Text.Json.JsonSerializer.Serialize(new
                    {
                        timestamp = DateTime.UtcNow,
                        pages = p6,
                        scrollTicks = scrollTicks,
                        preAnimationCaptureDelayMs = captureDelay,
                        sidebarClickHoldMs = config.SidebarClickHoldMs,
                        slotClickHoldMs = config.SlotClickHoldMs,
                        totalCaptured = captured
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                    Console.Write("\nOpen captured images folder? [Y/n]: ");
                    if (Console.ReadLine()?.Trim().ToLowerInvariant() != "n")
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = sessionDir,
                            UseShellExecute = true
                        });
                    }
                    break;

                case "7":
                    await TestNavClick(input, config, cts.Token);
                    break;

                case "8":
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = imagesDir,
                        UseShellExecute = true
                    });
                    break;

                case "9":
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = logsDir,
                        UseShellExecute = true
                    });
                    break;

                case "A":
                    await RunScannerAsync(imagesDir, cts.Token);
                    break;

                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }

        _logWriter?.Dispose();
    }

    private static async Task RunScannerAsync(string imagesDir, CancellationToken ct)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("--- OCR ECHO SCANNER (ADAPTED FROM TACET-LAB PIPELINE) ---");
        Console.ResetColor();

        // ── Database source ────────────────────────────────────────────────
        Console.WriteLine("Database source:");
        Console.WriteLine("  1. Fetch from internet (nanoka.cc, always up-to-date)");
        Console.WriteLine("  2. Use local cache (echo_catalog.json next to exe, faster)");
        Console.WriteLine("  3. Load from custom JSON file path");
        Console.Write("Select [1/2/3, default=1]: ");
        var dbChoice = Console.ReadLine()?.Trim();

        EchoCatalogEntry[] catalog;
        try
        {
            if (dbChoice == "2")
            {
                Log("INFO", "Loading echo catalog from local cache...");
                catalog = await GameDatabase.LoadAsync(forceRefresh: false, log: m => Log("DEBUG", m));
            }
            else if (dbChoice == "3")
            {
                Console.Write("Path to echo JSON file: ");
                var customPath = Console.ReadLine()?.Trim() ?? "";
                if (!File.Exists(customPath)) { Log("ERROR", $"File not found: {customPath}"); return; }
                Log("INFO", $"Loading catalog from {customPath}...");
                catalog = await GameDatabase.LoadFromFileAsync(customPath);
            }
            else
            {
                Log("INFO", "Fetching echo catalog from nanoka.cc (internet)...");
                catalog = await GameDatabase.LoadAsync(forceRefresh: true, log: m => Log("DEBUG", m));
            }
            Log("OK", $"Catalog loaded: {catalog.Length} echoes.");
        }
        catch (Exception ex)
        {
            Log("ERROR", $"Failed to load catalog: {ex.Message}");
            return;
        }

        // ── Session selection ──────────────────────────────────────────────
        if (!Directory.Exists(imagesDir) || Directory.GetDirectories(imagesDir).Length == 0)
        {
            string fallbackPublish = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../publish/AlephalSonata/aleph_images"));
            string fallbackPublish2 = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../publish/AlephalSonata/aleph_images"));
            if (Directory.Exists(fallbackPublish) && Directory.GetDirectories(fallbackPublish).Length > 0)
                imagesDir = fallbackPublish;
            else if (Directory.Exists(fallbackPublish2) && Directory.GetDirectories(fallbackPublish2).Length > 0)
                imagesDir = fallbackPublish2;
        }

        var sessions = Directory.Exists(imagesDir) ? Directory.GetDirectories(imagesDir) : Array.Empty<string>();
        if (sessions.Length == 0)
        {
            Console.Write($"No sessions in {imagesDir}. Enter custom image folder: ");
            var customDir = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(customDir) && Directory.Exists(customDir))
            {
                imagesDir = customDir;
                sessions = Directory.GetDirectories(imagesDir);
                if (sessions.Length == 0 && Directory.GetFiles(imagesDir, "*.png").Length > 0)
                {
                    // The entered directory itself contains the images
                    sessions = new[] { imagesDir };
                }
            }
            if (sessions.Length == 0)
            {
                Log("ERROR", $"No session folders found in {imagesDir}");
                return;
            }
        }

        Console.WriteLine("\nAvailable sessions:");
        for (int i = 0; i < sessions.Length; i++)
            Console.WriteLine($"  {i + 1}. {Path.GetFileName(sessions[i])}");
        Console.Write($"Select session [1-{sessions.Length}, default=1]: ");
        var sessInput = Console.ReadLine()?.Trim();
        int sessIdx = int.TryParse(sessInput, out int si) && si >= 1 && si <= sessions.Length ? si - 1 : 0;
        string sessionPath = sessions[sessIdx];

        // ── Limit ──────────────────────────────────────────────────────────
        Console.Write("Max images to scan [default: all]: ");
        var limitStr = Console.ReadLine()?.Trim();
        int limit = int.TryParse(limitStr, out int lv) && lv > 0 ? lv : int.MaxValue;

        // ── Run ─────────────────────────────────────────────────────────────
        ScannerConfig.ApplyEnvironment();
        SonataSignatureMatcher.CheckVersion(m => Log("WARN", m));
        Log("INFO", $"OCR mode: {(ScannerConfig.UseWindowsOcrFallback ? "Tesseract+WinOcr-fallback" : "Tesseract-only (QA)")}");
        var imageFiles = Directory.GetFiles(sessionPath, "echo_*.png")
            .OrderBy(f => f)
            .Take(limit)
            .ToArray();

        Log("INFO", $"Scanning {imageFiles.Length} images in {Path.GetFileName(sessionPath)}...");
        Log("INFO", "This may take a minute (Windows OCR per image). Press Ctrl+C to abort.");

        var recognizer = new EchoRecognizer(catalog, m => Log("DEBUG", m));
        var results    = new List<EchoScanResult>();
        int done = 0;

        foreach (var imgPath in imageFiles)
        {
            ct.ThrowIfCancellationRequested();
            var result = await recognizer.RecognizeAsync(imgPath);
            results.Add(result);
            done++;

            string nameStr  = result.EchoName?.Value as string ?? "?";
            string statStr  = result.MainStatKey?.Value as string ?? "?";
            string conf     = result.EchoName?.Confidence.ToString("F2") ?? "—";
            string icon = result.Errors.Count > 0 ? "x" : result.IsComplete ? "+" : "~";
            Log(result.Errors.Count > 0 ? "WARN" : "OK",
                $"  [{done,3}/{imageFiles.Length}] {icon} {Path.GetFileName(imgPath)} -> {nameStr} (conf:{conf}) | {statStr} | {result.Substats.Count} substats");
        }

        // ── Summary ─────────────────────────────────────────────────────────
        int identified = results.Count(r => r.EchoName?.Value != null);
        int complete   = results.Count(r => r.IsComplete);
        float nameRate = results.Count > 0 ? (float)identified / results.Count : 0;
        float statRate = results.Count > 0 ? (float)results.Count(r => r.MainStatKey?.Value != null) / results.Count : 0;
        float subAvg   = results.Count > 0 ? (float)results.Average(r => r.Substats.Count) : 0;
        float sonataRate = results.Count > 0 ? (float)results.Count(r => r.Sonata?.Value != null) / results.Count : 0;
        int sonataIcon = results.Count(r => r.Warnings.Any(w => w.StartsWith("Sonata from icon match")));
        int sonataOcr = results.Count(r => r.Warnings.Any(w => w.StartsWith("Sonata from OCR text")));

        var session = new ScanSessionResult
        {
            SessionPath        = sessionPath,
            RunAt              = DateTime.UtcNow,
            DatabaseVersion    = GameDatabase.DataVersion,
            TotalImages        = imageFiles.Length,
            SuccessfulScans    = identified,
            CompleteEchoes     = complete,
            NameDetectionRate  = nameRate,
            MainStatDetectionRate = statRate,
            SubstatAvg         = subAvg,
            Results            = results,
        };

        // ── Save JSON ────────────────────────────────────────────────────────
        string outputPath = Path.Combine(sessionPath,
            $"scan_results_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(session, jsonOpts), ct);

        Console.WriteLine();
        Log("OK",   $"Scan complete!");
        Log("INFO", $"  Images scanned   : {imageFiles.Length}");
        Log("INFO", $"  Names identified : {identified} ({nameRate:P0})");
        Log("INFO", $"  Complete echoes  : {complete} ({(results.Count > 0 ? (float)complete / results.Count : 0):P0})");
        Log("INFO", $"  Main stat rate   : {statRate:P0}");
        Log("INFO", $"  Sonata rate      : {sonataRate:P0} (icon:{sonataIcon} ocr:{sonataOcr})");
        Log("INFO", $"  Avg substats     : {subAvg:F1}");
        Log("OK",   $"  Results saved to : {outputPath}");

        // ── 1-click exports (Tacet-Lab + GOOD) ─────────────────────────────
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string tacetPath = Path.Combine(sessionPath, $"tacet-lab-backup_{stamp}.json");
            string tacetJson = TacetLabExporter.ExportScans(results, out int tacetSkipped);
            await File.WriteAllTextAsync(tacetPath, tacetJson, ct);
            Log("OK", $"  Tacet-Lab backup : {tacetPath} ({results.Count - tacetSkipped} echoes, {tacetSkipped} skipped)");

            string goodPath = Path.Combine(sessionPath, $"sonoro-good_{stamp}.json");
            string goodJson = GoodExporter.ExportScans(results, out int goodSkipped);
            await File.WriteAllTextAsync(goodPath, goodJson, ct);
            Log("OK", $"  GOOD export      : {goodPath} ({results.Count - goodSkipped} echoes, {goodSkipped} skipped)");
        }
        catch (Exception ex)
        {
            Log("WARN", $"  Export failed: {ex.Message}");
        }

        Console.Write("\nOpen results JSON? [Y/n]: ");
        if (Console.ReadLine()?.Trim().ToLowerInvariant() != "n")
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = outputPath, UseShellExecute = true
            });
        }
    }

    private static void PrintBanner()
    {
        var ver = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.1";
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($@"
    ╔═══════════════════════════════════════════════════════╗
    ║       ALEPHAL-SONATA (ℵ-Sonata) v{ver,-10}           ║
    ║   Diagnostic Engine & Verbose Process Debugger        ║
    ║   Real-time HID Strokes, Window Tracking & Traces     ║
    ╚═══════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    private static void PrintAdminStatus()
    {
        bool isAdmin = false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { }

        if (isAdmin)
        {
            Log("OK", "Privilege: Elevated as Administrator (Windows UIPI bypass active).");
        }
        else
        {
            Log("ERROR", "Privilege: NOT RUNNING AS ADMINISTRATOR! Windows UIPI will block synthetic clicks/keystrokes to Wuthering Waves.");
        }
    }

    private static void PrintDriverStatus(InputSimulator input)
    {
        if (input.UsingVirtualHid)
        {
            Log("OK", "Input Backend: Interception Virtual HID Driver ACTIVE (0ms latency, kernel HID injection).");
        }
        else
        {
            Log("WARN", "Input Backend: Standard Win32 Fallback. (Install interception.dll to bypass UE4 input filter).");
        }
    }

    private static void InspectWindow()
    {
        var hWnd = WindowManager.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            Log("ERROR", "FindGameWindow failed. No visible window matching 'Wuthering Waves' found.");
            return;
        }

        WindowManager.GetGameBounds(out var rect);
        Log("OK", $"Game Window Found: HWND=0x{hWnd:X8}");
        Log("INFO", $"  Bounds: Left={rect.Left}, Top={rect.Top}, Right={rect.Right}, Bottom={rect.Bottom}");
        Log("INFO", $"  Size:   Width={rect.Width} px, Height={rect.Height} px");

        float aspect = (float)rect.Width / Math.Max(1, rect.Height);
        Log("INFO", $"  Aspect Ratio: {aspect:F4}");

        bool is16x9 = Math.Abs(aspect - (16.0f / 9.0f)) < 0.05f;
        if (is16x9)
        {
            Log("OK", "  Aspect Ratio Check: PASS (16:9 Standard Layout)");
        }
        else
        {
            Log("WARN", "  Aspect Ratio Check: WARNING — Non-16:9 ratio may shift fractional click coordinates.");
        }
    }

    private static async Task TestFirstCardClick(InputSimulator input, NavigationConfig config, CancellationToken ct)
    {
        Log("INFO", "Bringing game to foreground for Cell (0,0) click test...");
        if (!await WindowManager.EnsureForegroundAsync())
        {
            Log("ERROR", "Failed to bring Wuthering Waves to foreground.");
            return;
        }

        if (!WindowManager.GetGameBounds(out var bounds))
        {
            Log("ERROR", "Failed to read window bounds.");
            return;
        }

        int targetX = bounds.Left + (int)(bounds.Width * config.FirstCellFraction.X);
        int targetY = bounds.Top + (int)(bounds.Height * config.FirstCellFraction.Y);

        Log("DEBUG", $"Computed Card (0,0) Screen Coordinates: ({targetX}, {targetY}) [Frac=({config.FirstCellFraction.X}, {config.FirstCellFraction.Y})]");
        await input.SendClickAsync(targetX, targetY, ct: ct);
        Log("OK", "Click dispatched.");
    }

    private static async Task TestScrollCalibration(InputSimulator input, NavigationConfig config, CancellationToken ct)
    {
        Log("INFO", "Bringing game to foreground for Scroll Calibration test...");
        if (!await WindowManager.EnsureForegroundAsync())
        {
            Log("ERROR", "Failed to bring Wuthering Waves to foreground.");
            return;
        }

        if (!WindowManager.GetGameBounds(out var bounds))
        {
            Log("ERROR", "Failed to read window bounds.");
            return;
        }

        int anchorX = bounds.Left + (int)(bounds.Width * config.ScrollAnchorFraction.X);
        int anchorY = bounds.Top + (int)(bounds.Height * config.ScrollAnchorFraction.Y);

        Win32.SetCursorPos(anchorX, anchorY);
        Log("DEBUG", $"Cursor anchored at ({anchorX}, {anchorY}). Sending {config.ScrollTicksPerPage} wheel notches...");

        await input.SendScrollAsync(config.ScrollTicksPerPage, config.ScrollEventGapMs, ct);
        Log("OK", "Scroll burst completed.");
    }

    private static async Task TestNavClick(InputSimulator input, NavigationConfig config, CancellationToken ct)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("--- TEST STEP-BY-STEP NAVIGATION TIMINGS ---");
        Console.ResetColor();
        Console.WriteLine("1. Test Key 'C' (Open Character Menu)");
        Console.WriteLine("2. Test Echo Sidebar Icon Click (custom hold)");
        Console.WriteLine("3. Test Equipped Echo Slot Click (custom hold)");
        Console.WriteLine("4. Test Full 3-Step Sequence (C -> Sidebar -> Slot) with custom holds & intervals");
        Console.Write("Select action [1-4]: ");
        var sub = Console.ReadLine()?.Trim();

        Log("INFO", "Bringing game to foreground in 2 seconds...");
        if (!await WindowManager.EnsureForegroundAsync())
        {
            Log("ERROR", "Failed to focus Wuthering Waves window.");
            return;
        }

        if (!WindowManager.GetGameBounds(out var bounds))
        {
            Log("ERROR", "Failed to read window bounds.");
            return;
        }

        int sidebarX = bounds.Left + (int)(bounds.Width * config.EchoSidebarFraction.X);
        int sidebarY = bounds.Top + (int)(bounds.Height * config.EchoSidebarFraction.Y);

        int slotX = bounds.Left + (int)(bounds.Width * config.EchoSlotFraction.X);
        int slotY = bounds.Top + (int)(bounds.Height * config.EchoSlotFraction.Y);

        switch (sub)
        {
            case "1":
                Log("DEBUG", $"Sending 'C' with hold={config.KeyHoldMs}ms...");
                await input.SendKeyAsync("C", holdMs: config.KeyHoldMs, ct: ct);
                Log("OK", "'C' keystroke dispatched.");
                break;

            case "2":
                Console.Write($"Sidebar click hold in ms [Default: {config.SidebarClickHoldMs} ms]: ");
                var sHoldStr = Console.ReadLine()?.Trim();
                int sHold = int.TryParse(sHoldStr, out int sh) && sh > 0 ? sh : config.SidebarClickHoldMs;
                Log("DEBUG", $"Clicking Echo Sidebar at ({sidebarX}, {sidebarY}) [hold={sHold}ms]...");
                await input.SendClickAsync(sidebarX, sidebarY, holdMs: sHold, ct: ct);
                Log("OK", "Sidebar click dispatched.");
                break;

            case "3":
                Console.Write($"Slot click hold in ms [Default: {config.SlotClickHoldMs} ms]: ");
                var lHoldStr = Console.ReadLine()?.Trim();
                int lHold = int.TryParse(lHoldStr, out int lh) && lh > 0 ? lh : config.SlotClickHoldMs;
                Log("DEBUG", $"Clicking Equipped Echo Slot at ({slotX}, {slotY}) [hold={lHold}ms]...");
                await input.SendClickAsync(slotX, slotY, holdMs: lHold, ct: ct);
                Log("OK", "Slot click dispatched.");
                break;

            case "4":
                Console.WriteLine($"Current defaults — Holds: C={config.KeyHoldMs}ms, Sidebar={config.SidebarClickHoldMs}ms, Slot={config.SlotClickHoldMs}ms");
                Console.WriteLine($"                — Intervals: AfterC={config.AfterKeyCIntervalMs}ms, AfterTab={config.AfterSidebarClickIntervalMs}ms, AfterSlot={config.AfterSlotClickIntervalMs}ms");

                Console.Write($"Interval after 'C' in ms [Default: {config.AfterKeyCIntervalMs}]: ");
                var cInterStr = Console.ReadLine()?.Trim();
                int cInter = int.TryParse(cInterStr, out int ci) && ci >= 0 ? ci : config.AfterKeyCIntervalMs;

                Console.Write($"Interval after Sidebar click in ms [Default: {config.AfterSidebarClickIntervalMs}]: ");
                var sbInterStr = Console.ReadLine()?.Trim();
                int sbInter = int.TryParse(sbInterStr, out int sbi) && sbi >= 0 ? sbi : config.AfterSidebarClickIntervalMs;

                Console.Write($"Interval after Slot click in ms [Default: {config.AfterSlotClickIntervalMs}]: ");
                var slInterStr = Console.ReadLine()?.Trim();
                int slInter = int.TryParse(slInterStr, out int sli) && sli >= 0 ? sli : config.AfterSlotClickIntervalMs;

                Log("INFO", $"Sequence: C(hold={config.KeyHoldMs}ms) --[{cInter}ms]--> Sidebar(hold={config.SidebarClickHoldMs}ms) --[{sbInter}ms]--> Slot(hold={config.SlotClickHoldMs}ms) --[{slInter}ms]--> Done");

                Log("INFO", $"Running step 1: 'C' (hold={config.KeyHoldMs}ms, interval={cInter}ms)...");
                await input.SendKeyAsync("C", holdMs: config.KeyHoldMs, ct: ct);
                await Task.Delay(cInter, ct);

                Log("INFO", $"Running step 2: Sidebar Icon at ({sidebarX}, {sidebarY}) (hold={config.SidebarClickHoldMs}ms, interval={sbInter}ms)...");
                await input.SendClickAsync(sidebarX, sidebarY, holdMs: config.SidebarClickHoldMs, ct: ct);
                await Task.Delay(sbInter, ct);

                Log("INFO", $"Running step 3: Equipped Slot at ({slotX}, {slotY}) (hold={config.SlotClickHoldMs}ms, interval={slInter}ms)...");
                await input.SendClickAsync(slotX, slotY, holdMs: config.SlotClickHoldMs, ct: ct);
                await Task.Delay(slInter, ct);

                Log("OK", "Step sequence dispatched.");
                break;
        }
    }
}
