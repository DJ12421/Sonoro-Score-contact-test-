// © 2026 RadianceNoMore (Sonoro-Score, MIT).

using System.Drawing;

namespace SonoroScore.Scanner;

/// <summary>
/// Legacy preprocessing for the Windows-OCR fallback path: contrast boost +
/// grayscale + 2× upscale. Measured numbers depend on it — do not retune
/// without re-running the corpus A/B. The Tesseract primary path uses
/// <see cref="EchoFieldPreprocessor"/> instead.
/// </summary>
public static class ImagePreprocessor
{
    /// <summary>
    /// Simple contrast-boost + grayscale.
    /// Returns a new bitmap; caller must dispose.
    /// </summary>
    public static Bitmap EnhanceForOcr(Bitmap src, float contrastFactor = 1.5f)
    {
        var result = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                var c = src.GetPixel(x, y);
                // Grayscale
                float gray = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
                // Contrast stretch around midpoint
                float enhanced = 128f + (gray - 128f) * contrastFactor;
                int v = Math.Clamp((int)enhanced, 0, 255);
                result.SetPixel(x, y, Color.FromArgb(c.A, v, v, v));
            }
        }
        return result;
    }

    /// <summary>Scale bitmap up 2× for better OCR on small crops.</summary>
    public static Bitmap Upscale2x(Bitmap src)
    {
        var scaled = new Bitmap(src.Width * 2, src.Height * 2);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, scaled.Width, scaled.Height);
        return scaled;
    }
}
