// © 2026 RadianceNoMore (Sonoro-Score, MIT).
// Unit tests for the field-counting rule (no images needed).

using SonoroScore.Scanner;
using Xunit;

namespace SonoroScore.Scanner.Tests;

public sealed class AccuracyScoringTests
{
    private static EchoScanResult Scan(
        string? name = "Sigillum", int? cost = 4, int? rarity = 5, int? level = 25,
        string? sonata = "Trailblazing Star",
        string? mainKey = "CritRate", float? mainVal = 22f,
        params (string Key, float Value)[] subs)
    {
        return new EchoScanResult
        {
            ImageFile = "test.png",
            ImagePath = "test.png",
            ScannedAt = DateTime.UtcNow,
            EchoName = new FieldResult(name, 0.9f),
            Cost = new FieldResult(cost, 0.9f),
            Rarity = new FieldResult(rarity, 0.9f),
            Level = new FieldResult(level, 0.9f),
            Sonata = new FieldResult(sonata, 0.9f),
            MainStatKey = new FieldResult(mainKey, 0.9f),
            MainStatValue = new FieldResult(mainVal, 0.9f),
            Substats = subs.Select(s => new SubstatResult(s.Key, s.Value, s.Value, 0.9f)).ToList(),
        };
    }

    private static EchoFixture Expected(
        string? name = "Sigillum", int? cost = 4, int? rarity = 5, int? level = 25,
        string? sonata = "Trailblazing Star",
        string? mainKey = "critRate", float mainVal = 22f,
        params (string Key, float Value)[] subs)
    {
        return new EchoFixture(1, "echo-detail",
            new Dictionary<string, int> { ["width"] = 1920, ["height"] = 1080 }, 1,
            new Dictionary<string, double>(), new Dictionary<string, Dictionary<string, double>>(),
            "test.png", true, name, cost, rarity, level, sonata,
            mainKey == null ? null : new FixtureStatLine(mainKey, mainVal), null,
            subs.Select(s => new FixtureStatLine(s.Key, s.Value)).ToList());
    }

    [Fact]
    public void PerfectScan_ScoresOne()
    {
        var acc = EchoAccuracy.Score(
            Scan(subs: [("AtkPercent", 30f), ("CritDamage", 13.8f)]),
            Expected(subs: [("atkPercent", 30f), ("critDamage", 13.8f)]));
        Assert.Equal(1.0, acc.Rate);
    }

    [Fact]
    public void NameMismatch_FailsOnlyIdentity()
    {
        var acc = EchoAccuracy.Score(Scan(name: "Glommoth"), Expected(name: "Sigillum"));
        var identity = Assert.Single(acc.Fields, f => f.Field == "identity");
        Assert.False(identity.Match);
        Assert.True(acc.Fields.Where(f => f.Field != "identity").All(f => f.Match));
    }

    [Fact]
    public void Substats_ScoreAsOrderFreeMultiset()
    {
        var acc = EchoAccuracy.Score(
            Scan(subs: [("CritDamage", 13.8f), ("AtkPercent", 30f)]),
            Expected(subs: [("atkPercent", 30f), ("critDamage", 13.8f)]));
        Assert.All(acc.Fields.Where(f => f.Field.StartsWith("substat")), f => Assert.True(f.Match));
    }

    [Fact]
    public void MissingSubstat_FailsThatSlotOnly()
    {
        var acc = EchoAccuracy.Score(
            Scan(subs: [("AtkPercent", 30f)]),
            Expected(subs: [("atkPercent", 30f), ("critDamage", 13.8f)]));
        Assert.Equal(1, acc.Fields.Count(f => f.Field.StartsWith("substat") && f.Match));
        Assert.Equal(1, acc.Fields.Count(f => f.Field.StartsWith("substat") && !f.Match));
    }

    [Fact]
    public void ExtraSubstat_CountsAsFalseField()
    {
        var acc = EchoAccuracy.Score(
            Scan(subs: [("AtkPercent", 30f), ("CritDamage", 13.8f), ("Hp", 470f)]),
            Expected(subs: [("atkPercent", 30f), ("critDamage", 13.8f)]));
        var extra = Assert.Single(acc.Fields, f => f.Field.StartsWith("substat-extra"));
        Assert.False(extra.Match);
    }

    [Fact]
    public void ValueTolerance_CoversFloatRepr()
    {
        var acc = EchoAccuracy.Score(
            Scan(mainKey: "CritRate", mainVal: 22.0001f),
            Expected(mainKey: "critRate", mainVal: 22f));
        Assert.True(acc.Fields.First(f => f.Field == "mainStat").Match);
    }

    [Fact]
    public void AbsentSecondMainStat_NotScored()
    {
        var acc = EchoAccuracy.Score(Scan(), Expected());
        Assert.DoesNotContain(acc.Fields, f => f.Field == "secondMainStat");
    }

    [Fact]
    public void NullVersusNull_CountsAsMatch()
    {
        var acc = EchoAccuracy.Score(Scan(name: null), Expected(name: null));
        Assert.True(acc.Fields.First(f => f.Field == "identity").Match);
    }
}
