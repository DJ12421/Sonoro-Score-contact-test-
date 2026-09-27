using System.Drawing;

namespace AlephalSonata.Automation;

public record PointF(float X, float Y);

public class NavigationConfig
{
    // Sidebar Echo Tab icon (4th glyph from top)
    public PointF EchoSidebarFraction { get; set; } = new(0.058f, 0.44f);

    // First equipped Echo slot (top circle)
    public PointF EchoSlotFraction { get; set; } = new(0.766f, 0.264f);

    // Specific click hold durations (Sidebar needs 120ms; slot and cards work at 60ms)
    public int SidebarClickHoldMs { get; set; } = 120;
    public int SlotClickHoldMs { get; set; } = 60;
    public int DefaultClickHoldMs { get; set; } = 60;
    public int KeyHoldMs { get; set; } = 80;

    // Navigation step intervals / wait delays (ms)
    public int AfterKeyCIntervalMs { get; set; } = 1500;         // Delay after 'C' for Character menu camera zoom & render
    public int AfterSidebarClickIntervalMs { get; set; } = 1200; // Delay after clicking Echo Sidebar icon for tab transition
    public int AfterSlotClickIntervalMs { get; set; } = 1200;    // Delay after clicking Equipped Echo slot to load picker modal

    // 5 Rows x 3 Columns picker grid
    public int GridRows { get; set; } = 5;
    public int GridCols { get; set; } = 3;

    // Top-left first card center
    public PointF FirstCellFraction { get; set; } = new(0.142f, 0.2056f);

    // Step between card centers (col step X, row step Y)
    public PointF StepFraction { get; set; } = new(0.07f, 0.1482f);

    // Cursor position during page scrolls (inside grid panel)
    public PointF ScrollAnchorFraction { get; set; } = new(0.21f, 0.5f);

    // Scroll settings
    public int ScrollTicksPerPage { get; set; } = -34;
    public int ScrollEventGapMs { get; set; } = 100;
    public int PageScrollDelayMs { get; set; } = 1200;
    public int LandingDelayMs { get; set; } = 1000;

    // Humanized click cadence
    public int ClickDelayMeanMs { get; set; } = 50;
    public int ClickDelaySigmaMs { get; set; } = 15;
    public int ClickDelayMinMs { get; set; } = 25;
    public int ClickDelayMaxMs { get; set; } = 120;
}
