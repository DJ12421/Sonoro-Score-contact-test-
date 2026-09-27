using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AlephalSonata.Native;

namespace AlephalSonata.Automation;

public static class ScreenCapturer
{
    public static Bitmap? CaptureGameWindow(string titleHint = WindowManager.DefaultGameTitle)
    {
        if (!WindowManager.GetGameBounds(out var bounds, titleHint))
        {
            return null;
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
        }
        return bmp;
    }

    public static string SaveBitmap(Bitmap bmp, string directory, string filename)
    {
        Directory.CreateDirectory(directory);
        string fullPath = Path.Combine(directory, filename);
        bmp.Save(fullPath, ImageFormat.Png);
        return fullPath;
    }
}
