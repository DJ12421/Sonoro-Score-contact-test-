using System;
using System.Text;
using System.Threading.Tasks;
using AlephalSonata.Native;

namespace AlephalSonata.Automation;

public static class WindowManager
{
    public const string DefaultGameTitle = "Wuthering Waves";

    public static IntPtr FindGameWindow(string titleHint = DefaultGameTitle)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        Win32.EnumWindows((hWnd, lParam) =>
        {
            if (!Win32.IsWindowVisible(hWnd)) return true;

            var sb = new StringBuilder(256);
            if (Win32.GetWindowText(hWnd, sb, sb.Capacity) > 0)
            {
                string title = sb.ToString();
                if (title.Contains(titleHint, StringComparison.OrdinalIgnoreCase))
                {
                    foundHwnd = hWnd;
                    return false; // Stop enumeration
                }
            }
            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    public static bool IsGameForeground(string titleHint = DefaultGameTitle)
    {
        var fg = Win32.GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        var sb = new StringBuilder(256);
        if (Win32.GetWindowText(fg, sb, sb.Capacity) > 0)
        {
            return sb.ToString().Contains(titleHint, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public static async Task<bool> EnsureForegroundAsync(string titleHint = DefaultGameTitle, int maxWaitMs = 1500)
    {
        var hWnd = FindGameWindow(titleHint);
        if (hWnd == IntPtr.Zero) return false;

        Win32.SetForegroundWindow(hWnd);

        int waited = 0;
        while (waited < maxWaitMs)
        {
            if (IsGameForeground(titleHint)) return true;
            await Task.Delay(100);
            waited += 100;
        }

        return IsGameForeground(titleHint);
    }

    public static bool GetGameBounds(out RECT bounds, string titleHint = DefaultGameTitle)
    {
        var hWnd = FindGameWindow(titleHint);
        if (hWnd != IntPtr.Zero)
        {
            return Win32.GetWindowRect(hWnd, out bounds);
        }
        bounds = default;
        return false;
    }
}
