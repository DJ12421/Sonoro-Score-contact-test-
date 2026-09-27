// © 2026 RadianceNoMore (Sonoro-Score, MIT).
// Field preprocessing ported from Tacet-Lab (https://github.com/DJ12421/Tacet-Lab,
// GPL-3.0) src/scanner/preprocess.worker.ts + ocr-pool.ts engine parameters.
// See NOTICES.md.
//
// Shipped Tacet behavior (NOT the aspirational prose in their architecture doc):
// pad → crop → enlarge ×3 → grayscale → 4/96 percentile normalize → polarity
// correction (8% border mean) → GLOBAL Otsu threshold → strategy cleanups →
// black-on-white with white border. adaptiveThreshold/dilate/erode exist in
// their worker but are never called — deliberately not ported.
//
// Engine parameters mirrored from their ocr-pool.ts: LSTM-only, DPI 300,
// preserve_interword_spaces, tessedit_do_invert=0 (input is already dark ink
// on light paper here), per-kind PSM + char whitelists.

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SonoroScore.Scanner;

/// <summary>Preprocessing strategy per field kind (mirrors Tacet's Strategy).</summary>
public enum FieldStrategy
{
    Name,
    Label,
    Text,
    Substat,
    Visual,
    Plain,
}

/// <summary>
/// Per-region, polarity-aware preprocessing for the Tesseract path.
/// Output is ALWAYS black text on white background (Tesseract is configured
/// with <c>tessedit_do_invert=0</c>). Color classifiers (rarity, sonata icon,
/// HP pixel matcher) must keep using RAW color crops — never this path.
/// </summary>
public static class EchoFieldPreprocessor
{
    /// <summary>
    /// Crop a panel-relative region, preprocess it, and return a new Bitmap
    /// the caller must dispose. Scale is ×3 for text strategies, ×1 otherwise.
    /// </summary>
    public static Bitmap Process(Bitmap panel, RectangleF region, FieldStrategy strategy, int padPx = 0)
    {
        var rel = EchoRegions.ToPixels(region, panel.Width, panel.Height);

        int sx = Math.Clamp(rel.X - padPx, 0, panel.Width - 1);
        int sy = Math.Clamp(rel.Y - padPx, 0, panel.Height - 1);
        int sw = Math.Clamp(rel.Width + padPx * 2, 1, panel.Width - sx);
        int sh = Math.Clamp(rel.Height + padPx * 2, 1, panel.Height - sy);

        int scale = strategy is FieldStrategy.Visual or FieldStrategy.Plain ? 1 : 3;
        int w = Math.Max(1, sw * scale);
        int h = Math.Max(1, sh * scale);

        byte[] gray = new byte[w * h];
        using (var scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(panel, new Rectangle(0, 0, w, h), new Rectangle(sx, sy, sw, sh), GraphicsUnit.Pixel);
            }
            gray = ToGrayscale(scaled);
        }

        if (strategy is FieldStrategy.Visual or FieldStrategy.Plain)
        {
            // Visual/color path: enlarged (or raw-scale) grayscale, no threshold.
            return FromGrayscale(gray, w, h);
        }

        byte[] normalized = Normalize(Percentile(gray, 0.04), Percentile(gray, 0.96), gray);
        byte[] light = EnsureLightBackground(normalized, w, h);
        byte[] binary = GlobalThreshold(light);

        if (strategy == FieldStrategy.Name) binary = RemoveLargeNameArtwork(binary, w, h);
        if (strategy == FieldStrategy.Label) binary = RemoveSmallLabelNoise(binary, w, h);
        if (strategy == FieldStrategy.Substat)
        {
            binary = RemoveSubstatHighlight(binary, w, h);
            binary = TrimSubstatFooter(binary, w, h);
        }

        return RenderBinary(binary, w, h);
    }

    // ── Grayscale I/O (LockBits, no unsafe) ─────────────────────────────────

    private static byte[] ToGrayscale(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] buf = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            byte[] gray = new byte[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    int o = y * stride + x * 4;
                    gray[y * bmp.Width + x] = (byte)Math.Round(buf[o + 2] * 0.2126 + buf[o + 1] * 0.7152 + buf[o] * 0.0722);
                }
            return gray;
        }
        finally { bmp.UnlockBits(data); }
    }

    private static Bitmap FromGrayscale(byte[] gray, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            byte[] buf = new byte[stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte v = gray[y * w + x];
                    int o = y * stride + x * 3;
                    buf[o] = v; buf[o + 1] = v; buf[o + 2] = v;
                }
            Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    // ── Normalize / polarity / Otsu ─────────────────────────────────────────

    private static int Percentile(byte[] values, double ratio)
    {
        int[] histogram = new int[256];
        foreach (byte v in values) histogram[v]++;
        int target = Math.Max(0, Math.Min(values.Length - 1, (int)Math.Round(values.Length * ratio)));
        int count = 0;
        for (int v = 0; v < 256; v++) { count += histogram[v]; if (count >= target) return v; }
        return 255;
    }

    private static byte[] Normalize(int low, int high, byte[] values)
    {
        int range = Math.Max(18, high - low);
        byte[] output = new byte[values.Length];
        for (int i = 0; i < values.Length; i++)
            output[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round((values[i] - low) / (double)range * 255)));
        return output;
    }

    private static double BorderMean(byte[] values, int w, int h)
    {
        int border = Math.Max(1, (int)Math.Round(Math.Min(w, h) * 0.08));
        long sum = 0;
        int count = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (x >= border && x < w - border && y >= border && y < h - border) continue;
                sum += values[y * w + x]; count++;
            }
        return count > 0 ? sum / (double)count : 255.0;
    }

    private static byte[] EnsureLightBackground(byte[] values, int w, int h)
    {
        if (BorderMean(values, w, h) >= 128) return values;
        byte[] output = new byte[values.Length];
        for (int i = 0; i < values.Length; i++) output[i] = (byte)(255 - values[i]);
        return output;
    }

    private static int OtsuThreshold(byte[] values)
    {
        int[] histogram = new int[256];
        foreach (byte v in values) histogram[v]++;
        long totalWeighted = 0;
        for (int v = 0; v < 256; v++) totalWeighted += (long)v * histogram[v];
        long bgWeight = 0, bgWeighted = 0;
        double bestVariance = -1;
        int threshold = 128;
        for (int v = 0; v < 256; v++)
        {
            bgWeight += histogram[v];
            if (bgWeight == 0) continue;
            long fgWeight = values.Length - bgWeight;
            if (fgWeight == 0) break;
            bgWeighted += (long)v * histogram[v];
            double bgMean = bgWeighted / (double)bgWeight;
            double fgMean = (totalWeighted - bgWeighted) / (double)fgWeight;
            double variance = bgWeight * fgWeight * (bgMean - fgMean) * (bgMean - fgMean);
            if (variance > bestVariance) { bestVariance = variance; threshold = v; }
        }
        return threshold;
    }

    private static byte[] GlobalThreshold(byte[] values)
    {
        int threshold = OtsuThreshold(values);
        byte[] output = new byte[values.Length];
        for (int i = 0; i < values.Length; i++) output[i] = values[i] <= threshold ? (byte)0 : (byte)255;
        return output;
    }

    private static Bitmap RenderBinary(byte[] binary, int w, int h)
    {
        var bmp = FromGrayscale(binary, w, h);
        // White quiet border (2.5% of shortest side, min 2px) — Tesseract edge aid.
        int border = Math.Max(2, (int)Math.Round(Math.Min(w, h) * 0.025));
        using var g = Graphics.FromImage(bmp);
        using var white = new SolidBrush(Color.White);
        g.FillRectangle(white, 0, 0, w, border);
        g.FillRectangle(white, 0, h - border, w, border);
        g.FillRectangle(white, 0, 0, border, h);
        g.FillRectangle(white, w - border, 0, border, h);
        return bmp;
    }

    // ── Connected components (4-neighbourhood BFS) ──────────────────────────

    private sealed record Component(List<int> Pixels, int MinX, int MaxX, int MinY, int MaxY);

    private static List<Component> FindComponents(byte[] values, int w, int h)
    {
        var visited = new byte[values.Length];
        var components = new List<Component>();
        var stack = new Stack<int>();
        for (int start = 0; start < values.Length; start++)
        {
            if (values[start] != 0 || visited[start] == 1) continue;
            var pixels = new List<int>();
            int minX = w, maxX = 0, minY = h, maxY = 0;
            visited[start] = 1;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int index = stack.Pop();
                int x = index % w, y = index / w;
                pixels.Add(index);
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                foreach (int next in new[] { index - 1, index + 1, index - w, index + w })
                {
                    if (next < 0 || next >= values.Length || visited[next] == 1 || values[next] != 0) continue;
                    if (Math.Abs(next % w - x) > 1) continue;
                    visited[next] = 1;
                    stack.Push(next);
                }
            }
            components.Add(new Component(pixels, minX, maxX, minY, maxY));
        }
        return components;
    }

    private static byte[] RemoveLargeNameArtwork(byte[] values, int w, int h)
    {
        byte[] output = (byte[])values.Clone();
        double maxTextArea = values.Length * 0.025;
        foreach (var c in FindComponents(values, w, h))
        {
            int cw = c.MaxX - c.MinX + 1, ch = c.MaxY - c.MinY + 1;
            bool largeArtwork = c.Pixels.Count > maxTextArea && (cw > w * 0.1 || ch > h * 0.45);
            bool rightEdgeArtwork = c.MinX > w * 0.5 && (c.MinY == 0 || c.MaxX == w - 1) && ch > h * 0.15;
            if (largeArtwork || rightEdgeArtwork)
                foreach (int i in c.Pixels) output[i] = 255;
        }
        return output;
    }

    private static byte[] RemoveSmallLabelNoise(byte[] values, int w, int h)
    {
        byte[] output = (byte[])values.Clone();
        double minGlyph = h * 0.18;
        var components = FindComponents(values, w, h);
        foreach (var c in components)
        {
            int cw = c.MaxX - c.MinX + 1, ch = c.MaxY - c.MinY + 1;
            if (cw >= minGlyph || ch >= minGlyph) continue;
            double centerX = (c.MinX + c.MaxX) / 2.0;
            bool isLetterDot = components.Any(stem => !ReferenceEquals(stem, c)
                && stem.MaxY - stem.MinY + 1 >= minGlyph
                && c.MaxY < stem.MinY
                && stem.MinY - c.MaxY <= h * 0.18
                && centerX >= stem.MinX - h * 0.03
                && centerX <= stem.MaxX + h * 0.03);
            if (!isLetterDot)
                foreach (int i in c.Pixels) output[i] = 255;
        }
        return output;
    }

    // ── Substat cleanups ────────────────────────────────────────────────────

    private static byte[] RemoveSubstatHighlight(byte[] values, int w, int h)
    {
        byte[] output = (byte[])values.Clone();
        int horizontalLimit = Math.Max(1, (int)(h * 0.34));
        int horizontalRun = (int)(w * 0.62);
        int clearRadius = Math.Max(1, (int)Math.Round(Math.Min(w, h) * 0.018));
        int LongestRun(int y)
        {
            int longest = 0, current = 0;
            for (int x = 0; x < w; x++) { current = values[y * w + x] == 0 ? current + 1 : 0; longest = Math.Max(longest, current); }
            return longest;
        }
        void ClearRow(int center)
        {
            for (int y = Math.Max(0, center - clearRadius); y <= Math.Min(h - 1, center + clearRadius); y++)
                Array.Fill(output, (byte)255, y * w, w);
        }
        for (int y = 0; y < horizontalLimit; y++) if (LongestRun(y) >= horizontalRun) ClearRow(y);
        for (int y = h - horizontalLimit; y < h; y++) if (LongestRun(y) >= horizontalRun) ClearRow(y);
        return output;
    }

    private static byte[] TrimSubstatFooter(byte[] values, int w, int h)
    {
        byte[] output = (byte[])values.Clone();
        int valueStart = (int)(w * 0.7);
        int minimumInk = Math.Max(3, (int)Math.Round((w - valueStart) * 0.02));
        int gapAllowance = Math.Max(1, (int)Math.Round(h * 0.012));
        int bands = 0, lastInkRow = -1, previousInkRow = -gapAllowance - 1;
        for (int y = 0; y < h; y++)
        {
            int ink = 0;
            for (int x = valueStart; x < w; x++) if (values[y * w + x] == 0) ink++;
            if (ink < minimumInk) continue;
            if (y - previousInkRow > gapAllowance) bands++;
            previousInkRow = y; lastInkRow = y;
        }
        if (bands < 3 || lastInkRow < 0) return output;
        int cutoff = Math.Min(h, lastInkRow + Math.Max(3, (int)Math.Round(h * 0.035)));
        Array.Fill(output, (byte)255, cutoff * w, output.Length - cutoff * w);
        return output;
    }
}
