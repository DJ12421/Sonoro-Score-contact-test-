// © 2026 RadianceNoMore (Sonoro-Score, MIT).
// Field-counting rule adapted from Tacet-Lab (https://github.com/DJ12421/Tacet-Lab,
// GPL-3.0) src/scanner/accuracy.ts + docs/ocr-fixtures.md: identity, cost, rarity,
// level, sonata, main stat, and EACH substat count as individual fields; corpus
// accuracy = matched fields ÷ expected fields. See NOTICES.md.

using System.Text.Json.Serialization;

namespace SonoroScore.Scanner;

// ── Fixture sidecar (§2e shape + verified flag + second-main extension) ─────

public sealed record FixtureStatLine(
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("value")] float Value);

public sealed record EchoFixture(
    [property: JsonPropertyName("fixtureVersion")] int FixtureVersion,
    [property: JsonPropertyName("layout")] string Layout,
    [property: JsonPropertyName("resolution")] Dictionary<string, int> Resolution,
    [property: JsonPropertyName("uiScale")] int UiScale,
    [property: JsonPropertyName("panelRect")] Dictionary<string, double> PanelRect,
    [property: JsonPropertyName("fieldRects")] Dictionary<string, Dictionary<string, double>> FieldRects,
    [property: JsonPropertyName("sourceImage")] string SourceImage,
    [property: JsonPropertyName("verified")] bool Verified,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("cost")] int? Cost,
    [property: JsonPropertyName("rarity")] int? Rarity,
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("sonata")] string? Sonata,
    [property: JsonPropertyName("mainStat")] FixtureStatLine? MainStat,
    [property: JsonPropertyName("secondMainStat")] FixtureStatLine? SecondMainStat,
    [property: JsonPropertyName("subStats")] List<FixtureStatLine> SubStats)
{
    public const int CurrentVersion = 1;
}

// ── Field-level scorer ──────────────────────────────────────────────────────

public sealed record FieldScore(string Field, string? Expected, string? Actual, bool Match);

public sealed record EchoAccuracyReport(IReadOnlyList<FieldScore> Fields)
{
    public int Matched => Fields.Count(f => f.Match);
    public int Total => Fields.Count;
    public double Rate => Total == 0 ? 1.0 : (double)Matched / Total;
}

/// <summary>
/// Scores one scan against one fixture, field by field. Substats compare as an
/// order-free multiset (detection order is not contractual); floats match
/// within 0.051. Null/empty on both sides counts as a match (absent field).
/// </summary>
public static class EchoAccuracy
{
    private const float ValueTolerance = 0.051f;

    public static EchoAccuracyReport Score(EchoScanResult actual, EchoFixture expected)
    {
        var fields = new List<FieldScore>();

        string? actualName = actual.EchoName?.Value as string;
        if (string.IsNullOrEmpty(actualName) || actualName == "UNKNOWN") actualName = null;
        fields.Add(ScoreField("identity", expected.Name, actualName, OrdinalIgnoreCase: true));
        fields.Add(ScoreField("cost", expected.Cost?.ToString(), ToIntString(actual.Cost?.Value)));
        fields.Add(ScoreField("rarity", expected.Rarity?.ToString(), ToIntString(actual.Rarity?.Value)));
        fields.Add(ScoreField("level", expected.Level?.ToString(), ToIntString(actual.Level?.Value)));
        fields.Add(ScoreField("sonata", expected.Sonata, actual.Sonata?.Value as string, OrdinalIgnoreCase: true));
        fields.Add(ScoreStat("mainStat", expected.MainStat,
            actual.MainStatKey?.Value as string, ToFloat(actual.MainStatValue?.Value)));

        if (expected.SecondMainStat?.Key != null)
            fields.Add(ScoreStat("secondMainStat", expected.SecondMainStat,
                actual.SecondMainStatKey?.Value as string, ToFloat(actual.SecondMainStatValue?.Value)));

        // Substats: greedy multiset match on (key, value±tol).
        var remaining = actual.Substats
            .Select(s => (Key: s.Key, Value: s.SnappedValue ?? s.Value))
            .ToList();
        int subIndex = 0;
        foreach (var exp in expected.SubStats)
        {
            subIndex++;
            int hit = remaining.FindIndex(a =>
                string.Equals(a.Key, exp.Key, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(a.Value - exp.Value) <= ValueTolerance);
            if (hit >= 0)
            {
                var a = remaining[hit];
                remaining.RemoveAt(hit);
                fields.Add(new FieldScore($"substat{subIndex}", $"{exp.Key} {exp.Value}", $"{a.Key} {a.Value}", true));
            }
            else
            {
                fields.Add(new FieldScore($"substat{subIndex}", $"{exp.Key} {exp.Value}", null, false));
            }
        }
        int extra = 0;
        foreach (var a in remaining)
        {
            extra++;
            fields.Add(new FieldScore($"substat-extra{extra}", null, $"{a.Key} {a.Value}", false));
        }

        return new EchoAccuracyReport(fields);
    }

    private static FieldScore ScoreField(string field, string? expected, string? actual, bool OrdinalIgnoreCase = false)
    {
        if (string.IsNullOrEmpty(expected)) expected = null;
        if (string.IsNullOrEmpty(actual)) actual = null;
        bool match = OrdinalIgnoreCase
            ? string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)
            : expected == actual;
        return new FieldScore(field, expected, actual, match);
    }

    private static FieldScore ScoreStat(string field, FixtureStatLine? expected, string? actualKey, float? actualValue)
    {
        if (expected?.Key == null)
            return new FieldScore(field, null, actualKey, actualKey == null);
        bool match = string.Equals(expected.Key, actualKey, StringComparison.OrdinalIgnoreCase)
            && actualValue.HasValue && Math.Abs(expected.Value - actualValue.Value) <= ValueTolerance;
        return new FieldScore(field,
            $"{expected.Key} {expected.Value}",
            actualKey != null ? $"{actualKey} {actualValue}" : null,
            match);
    }

    private static string? ToIntString(object? v) => v switch
    {
        int i => i.ToString(),
        long l => l.ToString(),
        System.Text.Json.JsonElement je when je.TryGetInt32(out int ji) => ji.ToString(),
        _ => v?.ToString(),
    };

    private static float? ToFloat(object? v) => v switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        long l => l,
        System.Text.Json.JsonElement je when je.TryGetSingle(out float jf) => jf,
        System.Text.Json.JsonElement je when je.TryGetDouble(out double jd) => (float)jd,
        _ => null,
    };
}
