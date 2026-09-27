using System;
using System.Runtime.InteropServices;

namespace AlephalSonata.Native;

[StructLayout(LayoutKind.Sequential)]
public struct InterceptionKeyStroke
{
    public ushort Code;
    public ushort State;
    public uint Information;
}

[StructLayout(LayoutKind.Sequential)]
public struct InterceptionMouseStroke
{
    public ushort State;
    public ushort Flags;
    public short Rolling;
    public int X;
    public int Y;
    public uint Information;
}

public static class Interception
{
    public const int KeyboardDevice0 = 1;
    public const int MouseDevice0 = 11;

    public const ushort KeyDown = 0x00;
    public const ushort KeyUp = 0x01;

    public const ushort MouseLeftDown = 0x001;
    public const ushort MouseLeftUp = 0x002;
    public const ushort MouseWheel = 0x400;

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr interception_create_context();

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_destroy_context(IntPtr context);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_send")]
    public static extern int interception_send_key(IntPtr context, int device, ref InterceptionKeyStroke stroke, uint nstroke);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "interception_send")]
    public static extern int interception_send_mouse(IntPtr context, int device, ref InterceptionMouseStroke stroke, uint nstroke);

    private static bool? _isAvailable;

    public static bool IsDriverAvailable()
    {
        if (_isAvailable.HasValue) return _isAvailable.Value;

        try
        {
            var hMod = Win32.LoadLibrary("interception.dll");
            if (hMod == IntPtr.Zero)
            {
                _isAvailable = false;
                return false;
            }

            var ctx = interception_create_context();
            if (ctx != IntPtr.Zero)
            {
                interception_destroy_context(ctx);
                _isAvailable = true;
                return true;
            }

            _isAvailable = false;
            return false;
        }
        catch
        {
            _isAvailable = false;
            return false;
        }
    }
}
