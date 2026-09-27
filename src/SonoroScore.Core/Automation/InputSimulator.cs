using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlephalSonata.Native;

namespace AlephalSonata.Automation;

public class InputSimulator : IDisposable
{
    private IntPtr _interceptionContext = IntPtr.Zero;
    private readonly bool _useInterception;
    private readonly Random _rng = new();

    public bool UsingVirtualHid => _useInterception;

    public InputSimulator()
    {
        _useInterception = Interception.IsDriverAvailable();
        if (_useInterception)
        {
            _interceptionContext = Interception.interception_create_context();
        }
    }

    public static readonly Dictionary<string, byte> VirtualKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ESC"] = 0x1B,
        ["ENTER"] = 0x0D,
        ["TAB"] = 0x09,
        ["SPACE"] = 0x20,
        ["A"] = 0x41,
        ["B"] = 0x42,
        ["C"] = 0x43,
        ["D"] = 0x44,
        ["E"] = 0x45,
        ["F"] = 0x46,
        ["G"] = 0x47,
        ["H"] = 0x48,
        ["I"] = 0x49,
        ["J"] = 0x4A,
        ["K"] = 0x4B,
        ["L"] = 0x4C,
        ["M"] = 0x4D,
        ["N"] = 0x4E,
        ["O"] = 0x4F,
        ["P"] = 0x50,
        ["Q"] = 0x51,
        ["R"] = 0x52,
        ["S"] = 0x53,
        ["T"] = 0x54,
        ["U"] = 0x55,
        ["V"] = 0x56,
        ["W"] = 0x57,
        ["X"] = 0x58,
        ["Y"] = 0x59,
        ["Z"] = 0x5A,
        ["0"] = 0x30,
        ["1"] = 0x31,
        ["2"] = 0x32,
        ["3"] = 0x33,
        ["4"] = 0x34,
        ["5"] = 0x35,
        ["6"] = 0x36,
        ["7"] = 0x37,
        ["8"] = 0x38,
        ["9"] = 0x39,
    };

    public static readonly Dictionary<string, ushort> ScanCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ESC"] = 0x01,
        ["ENTER"] = 0x1C,
        ["TAB"] = 0x0F,
        ["SPACE"] = 0x39,
        ["A"] = 0x1E,
        ["B"] = 0x30,
        ["C"] = 0x2E,
        ["D"] = 0x20,
        ["E"] = 0x12,
        ["F"] = 0x21,
        ["G"] = 0x22,
        ["H"] = 0x23,
        ["I"] = 0x17,
        ["J"] = 0x24,
        ["K"] = 0x25,
        ["L"] = 0x26,
        ["M"] = 0x32,
        ["N"] = 0x31,
        ["O"] = 0x18,
        ["P"] = 0x19,
        ["Q"] = 0x10,
        ["R"] = 0x13,
        ["S"] = 0x1F,
        ["T"] = 0x14,
        ["U"] = 0x16,
        ["V"] = 0x2F,
        ["W"] = 0x11,
        ["X"] = 0x2D,
        ["Y"] = 0x15,
        ["Z"] = 0x2C,
        ["0"] = 0x0B,
        ["1"] = 0x02,
        ["2"] = 0x03,
        ["3"] = 0x04,
        ["4"] = 0x05,
        ["5"] = 0x06,
        ["6"] = 0x07,
        ["7"] = 0x08,
        ["8"] = 0x09,
        ["9"] = 0x0A,
    };

    public async Task SendKeyAsync(string keyName, int holdMs = 50, CancellationToken ct = default)
    {
        if (_useInterception && _interceptionContext != IntPtr.Zero && ScanCodes.TryGetValue(keyName, out var scanCode))
        {
            var down = new InterceptionKeyStroke { Code = scanCode, State = Interception.KeyDown };
            Interception.interception_send_key(_interceptionContext, Interception.KeyboardDevice0, ref down, 1);

            await Task.Delay(holdMs, ct);

            var up = new InterceptionKeyStroke { Code = scanCode, State = Interception.KeyUp };
            Interception.interception_send_key(_interceptionContext, Interception.KeyboardDevice0, ref up, 1);
        }
        else if (VirtualKeys.TryGetValue(keyName, out var vk))
        {
            byte scan = (byte)Win32.MapVirtualKey(vk, 0);
            Win32.keybd_event(vk, scan, Win32.KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            await Task.Delay(holdMs, ct);
            Win32.keybd_event(vk, scan, Win32.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }

    public async Task SendClickAsync(int screenX, int screenY, int holdMs = 40, CancellationToken ct = default)
    {
        Win32.SetCursorPos(screenX, screenY);
        await Task.Delay(15, ct);

        if (_useInterception && _interceptionContext != IntPtr.Zero)
        {
            var down = new InterceptionMouseStroke { State = Interception.MouseLeftDown };
            Interception.interception_send_mouse(_interceptionContext, Interception.MouseDevice0, ref down, 1);

            await Task.Delay(holdMs, ct);

            var up = new InterceptionMouseStroke { State = Interception.MouseLeftUp };
            Interception.interception_send_mouse(_interceptionContext, Interception.MouseDevice0, ref up, 1);
        }
        else
        {
            Win32.mouse_event(Win32.MOUSEEVENTF_LEFTDOWN, screenX, screenY, 0, UIntPtr.Zero);
            await Task.Delay(holdMs, ct);
            Win32.mouse_event(Win32.MOUSEEVENTF_LEFTUP, screenX, screenY, 0, UIntPtr.Zero);
        }
    }

    public async Task SendScrollAsync(int wheelTicks, int stepDelayMs = 90, CancellationToken ct = default)
    {
        const int tickUnit = 120; // 1 standard notch = 120 units
        int totalUnits = wheelTicks * tickUnit;

        // Split large scrolls into steps of ~2-3 notches so UE4 doesn't clamp
        int stepUnits = wheelTicks < 0 ? -240 : 240;
        int remaining = totalUnits;

        while (Math.Abs(remaining) > 0)
        {
            ct.ThrowIfCancellationRequested();

            int currentStep = Math.Abs(remaining) < Math.Abs(stepUnits) ? remaining : stepUnits;
            remaining -= currentStep;

            if (_useInterception && _interceptionContext != IntPtr.Zero)
            {
                var stroke = new InterceptionMouseStroke
                {
                    State = Interception.MouseWheel,
                    Rolling = (short)currentStep
                };
                Interception.interception_send_mouse(_interceptionContext, Interception.MouseDevice0, ref stroke, 1);
            }
            else
            {
                Win32.mouse_event(Win32.MOUSEEVENTF_WHEEL, 0, 0, currentStep, UIntPtr.Zero);
            }

            if (Math.Abs(remaining) > 0)
            {
                await Task.Delay(stepDelayMs, ct);
            }
        }
    }

    public int GaussianDelay(int mean, int sigma, int min, int max)
    {
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = 1.0 - _rng.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        double randNormal = mean + sigma * randStdNormal;
        return Math.Clamp((int)Math.Round(randNormal), min, max);
    }

    public void Dispose()
    {
        if (_interceptionContext != IntPtr.Zero)
        {
            Interception.interception_destroy_context(_interceptionContext);
            _interceptionContext = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}
