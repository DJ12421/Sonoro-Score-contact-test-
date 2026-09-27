// © 2026 RadianceNoMore (Sonoro-Score, MIT).
// Tacet-Lab envelope (schemaVersion 7 / AccountDocument) reverse-engineered from
// Tacet-Lab (https://github.com/DJ12421/Tacet-Lab, GPL-3.0) src/storage/database.ts
// (exportAccount) + src/domain/types.ts. See NOTICES.md.

using System.Text.Json.Serialization;

namespace SonoroScore.Scanner;

/// <summary>
/// Lossless-ish echo DTO shared by both exporters.
/// Build via <see cref="FromScanResult"/> (CLI/debugger) or
/// <c>EchoReviewItem → ExportableEcho</c> mapping in the GUI.
/// NOTE: the second main stat lives in SS verified JSON only — Tacet-Lab and
/// GOOD schemas carry a single main stat, so exporters keep the primary.
/// </summary>
public sealed record ExportableEcho(
    string Name,
    int Cost,
    int Rarity,
    int Level,
    string Sonata,
    string EquippedBy,
    string MainStatKey,
    float MainStatValue,
    IReadOnlyList<(string Key, float Value)> Substats)
{
    public static ExportableEcho? FromScanResult(EchoScanResult scan)
    {
        string name = EchoReviewCompat.ExtractString(scan.EchoName?.Value, "UNKNOWN");
        int cost = EchoReviewCompat.ExtractInt(scan.Cost?.Value, 1);
        int rarity = EchoReviewCompat.ExtractInt(scan.Rarity?.Value, 5);
        int level = EchoReviewCompat.ExtractInt(scan.Level?.Value, 0);
        string sonata = EchoReviewCompat.ExtractString(scan.Sonata?.Value, "");
        string equipped = EchoReviewCompat.ExtractString(scan.EquippedBy?.Value, "");
        string mainKey = EchoReviewCompat.ExtractString(scan.MainStatKey?.Value, "");
        float mainVal = EchoReviewCompat.ExtractFloat(scan.MainStatValue?.Value, 0f);

        if (string.IsNullOrWhiteSpace(mainKey) || string.Equals(mainKey, "Unknown", StringComparison.OrdinalIgnoreCase))
            return null; // Tacet isEcho requires a valid mainStat; GOOD needs mainStatKey too.

        var subs = new List<(string Key, float Value)>();
        foreach (var s in scan.Substats)
        {
            if (string.IsNullOrWhiteSpace(s.Key) || string.Equals(s.Key, "Unknown", StringComparison.OrdinalIgnoreCase))
                continue;
            subs.Add((s.Key, s.Value));
        }

        return new ExportableEcho(name, cost, rarity, level, sonata, equipped, mainKey, mainVal, subs);
    }
}

/// <summary>JsonElement-safe scalar extraction (mirrors EchoReviewItem helpers without GUI dependency).</summary>
internal static class EchoReviewCompat
{
    public static string ExtractString(object? val, string defaultVal = "")
    {
        if (val == null) return defaultVal;
        if (val is string s) return string.IsNullOrWhiteSpace(s) ? defaultVal : s;
        if (val is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var str = je.GetString();
                return string.IsNullOrWhiteSpace(str) ? defaultVal : str;
            }
            if (je.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined)
                return defaultVal;
            return je.ToString();
        }
        return val.ToString() ?? defaultVal;
    }

    public static int ExtractInt(object? val, int defaultVal = 0)
    {
        if (val == null) return defaultVal;
        if (val is int i) return i;
        if (val is long l) return (int)l;
        if (val is System.Text.Json.JsonElement je)
        {
            if (je.TryGetInt32(out int ji)) return ji;
            if (int.TryParse(je.GetString(), out int js)) return js;
        }
        if (int.TryParse(val.ToString(), out int parsed)) return parsed;
        return defaultVal;
    }

    public static float ExtractFloat(object? val, float defaultVal = 0f)
    {
        if (val == null) return defaultVal;
        if (val is float f) return f;
        if (val is double d) return (float)d;
        if (val is int i) return i;
        if (val is System.Text.Json.JsonElement je)
        {
            if (je.TryGetSingle(out float jf)) return jf;
            if (je.TryGetDouble(out double jd)) return (float)jd;
            if (float.TryParse(je.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float js)) return js;
        }
        if (float.TryParse(val.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float parsed)) return parsed;
        return defaultVal;
    }
}

/// <summary>Shared C# StatKey → Tacet camelCase key mapping.</summary>
internal static class TacetStatKeys
{
    public static string? ToTacetKey(string csharpKey)
    {
        if (string.IsNullOrWhiteSpace(csharpKey)) return null;
        string k = csharpKey.Trim();
        if (k.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return null;
        // Hp → hp, HpPercent → hpPercent, CritRate → critRate, …
        return char.ToLowerInvariant(k[0]) + k.Substring(1);
    }
}
