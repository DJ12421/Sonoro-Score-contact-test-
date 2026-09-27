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

    /// <summary>Closed text vocabulary (Tacet ocr-pool textWhitelist).</summary>
    public const string TextWhitelist = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?'-–—&()/+%";

    /// <summary>Digit vocabulary for number strips (Tacet ocr-pool numberWhitelist).</summary>
    public const string NumberWhitelist = "0123456789+-.,:%";

    private static readonly Lazy<TesseractEngine> _engine = new(() =>
    {
        // LSTM-only, mirroring Tacet's tesseract.js worker (OEM 1).
        var engine = new TesseractEngine(TessDataPath, Language, EngineMode.LstmOnly);
        engine.SetVariable("preserve_interword_spaces", "1");
        engine.SetVariable("user_defined_dpi", "300");
        engine.SetVariable("tessedit_do_invert", "0"); // input is already dark ink on light paper
        return engine;
    });

    private static TesseractEngine Engine => _engine.Value;

    /// <summary>
    /// Convert a GDI bitmap to a Leptonica Pix via an in-memory PNG
    /// (Tesseract 5.2.0 has no PixConverter; Pix.LoadFromMemory is the bridge).
    /// </summary>
    private static Pix ToPix(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return Pix.LoadFromMemory(ms.ToArray());
    }

    /// <summary>
    /// Run OCR on the given bitmap and return the extracted plain text.
    /// <paramref name="mode"/> selects Tesseract's page segmentation:
    /// <c>SingleLine</c> for one-line strips (level, cost, stat lines),
    /// <c>SingleBlock</c> for uniform text blocks, <c>Auto</c> for mixed zones.
    /// </summary>
    public static async Task<string> RecognizeAsync(
        Bitmap bmp, PageSegMode mode = PageSegMode.Auto, string? whitelist = null)
    {
        return await Task.Run(() =>
        {
            if (whitelist != null) Engine.SetVariable("tessedit_char_whitelist", whitelist);
            using var pix = ToPix(bmp);
            using var page = Engine.Process(pix, (PageSegMode?)mode);
            return page.GetText();
        });
    }

    /// <summary>
    /// Run OCR and return detected lines with basic bounds information.
    /// The returned <see cref="OcrLineInfo"/> matches the type defined in WinOcr.cs.
    /// </summary>
    public static async Task<List<OcrLineInfo>> RecognizeLinesWithBoundsAsync(
        Bitmap bmp, PageSegMode mode = PageSegMode.Auto, string? whitelist = null)
    {
        return await Task.Run(() =>
        {
            if (whitelist != null) Engine.SetVariable("tessedit_char_whitelist", whitelist);
            var result = new List<OcrLineInfo>();
            using var pix = ToPix(bmp);
            using var page = Engine.Process(pix, (PageSegMode?)mode);
            using var iter = page.GetIterator();
            if (iter == null) return result;

            // Iterate over each text line. IsAtBeginningOf guards the first row
            // (ResultIterator starts before the first element in 5.x bindings).
            do
            {
                if (iter.IsAtBeginningOf(PageIteratorLevel.TextLine))
                {
                    var text = iter.GetText(PageIteratorLevel.TextLine);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        double x = 0, y = 0, w = 0, h = 0;
                        if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out Tesseract.Rect box))
                        {
                            x = box.X1; y = box.Y1; w = box.Width; h = box.Height;
                        }
                        result.Add(new OcrLineInfo(text.Trim(), y, x, w, h));
                    }
                }
            } while (iter.Next(PageIteratorLevel.TextLine));

            return result;
        });
    }
}
