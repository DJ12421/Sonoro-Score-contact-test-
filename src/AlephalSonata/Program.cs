using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlephalSonata.Automation;
using AlephalSonata.Native;

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

        // Initialize logging folder
        string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        string logPath = Path.Combine(logsDir, $"aleph_trace_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        _logWriter = new StreamWriter(logPath, append: true);

        PrintBanner();
        Log("INFO", $"Diagnostic logger session initialized. Log file: {logPath}");

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
            Console.WriteLine("6. Open Logs Directory");
            Console.WriteLine("0. Exit");
            Console.Write("\nSelect an action [0-6]: ");

            var choice = Console.ReadLine()?.Trim();
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

                    Log("INFO", "Starting full navigation sequence in 2 seconds... (Alt+Tab to cancel)");
                    await Task.Delay(2000, cts.Token);
                    var ok = await navigator.NavigateToEchoPickerAsync(cts.Token);
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
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = logsDir,
                        UseShellExecute = true
                    });
                    break;

                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }

        _logWriter?.Dispose();
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(@"
    ╔═══════════════════════════════════════════════════════╗
    ║       ALEPHAL-SONATA (ℵ-Sonata)                       ║
    ║   Diagnostic Engine & Verbose Process Debugger        ║
    ║   Real-time HID Strokes, Window Tracking & Traces     ║
    ╚═══════════════════════════════════════════════════════╝");
        Console.ResetColor();
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
}
