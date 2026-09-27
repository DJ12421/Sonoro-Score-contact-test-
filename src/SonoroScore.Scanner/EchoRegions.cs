using System.Drawing;
using System.Drawing.Imaging;

namespace SonoroScore.Scanner;

/// <summary>
/// Named, normalized [0–1] bounding rectangles for the echo detail panel
/// as shown in the Echo Picker (Character Menu / echo-detail layout).
/// Coordinates are panel-relative (the panel is the cropped right-side strip).
/// Ported from Tacet-Lab regions.ts CHARACTER_MENU_REGIONS + DEFAULT_PANEL_RECTS.
/// </summary>
public static class EchoRegions
{
    // Panel rect within a full 16:9 game frame (echo-detail / Character Menu layout)
    public const float PanelX = 0.7815f;
    public const float PanelY = 0.1212f;
    public const float PanelW = 0.1882f;
    public const float PanelH = 0.7792f;

    // Panel-relative regions (x, y, w, h all in [0,1] relative to panel crop)
    public static readonly RectangleF EchoName        = new(0f,        0f,      0.770f,  0.061f);
    public static readonly RectangleF Level           = new(0.776f,    0.018f,  0.119f,  0.037f);
    public static readonly RectangleF Cost            = new(0.018f,    0.065f,  0.241f,  0.031f);
    
    // Unified Main Stat strip (captures both label and value in one line, e.g. "Crit. Rate 22.0%")
    public static readonly RectangleF MainStatStrip   = new(0.080f,    0.110f,  0.900f,  0.055f);
    
    // Substats 2-column crops (avoids OCR reading across columns out of order)
    public static readonly RectangleF SubstatsLabels  = new(0.080f,    0.200f,  0.650f,  0.280f);
    public static readonly RectangleF SubstatsValues  = new(0.730f,    0.200f,  0.250f,  0.280f);

    // Fallback single block
    public static readonly RectangleF SubstatsBlock   = new(0.080f,    0.200f,  0.900f,  0.280f);

    // Rarity band (used for hue-based pixel classification)
    public static readonly RectangleF RarityBand      = new(0.003f,    0.016f,  0.320f,  0.042f);

    /// <summary>
    /// Convert a panel-relative RectangleF to absolute pixel coordinates
    /// given the panel bitmap dimensions.
    /// </summary>
    public static Rectangle ToPixels(RectangleF rel, int panelW, int panelH)
        => new(
            (int)(rel.X * panelW),
            (int)(rel.Y * panelH),
            (int)(rel.Width * panelW),
            (int)(rel.Height * panelH));

    /// <summary>
    /// Crop the panel image to the given panel-relative region.
    /// Returns a new Bitmap that the caller is responsible for disposing.
    /// </summary>
    public static Bitmap CropRegion(Bitmap panel, RectangleF region)
    {
        var pix = ToPixels(region, panel.Width, panel.Height);
        pix.X      = Math.Clamp(pix.X,      0, panel.Width  - 1);
        pix.Y      = Math.Clamp(pix.Y,      0, panel.Height - 1);
        pix.Width  = Math.Clamp(pix.Width,  1, panel.Width  - pix.X);
        pix.Height = Math.Clamp(pix.Height, 1, panel.Height - pix.Y);
        var crop = new Bitmap(pix.Width, pix.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(crop);
        g.DrawImage(panel, new Rectangle(0, 0, pix.Width, pix.Height), pix, GraphicsUnit.Pixel);
        return crop;
    }

    /// <summary>
    /// Extract the panel strip from a full game screenshot.
    /// </summary>
    public static Bitmap ExtractPanel(Bitmap frame)
    {
        var panelRect = new RectangleF(
            PanelX * frame.Width,  PanelY * frame.Height,
            PanelW * frame.Width,  PanelH * frame.Height);
        return CropRegion(frame, new RectangleF(PanelX, PanelY, PanelW, PanelH));
    }
}
