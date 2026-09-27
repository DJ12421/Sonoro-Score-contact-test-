using System;
using System.Threading;
using System.Threading.Tasks;
using AlephalSonata.Native;

namespace AlephalSonata.Automation;

public enum NavigationStep
{
    FocusWindow,
    OpenCharacterMenu,
    ClickEchoTab,
    ClickEchoSlot,
    EchoPickerReady,
    ScanningGrid,
    ScrollingPage,
    Completed,
    Aborted
}

public class AutoNavigator
{
    private readonly InputSimulator _input;
    private readonly NavigationConfig _config;

    public event Action<NavigationStep, string>? StatusChanged;
    public event Action<int, int, int, int, int>? EchoSelected; // pageIndex, row, col, screenX, screenY

    public AutoNavigator(InputSimulator input, NavigationConfig? config = null)
    {
        _input = input;
        _config = config ?? new NavigationConfig();
    }

    private void Report(NavigationStep step, string message) => StatusChanged?.Invoke(step, message);

    private bool CheckFocus()
    {
        if (WindowManager.IsGameForeground()) return true;
        Report(NavigationStep.Aborted, "Stopped: Wuthering Waves lost foreground focus (Alt+Tab safety trigger).");
        return false;
    }

    public Task<bool> NavigateToEchoPickerAsync(CancellationToken ct = default)
        => NavigateToEchoPickerAsync(null, null, ct);

    public async Task<bool> NavigateToEchoPickerAsync(int? sidebarHoldMs, int? slotHoldMs = null, CancellationToken ct = default)
    {
        int sbHold = sidebarHoldMs ?? _config.SidebarClickHoldMs;
        int slHold = slotHoldMs ?? _config.SlotClickHoldMs;

        Report(NavigationStep.FocusWindow, "Bringing Wuthering Waves to foreground...");
        if (!await WindowManager.EnsureForegroundAsync())
        {
            Report(NavigationStep.Aborted, "Failed to find or focus Wuthering Waves window.");
            return false;
        }

        await Task.Delay(300, ct);
        if (!CheckFocus()) return false;

        // Step 1: Open Character menu via 'C'
        Report(NavigationStep.OpenCharacterMenu, $"Sending 'C' to open Character menu (hold: {_config.KeyHoldMs}ms)...");
        await _input.SendKeyAsync("C", holdMs: _config.KeyHoldMs, ct: ct);
        await Task.Delay(1200, ct);
        if (!CheckFocus()) return false;

        if (!WindowManager.GetGameBounds(out var bounds))
        {
            Report(NavigationStep.Aborted, "Lost track of game window bounds.");
            return false;
        }

        // Step 2: Click Echo tab on left sidebar (calibrated: 120ms)
        int sidebarX = bounds.Left + (int)(bounds.Width * _config.EchoSidebarFraction.X);
        int sidebarY = bounds.Top + (int)(bounds.Height * _config.EchoSidebarFraction.Y);
        Report(NavigationStep.ClickEchoTab, $"Clicking Echo sidebar icon at ({sidebarX}, {sidebarY}) with hold={sbHold}ms...");
        await _input.SendClickAsync(sidebarX, sidebarY, holdMs: sbHold, ct: ct);
        await Task.Delay(1000, ct);
        if (!CheckFocus()) return false;

        // Step 3: Click top equipped echo slot (calibrated: 60ms)
        int slotX = bounds.Left + (int)(bounds.Width * _config.EchoSlotFraction.X);
        int slotY = bounds.Top + (int)(bounds.Height * _config.EchoSlotFraction.Y);
        Report(NavigationStep.ClickEchoSlot, $"Clicking equipped echo slot at ({slotX}, {slotY}) with hold={slHold}ms...");
        await _input.SendClickAsync(slotX, slotY, holdMs: slHold, ct: ct);
        await Task.Delay(_config.LandingDelayMs, ct);

        Report(NavigationStep.EchoPickerReady, "Echo picker is open and ready.");
        return CheckFocus();
    }

    public async Task CrawlEchoGridAsync(int maxPages = 50, CancellationToken ct = default)
    {
        if (!WindowManager.GetGameBounds(out var bounds))
        {
            Report(NavigationStep.Aborted, "Cannot read game window bounds.");
            return;
        }

        Report(NavigationStep.ScanningGrid, $"Starting grid crawl across up to {maxPages} pages...");

        for (int page = 0; page < maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (!CheckFocus()) return;

            // Recalculate bounds in case window moved
            WindowManager.GetGameBounds(out bounds);

            // Iterate 5 rows x 3 columns
            for (int r = 0; r < _config.GridRows; r++)
            {
                for (int c = 0; c < _config.GridCols; c++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!CheckFocus()) return;

                    float fracX = _config.FirstCellFraction.X + c * _config.StepFraction.X;
                    float fracY = _config.FirstCellFraction.Y + r * _config.StepFraction.Y;

                    int screenX = bounds.Left + (int)(bounds.Width * fracX);
                    int screenY = bounds.Top + (int)(bounds.Height * fracY);

                    // Click card (calibrated 60ms)
                    await _input.SendClickAsync(screenX, screenY, holdMs: _config.DefaultClickHoldMs, ct: ct);
                    EchoSelected?.Invoke(page, r, c, screenX, screenY);

                    // Humanized delay between card clicks
                    int jitter = _input.GaussianDelay(
                        _config.ClickDelayMeanMs,
                        _config.ClickDelaySigmaMs,
                        _config.ClickDelayMinMs,
                        _config.ClickDelayMaxMs
                    );
                    await Task.Delay(jitter, ct);
                }
            }

            // Scroll to next page
            Report(NavigationStep.ScrollingPage, $"Finished Page {page + 1}. Scrolling down {_config.ScrollTicksPerPage} ticks...");
            
            // Move cursor inside grid panel before scrolling
            int anchorX = bounds.Left + (int)(bounds.Width * _config.ScrollAnchorFraction.X);
            int anchorY = bounds.Top + (int)(bounds.Height * _config.ScrollAnchorFraction.Y);
            Win32.SetCursorPos(anchorX, anchorY);
            await Task.Delay(50, ct);

            await _input.SendScrollAsync(_config.ScrollTicksPerPage, _config.ScrollEventGapMs, ct);
            await Task.Delay(_config.PageScrollDelayMs, ct);
        }

        Report(NavigationStep.Completed, "Grid crawl completed successfully.");
    }

    public async Task<int> CaptureEchoGridAsync(
        string outputDirectory,
        int maxPages = 3,
        int scrollTicks = -34,
        int preAnimationCaptureDelayMs = 50,
        Action<int, int, int, string>? cardCaptured = null,
        CancellationToken ct = default)
    {
        if (!WindowManager.GetGameBounds(out var bounds))
        {
            Report(NavigationStep.Aborted, "Cannot read game window bounds.");
            return 0;
        }

        System.IO.Directory.CreateDirectory(outputDirectory);
        Report(NavigationStep.ScanningGrid, $"Starting raw screen capture test suite across {maxPages} pages (delay: {preAnimationCaptureDelayMs}ms, scroll: {scrollTicks})...");

        int totalCaptured = 0;

        for (int page = 0; page < maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            if (!CheckFocus()) return totalCaptured;

            WindowManager.GetGameBounds(out bounds);

            for (int r = 0; r < _config.GridRows; r++)
            {
                for (int c = 0; c < _config.GridCols; c++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!CheckFocus()) return totalCaptured;

                    float fracX = _config.FirstCellFraction.X + c * _config.StepFraction.X;
                    float fracY = _config.FirstCellFraction.Y + r * _config.StepFraction.Y;

                    int screenX = bounds.Left + (int)(bounds.Width * fracX);
                    int screenY = bounds.Top + (int)(bounds.Height * fracY);

                    // Click card (calibrated 60ms)
                    await _input.SendClickAsync(screenX, screenY, holdMs: _config.DefaultClickHoldMs, ct: ct);
                    EchoSelected?.Invoke(page, r, c, screenX, screenY);

                    // Wait for stats text to render but BEFORE 3D shimmer animation finishes
                    if (preAnimationCaptureDelayMs > 0)
                    {
                        await Task.Delay(preAnimationCaptureDelayMs, ct);
                    }

                    // Capture raw frame
                    using var bmp = ScreenCapturer.CaptureGameWindow();
                    if (bmp != null)
                    {
                        totalCaptured++;
                        string filename = $"echo_p{page + 1:D2}_r{r + 1:D2}_c{c + 1:D2}_idx{totalCaptured:D3}.png";
                        string fullPath = ScreenCapturer.SaveBitmap(bmp, outputDirectory, filename);
                        cardCaptured?.Invoke(page, r, c, fullPath);
                    }

                    // Small cadence pacing
                    await Task.Delay(60, ct);
                }
            }

            // Scroll to next page if not on last page
            if (page < maxPages - 1)
            {
                Report(NavigationStep.ScrollingPage, $"Finished Page {page + 1}. Scrolling down {scrollTicks} ticks...");

                int anchorX = bounds.Left + (int)(bounds.Width * _config.ScrollAnchorFraction.X);
                int anchorY = bounds.Top + (int)(bounds.Height * _config.ScrollAnchorFraction.Y);
                Win32.SetCursorPos(anchorX, anchorY);
                await Task.Delay(50, ct);

                await _input.SendScrollAsync(scrollTicks, _config.ScrollEventGapMs, ct);
                await Task.Delay(_config.PageScrollDelayMs, ct);
            }
        }

        Report(NavigationStep.Completed, $"Raw screen dataset captured: {totalCaptured} images saved to {outputDirectory}.");
        return totalCaptured;
    }
}
