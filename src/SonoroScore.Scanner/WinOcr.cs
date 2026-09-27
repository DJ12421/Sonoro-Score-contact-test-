using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SonoroScore.Scanner;

/// <summary>
/// OCR wrapper using Windows.Media.Ocr (WinRT, zero-dependency, ships with Windows 10+).
/// Falls back gracefully if OCR engine is unavailable.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class WinOcr
{
    private static OcrEngine? _engine;

    public static bool IsAvailable => OcrEngine.IsLanguageSupported(
        Windows.Globalization.Language.CurrentInputMethodLanguageTag is string tag
            ? new Windows.Globalization.Language(tag)
            : new Windows.Globalization.Language("en-US"));

    private static OcrEngine GetEngine()
    {
        if (_engine != null) return _engine;
        // Try en-US first
        var lang = new Windows.Globalization.Language("en-US");
        _engine = OcrEngine.IsLanguageSupported(lang)
            ? OcrEngine.TryCreateFromLanguage(lang)
            : OcrEngine.TryCreateFromUserProfileLanguages();
        return _engine ?? throw new InvalidOperationException(
            "Windows OCR engine is not available. Ensure the English language pack is installed.");
    }

    /// <summary>
    /// Run OCR on the given bitmap crop and return the extracted text.
    /// </summary>
    public static async Task<string> RecognizeAsync(Bitmap bmp)
    {
        var engine = GetEngine();

        // Convert GDI Bitmap → SoftwareBitmap via stream
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        var softBmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(softBmp);
        return string.Join("\n", result.Lines.Select(l => l.Text));
    }

    /// <summary>
    /// Run OCR on the given bitmap crop and return detected lines with their Y/X bounds.
    /// </summary>
    public static async Task<List<OcrLineInfo>> RecognizeLinesWithBoundsAsync(Bitmap bmp)
    {
        var engine = GetEngine();

        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        var softBmp = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var result = await engine.RecognizeAsync(softBmp);
        var list = new List<OcrLineInfo>();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0) continue;
            double y = line.Words.Average(w => w.BoundingRect.Y);
            double x = line.Words.First().BoundingRect.X;
            double w = line.Words.Last().BoundingRect.Right - x;
            double h = line.Words.Max(w => w.BoundingRect.Height);
            list.Add(new OcrLineInfo(line.Text, y, x, w, h));
        }
        return list;
    }
}

public record OcrLineInfo(string Text, double Y, double X, double Width, double Height);

