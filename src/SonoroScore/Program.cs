using System;
using System.Threading;
using System.Threading.Tasks;
using AlephalSonata.Automation;

namespace SonoroScore;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.Title = "SonoroScore — Wuthering Waves Echo Navigator";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintHeader();

        using var input = new InputSimulator();
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
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
            Console.ResetColor();
        };

        navigator.EchoSelected += (page, r, c, x, y) =>
        {
            Console.Write($"\r[SCANNING] Page {page + 1} | Card {r * 3 + c + 1}/15...       ");
        };

        while (true)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("--- SonoroScore Menu ---");
            Console.ResetColor();
            Console.WriteLine("1. Start Full Auto-Navigation & Scan");
            Console.WriteLine("2. Crawl Active Echo Picker (Fast Mode)");
            Console.WriteLine("3. Verify Game Window Status");
            Console.WriteLine("0. Exit");
            Console.Write("\nSelect [0-3]: ");

            var choice = Console.ReadLine()?.Trim();
            if (choice == "0") break;

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[HALT] Cancelled by user.");
                Console.ResetColor();
            };

            switch (choice)
            {
                case "1":
                    Console.Write("Enter number of pages to crawl [Default 5]: ");
                    var pStr = Console.ReadLine()?.Trim();
                    int pages = int.TryParse(pStr, out int p) && p > 0 ? p : 5;

                    Console.WriteLine("\n[INFO] Starting in 2 seconds... (Alt+Tab to cancel)");
                    await Task.Delay(2000, cts.Token);
                    var ok = await navigator.NavigateToEchoPickerAsync(cts.Token);
                    if (ok)
                    {
                        await navigator.CrawlEchoGridAsync(pages, cts.Token);
                    }
                    break;

                case "2":
                    Console.Write("Enter number of pages to crawl [Default 5]: ");
                    var pDirectStr = Console.ReadLine()?.Trim();
                    int pDirect = int.TryParse(pDirectStr, out int pd) && pd > 0 ? pd : 5;

                    Console.WriteLine("[INFO] Focusing game in 2 seconds...");
                    await WindowManager.EnsureForegroundAsync();
                    await Task.Delay(2000, cts.Token);
                    await navigator.CrawlEchoGridAsync(pDirect, cts.Token);
                    break;

                case "3":
                    CheckStatus();
                    break;

                default:
                    Console.WriteLine("Invalid option.");
                    break;
            }
        }
    }

    private static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
   ╔════════════════════════════════════════════════════╗
   ║                   SONOROSCORE                      ║
   ║      Wuthering Waves Native Echo Navigator         ║
   ╚════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }

    private static void CheckStatus()
    {
        Console.WriteLine();
        var hWnd = WindowManager.FindGameWindow();
        if (hWnd == IntPtr.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[STATUS] Wuthering Waves window not found. Please start the game.");
            Console.ResetColor();
            return;
        }

        WindowManager.GetGameBounds(out var rect);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[STATUS] Wuthering Waves found (Handle: 0x{hWnd:X8})");
        Console.WriteLine($"  Resolution: {rect.Width}x{rect.Height} (Position: {rect.Left}, {rect.Top})");
        Console.ResetColor();
    }
}
