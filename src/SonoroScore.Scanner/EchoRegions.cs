using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;

namespace SonoroScore.Scanner;

/// <summary>
/// Named, normalized [0–1] bounding rectangles for the echo detail panel
/// as shown in the Echo Picker (Character Menu / echo-detail layout).
/// Coordinates are panel-relative (the panel is the cropped right-side strip).
/// Ported from Tacet-Lab regions.ts CHARACTER_MENU_REGIONS + DEFAULT_PANEL_RECTS.
///
/// Runtime overrides: the SS area-config dialog saves user edits to
/// <c>regions.override.json</c> next to the exe; values present there win over
/// the compiled defaults below. Delete the file (or Reset in the dialog) to
/// restore defaults.
/// </summary>
public static class EchoRegions
{
    // Panel rect within a full 16:9 game frame (echo-detail / Character Menu layout)
    public const float PanelX = 0.7815f;
    public const float PanelY = 0.1212f;
    public const float PanelW = 0.1882f;
    public const float PanelH = 0.7792f;

    public const string OverrideFileName = "regions.override.json";

    // ── Compiled defaults ─────────────────────────────────────────────────
    // Calibrated by hand in the SS area-config dialog (2026-09-28) against a
    // 1920x1080 capture → panel 361x841px. User-tuned values supersede the
    // original probe estimates; see regions.override.json history.
    private static readonly Dictionary<string, RectangleF> _defaults = new()
    {
        ["EchoName"]      = new(0f,        0.02f,   0.770f,  0.0395f),
        ["Level"]         = new(0.776f,    0.018f,  0.119f,  0.037f),
        ["Cost"]          = new(0.0215f,   0.0715f, 0.2505f, 0.026f),
        ["MainStatStrip"] = new(0.1065f,   0.1225f, 0.8735f, 0.039f),
        // Second main-stat line under the primary strip.
        ["SecondMainStat"] = new(0.1065f,  0.1695f, 0.8735f, 0.034f),
        // Five individually tuned substat slots; SubstatsBlock follows as union.
        ["Substat1"]      = new(0.1075f,   0.21f,   0.8525f, 0.034f),
        ["Substat2"]      = new(0.1075f,   0.252f,  0.8525f, 0.034f),
        ["Substat3"]      = new(0.1075f,   0.293f,  0.8525f, 0.0365f),
        ["Substat4"]      = new(0.1075f,   0.3375f, 0.8525f, 0.032f),
        ["Substat5"]      = new(0.1075f,   0.3805f, 0.8525f, 0.033f),
        // Sonata icon to the right of the "Sonata Effect" heading.
        ["SonataIcon"]    = new(0.901f,    0.0195f, 0.081f,  0.0385f),
        // Zone B: Echo Skill + Sonata Effect text.
        ["SonataZone"]    = new(0.000f,    0.462f,  1.000f,  0.478f),
        // Zone C: "Equipped by [Character]" footer strip.
        ["OwnerZone"]     = new(0.0295f,   0.956f,  0.9705f, 0.044f),
    };

    /// <summary>Editable region names in stable display order.</summary>
    public static readonly string[] RegionNames =
        ["EchoName", "Level", "Cost", "MainStatStrip", "SecondMainStat",
         "Substat1", "Substat2", "Substat3", "Substat4", "Substat5",
         "SonataIcon", "SonataZone", "OwnerZone"];

    private static readonly object _lock = new();
    private static Dictionary<string, RectangleF>? _overrides;

    static EchoRegions() { Reload(); }

    // ── Public accessors (override wins over default) ─────────────────────
    public static RectangleF EchoName       => Get("EchoName");
    public static RectangleF Level          => Get("Level");
    public static RectangleF Cost           => Get("Cost");
    public static RectangleF MainStatStrip  => Get("MainStatStrip");
    public static RectangleF SecondMainStat => Get("SecondMainStat");
    public static RectangleF Substat1       => Get("Substat1");
    public static RectangleF Substat2       => Get("Substat2");
    public static RectangleF Substat3       => Get("Substat3");
    public static RectangleF Substat4       => Get("Substat4");
    public static RectangleF Substat5       => Get("Substat5");
    public static RectangleF SonataIcon     => Get("SonataIcon");
    public static RectangleF SonataZone     => Get("SonataZone");
    public static RectangleF OwnerZone      => Get("OwnerZone");

    /// <summary>
    /// Unified substat block = bounding union of the five slots.
    /// The pipeline (Y-clustering + HP pixel fallback) keeps using this, so
    /// tuning the slots automatically moves the block.
    /// </summary>
    public static RectangleF SubstatsBlock
    {
        get
        {
            float l = Math.Min(Substat1.X, Math.Min(Substat2.X, Math.Min(Substat3.X, Math.Min(Substat4.X, Substat5.X))));
            float t = Math.Min(Substat1.Y, Math.Min(Substat2.Y, Math.Min(Substat3.Y, Math.Min(Substat4.Y, Substat5.Y))));
            float r = Math.Max(Substat1.Right, Math.Max(Substat2.Right, Math.Max(Substat3.Right, Math.Max(Substat4.Right, Substat5.Right))));
            float b = Math.Max(Substat1.Bottom, Math.Max(Substat2.Bottom, Math.Max(Substat3.Bottom, Math.Max(Substat4.Bottom, Substat5.Bottom))));
            return new RectangleF(l, t, r - l, b - t);
        }
    }

    // Rarity is color-only in WuWa (no text region) — fixed pixel band for the
    // hue classifier. Not editable, not overridable.
    public static RectangleF RarityBand => new(0.003f, 0.016f, 0.320f, 0.042f);

    // Legacy 2-column substat crops (kept, not editable — pipeline uses SubstatsBlock).
    public static RectangleF SubstatsLabels => new(0.080f, 0.200f, 0.650f, 0.220f);
    public static RectangleF SubstatsValues => new(0.730f, 0.200f, 0.250f, 0.220f);

    private static RectangleF Get(string name)
    {
        lock (_lock)
        {
            if (_overrides != null && _overrides.TryGetValue(name, out var o))
                return o;
            return _defaults[name];
        }
    }

    /// <summary>Snapshot of effective regions (override merged over defaults).</summary>
    public static Dictionary<string, RectangleF> Snapshot()
    {
        lock (_lock)
        {
            var snap = new Dictionary<string, RectangleF>(_defaults);
            if (_overrides != null)
                foreach (var (k, v) in _overrides)
                    if (snap.ContainsKey(k)) snap[k] = v;
            return snap;
        }
    }

    public static bool HasOverrides
    {
        get { lock (_lock) { return _overrides is { Count: > 0 }; } }
    }

    public static string OverridePath
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, OverrideFileName);

    /// <summary>(Re)load overrides from disk. Invalid entries fall back to defaults.</summary>
    public static void Reload(string? path = null)
    {
        lock (_lock)
        {
            _overrides = null;
            string file = path ?? OverridePath;
            if (!File.Exists(file)) return;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var loaded = new Dictionary<string, RectangleF>();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!_defaults.ContainsKey(prop.Name)) continue;
                    if (TryParseRect(prop.Value, out var r)) loaded[prop.Name] = r;
                }
                if (loaded.Count > 0) _overrides = loaded;
            }
            catch { /* corrupt file → defaults */ }
        }
    }

    /// <summary>Persist edited regions (relative L/T/R/B) as the override file.</summary>
    public static void SaveOverrides(IReadOnlyDictionary<string, RectangleF> regions, string? path = null)
    {
        var clean = new Dictionary<string, object>();
        foreach (var name in RegionNames)
        {
            if (!regions.TryGetValue(name, out var r)) r = _defaults[name];
            r = Clamp(r);
            clean[name] = new { x = r.X, y = r.Y, w = r.Width, h = r.Height };
        }
        File.WriteAllText(path ?? OverridePath,
            JsonSerializer.Serialize(clean, new JsonSerializerOptions { WriteIndented = true }));
        Reload(path);
    }

    /// <summary>Delete the override file and restore compiled defaults.</summary>
    public static void ResetOverrides(string? path = null)
    {
        try
        {
            string file = path ?? OverridePath;
            if (File.Exists(file)) File.Delete(file);
        }
        catch { }
        Reload(path);
    }

    private static bool TryParseRect(JsonElement el, out RectangleF r)
    {
        r = default;
        try
        {
            float x = (float)el.GetProperty("x").GetDouble();
            float y = (float)el.GetProperty("y").GetDouble();
            float w = (float)el.GetProperty("w").GetDouble();
            float h = (float)el.GetProperty("h").GetDouble();
            if (w <= 0.001f || h <= 0.001f) return false;
            r = Clamp(new RectangleF(x, y, w, h));
            return true;
        }
        catch { return false; }
    }

    private static RectangleF Clamp(RectangleF r)
    {
        float x = Math.Clamp(r.X, 0f, 1f);
        float y = Math.Clamp(r.Y, 0f, 1f);
        float w = Math.Clamp(r.Width, 0.005f, 1f - x);
        float h = Math.Clamp(r.Height, 0.005f, 1f - y);
        return new RectangleF(x, y, w, h);
    }

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
        return CropRegion(frame, new RectangleF(PanelX, PanelY, PanelW, PanelH));
    }
}
