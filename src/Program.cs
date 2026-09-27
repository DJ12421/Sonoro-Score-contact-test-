using System;
using System.Threading;
using System.Threading.Tasks;
using AlephalSonata.Automation;
using AlephalSonata.Native;

namespace AlephalSonata;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.Title = "Alephal-Sonata — Infinite Echo Automation";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintBanner();

        using var input = new InputSimulator();
        PrintDriverStatus(input);

        var config = new NavigationConfig();
        var navigator = new AutoNavigator(input, config);

        navigator.StatusChanged += (step, msg) =>
        {
            var color = step switch
            {
                NavigationStep.Completed => ConsoleColor.Green,
                NavigationStep.Aborted => ConsoleColor.Red,
                NavigationStep.ScanningGrid => ConsoleColor.Cyan,
                NavigationStep.ScrollingPage => ConsoleColor.Yellow,
                _ => ConsoleColor.White
            };
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{step}] {msg}");
            Console.ResetColor();
        };

        navigator.EchoSelected += (page, r, c, x, y) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  -> Card [Page {page + 1}, R{r + 1}:C{c + 1}] clicked at ({x}, {y})");
            Console.ResetColor();
        };

        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("=== ALEPHAL-SONATA CONTROLS ===");
            Console.ResetColor();
            Console.WriteLine("1. Detect Wuthering Waves Window");
            Console.WriteLine("2. Full Auto-Navigation (Main Screen -> Character -> Echo Picker -> Crawl)");
            Console.WriteLine("3. Crawl Picker Grid Only (Use if already inside Echo Picker)");
            Console.WriteLine("4. Test Page Scroll Calibration (-34 ticks)");
            Console.WriteLine("5. Test Single Card Click (First Echo)");
            Console.WriteLine("0. Exit");
            Console.Write("\nSelect an option [0-5]: ");

            var choice = Console.ReadLine()?.Trim();
            if (choice == "0") break;

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[EMERGENCY STOP] Cancellation requested by user.");
                Console.ResetColor();
            };

            switch (choice)
            {
                case "1":
                    CheckWindowStatus();
                    break;

                case "2":
                    Console.WriteLine("\n[INFO] Starting Full Auto-Navigation in 2 seconds... (Alt+Tab to cancel)");
                    await Task.Delay(2000, cts.Token);
                    var ok = await navigator.NavigateToEchoPickerAsync(cts.Token);
                    if (ok)
                    {
                        Console.Write("\nEcho Picker reached! How many pages to crawl? [Default: 5]: ");
                        var pageInput = Console.ReadLine()?.Trim();
                        int pages = int.TryParse(pageInput, out int p) && p > 0 ? p : 5;
                        await navigator.CrawlEchoGridAsync(pages, cts.Token);
                    }
                    break;

                case "3":
                    Console.Write("\nHow many pages to crawl? [Default: 5]: ");
                    var directPageInput = Console.ReadLine()?.Trim();
                    int directPages = int.TryParse(directPageInput, out int dp) && dp > 0 ? dp : 5;

                    Console.WriteLine("[INFO] Switching to game window in 2 seconds...");
                    await WindowManager.EnsureForegroundAsync();
                    await Task.Delay(2000, cts.Token);
                    await navigator.CrawlEchoGridAsync(directPages, cts.Token);
                    break;

                case "4":
                    Console.WriteLine("\n[INFO] Bringing game to foreground to test scroll...");
                    if (await WindowManager.EnsureForegroundAsync())
                    {
                        if (WindowManager.GetGameBounds(out var bounds))
                        {
                            int anchorX = bounds.Left + (int)(bounds.Width * config.ScrollAnchorFraction.X);
                            int anchorY = bounds.Top + (int)(bounds.Height * config.ScrollAnchorFraction.Y);
                            Win32.SetCursorPos(anchorX, anchorY);
                            Console.WriteLine($"Mouse placed at anchor ({anchorX}, {anchorY}). Scrolling {config.ScrollTicksPerPage} ticks...");
                            await input.SendScrollAsync(config.ScrollTicksPerPage, config.ScrollEventGapMs, cts.Token);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("Scroll test completed.");
                            Console.ResetColor();
                        }
                    }
                    break;

                case "5":
                    Console.WriteLine("\n[INFO] Bringing game to foreground to test click on first card...");
                    if (await WindowManager.EnsureForegroundAsync())
                    {
                        if (WindowManager.GetGameBounds(out var bounds))
                        {
                            int firstX = bounds.Left + (int)(bounds.Width * config.FirstCellFraction.X);
                            int firstY = bounds.Top + (int)(bounds.Height * config.FirstCellFraction.Y);
                            Console.WriteLine($"Clicking cell (0,0) at ({firstX}, {firstY})...");
                            await input.SendClickAsync(firstX, firstY, ct: cts.Token);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("Click test completed.");
                            Console.ResetColor();
                        }
                    }
                    break;

                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }
    }

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
    ╔═══════════════════════════════════════════════════════╗
    ║       ALEPHAL-SONATA (ℵ-Sonata)                       ║
    ║   Infinite Echo Automation & Navigation Engine        ║
    ║   Dedicated Windows C# Native Scanner                 ║
    ╚═══════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    private static void PrintDriverStatus(InputSimulator input)
    {
        Console.WriteLine();
        if (input.UsingVirtualHid)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[DRIVER STATUS] Interception Virtual HID Driver: ACTIVE");
            Console.WriteLine("  -> Input bypasses Unreal Engine & ACE synthetic filters directly.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[DRIVER STATUS] Interception Driver: NOT DETECTED (Using Win32 fallback)");
            Console.WriteLine("  -> For full hardware emulation, install interception.dll in PATH or app directory.");
        }
        Console.ResetColor();
    }

    private static void CheckWindowStatus()
    {
        Console.WriteLine();
        var hWnd = WindowManager.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[WINDOW] Wuthering Waves window NOT found. Please start the game first.");
            Console.ResetColor();
            return;
        }

        WindowManager.GetGameBounds(out var rect);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[WINDOW] Found Wuthering Waves handle: 0x{hWnd:X8}");
        Console.WriteLine($"  Bounds: Left={rect.Left}, Top={rect.Top}, Width={rect.Width}, Height={rect.Height}");
        
        float aspect = (float)rect.Width / Math.Max(1, rect.Height);
        Console.WriteLine($"  Resolution: {rect.Width}x{rect.Height} (Aspect Ratio: {aspect:F2})");
        
        bool is16x9 = Math.Abs(aspect - (16.0f / 9.0f)) < 0.05f;
        if (is16x9)
        {
            Console.WriteLine("  Aspect Ratio Check: PASS (16:9 Standard)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Aspect Ratio Check: Non-16:9 detected. Fractional coordinates may require slight tuning.");
        }
        Console.ResetColor();
    }
}
