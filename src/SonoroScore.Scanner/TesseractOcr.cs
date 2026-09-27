// © 2026 RadianceNoMore (Sonoro-Score, MIT).
// OCR strategy adapted from Tacet-Lab (https://github.com/DJ12421/Tacet-Lab, GPL-3.0).
// See NOTICES.md.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Tesseract;

namespace SonoroScore.Scanner;

/// <summary>
/// OCR wrapper using Tesseract (via the Tesseract NuGet package).
/// Provides the same public API as WinOcr for easy substitution.
/// </summary>
public static class TesseractOcr
{
    // Path to the tessdata folder containing language files (e.g., eng.traineddata).
    // It is resolved relative to the executable's working directory.
    private static readonly string TessDataPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
    private static readonly string Language = "eng";

    /// <summary>True when the tessdata language file is present and the engine initialized.</summary>
    public static bool IsAvailable
    {
        get
        {
            try { return _engine.Value != null; }
            catch { return false; }
        }
    }

    private static readonly Lazy<TesseractEngine> _engine = new(() =>
        new TesseractEngine(TessDataPath, Language, EngineMode.Default));

    private static TesseractEngine Engine => _engine.Value;

    /// <summary>
    /// Run OCR on the given bitmap and return the extracted plain text.
    /// </summary>
    public static async Task<string> RecognizeAsync(Bitmap bmp)
    {
        return await Task.Run(() =>
        {
            using var pix = PixConverter.ToPix(bmp);
            using var page = Engine.Process(pix);
            return page.GetText();
        });
    }

    /// <summary>
    /// Run OCR and return detected lines with basic bounds information.
    /// The returned <see cref="OcrLineInfo"/> matches the type defined in WinOcr.cs.
    /// </summary>
    public static async Task<List<OcrLineInfo>> RecognizeLinesWithBoundsAsync(Bitmap bmp)
    {
        return await Task.Run(() =>
        {
            var result = new List<OcrLineInfo>();
            using var pix = PixConverter.ToPix(bmp);
            using var page = Engine.Process(pix);
            using var iter = page.GetIterator();
            if (iter == null) return result;

            // Iterate over each text line.
            // PageIterator.Next(PageIteratorLevel) is the only valid overload in
            // the Tesseract 5.x .NET binding. IsAtBeginningOf guards the first row.
            do
            {
                if (iter.IsAtBeginningOf(PageIteratorLevel.TextLine))
                {
                    var text = iter.GetText(PageIteratorLevel.TextLine);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        iter.TryGetBoundingBox(PageIteratorLevel.TextLine,
                            out int x1, out int y1, out int x2, out int y2);
                        result.Add(new OcrLineInfo(text.Trim(), y1, x1, x2 - x1, y2 - y1));
                    }
                }
            } while (iter.Next(PageIteratorLevel.TextLine));

            return result;
        });
    }
}
