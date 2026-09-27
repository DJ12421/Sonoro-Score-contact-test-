// © 2026 RadianceNoMore (Sonoro-Score, MIT).
// Invariants: region geometry, roll-table honesty, color-path separation.

using SonoroScore.Scanner;
using Xunit;

namespace SonoroScore.Scanner.Tests;

public sealed class ScannerInvariantTests
{
    [Fact]
    public void RegionNames_CoverThirteenEditableBoxes_NoRarityBand()
    {
        Assert.Equal(13, EchoRegions.RegionNames.Length);
        Assert.Contains("SecondMainStat", EchoRegions.RegionNames);
        Assert.Contains("Substat1", EchoRegions.RegionNames);
        Assert.Contains("Substat5", EchoRegions.RegionNames);
        Assert.DoesNotContain("RarityBand", EchoRegions.RegionNames);
        Assert.DoesNotContain("SubstatsBlock", EchoRegions.RegionNames);
    }

    [Fact]
    public void SubstatsBlock_EqualsUnionOfFiveSlots()
    {
        var slots = new[] { EchoRegions.Substat1, EchoRegions.Substat2, EchoRegions.Substat3, EchoRegions.Substat4, EchoRegions.Substat5 };
        var block = EchoRegions.SubstatsBlock;
        Assert.Equal(slots.Min(s => s.X), block.X);
        Assert.Equal(slots.Min(s => s.Y), block.Y);
        Assert.Equal(slots.Max(s => s.Right), block.Right, precision: 5);
        Assert.Equal(slots.Max(s => s.Bottom), block.Bottom, precision: 5);
    }

    [Fact]
    public void RarityBand_IsFixedColorCrop()
    {
        // Rarity is color-only (no text box): fixed rect, outside the override system.
        var a = EchoRegions.RarityBand;
        var b = EchoRegions.RarityBand;
        Assert.Equal(a, b);
        Assert.True(a.Width > 0 && a.Height > 0);
    }

    [Fact]
    public void Overrides_RoundTrip_ThroughSaveReload()
    {
        string path = Path.Combine(Path.GetTempPath(), $"regions_test_{Guid.NewGuid():N}.json");
        try
        {
            var snap = EchoRegions.Snapshot();
            var edited = new Dictionary<string, System.Drawing.RectangleF>(snap)
            {
                ["Substat1"] = new System.Drawing.RectangleF(0.05f, 0.2f, 0.9f, 0.04f),
            };
            EchoRegions.SaveOverrides(edited, path);
            Assert.True(EchoRegions.HasOverrides);
            Assert.Equal(0.05f, EchoRegions.Substat1.X);
            Assert.Equal(0.9f, EchoRegions.Substat1.Width);
        }
        finally
        {
            EchoRegions.ResetOverrides(path);
            if (File.Exists(path)) File.Delete(path);
        }
        Assert.False(EchoRegions.HasOverrides);
        Assert.NotEqual(0.05f, EchoRegions.Substat1.X);
    }

    [Fact]
    public void TunableRolls_NeverFabricates_MissedRowStaysAbsent()
    {
        // A fully-missed substat row has no key at all; at the roll layer the
        // equivalent guarantee is: far-off values resolve to null, never to a
        // fabricated "closest" roll outside tolerance.
        var (exact, exactConf) = TunableRolls.Resolve(StatKey.CritRate, 6.3f);
        Assert.Equal(6.3f, exact);
        Assert.True(exactConf > 0.9f);

        var (far, farConf) = TunableRolls.Resolve(StatKey.CritRate, 50f);
        Assert.Null(far);
        Assert.True(farConf <= 0.5f);

        var (flat, _) = TunableRolls.Resolve(StatKey.Hp, 99999f);
        Assert.Null(flat);
    }

    [Fact]
    public void TunableRolls_OcrDigitSwap_RecoversOneSevenConfusion()
    {
        // 1↔7 is the classic OCR confusion: 1.5 is not a roll, but swapping
        // 1→7 gives 7.5, which is — recovered at swap confidence.
        var (snapped, conf) = TunableRolls.Resolve(StatKey.CritRate, 1.5f);
        Assert.Equal(7.5f, snapped);
        Assert.Equal(0.85f, conf);
    }
}
