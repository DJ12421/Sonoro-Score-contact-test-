using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SonoroScore.Scanner;

namespace SonoroScore;

public static class StatDisplayNames
{
    private static readonly Dictionary<string, string> KeyToDisplay = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hp"]                 = "HP",
        ["HpPercent"]          = "HP %",
        ["Atk"]                = "ATK",
        ["AtkPercent"]         = "ATK %",
        ["Def"]                = "DEF",
        ["DefPercent"]         = "DEF %",
        ["CritRate"]           = "Crit. Rate",
        ["CritDamage"]         = "Crit. DMG",
        ["EnergyRegen"]        = "Energy Regen",
        ["BasicDamage"]        = "Basic Attack DMG Bonus",
        ["HeavyDamage"]        = "Heavy Attack DMG Bonus",
        ["SkillDamage"]        = "Res. Skill DMG Bonus",
        ["LiberationDamage"]   = "Res. Liberation DMG Bonus",
        ["SpectroDamage"]      = "Spectro DMG Bonus",
        ["FusionDamage"]       = "Fusion DMG Bonus",
        ["GlacioDamage"]       = "Glacio DMG Bonus",
        ["ElectroDamage"]      = "Electro DMG Bonus",
        ["AeroDamage"]         = "Aero DMG Bonus",
        ["HavocDamage"]        = "Havoc DMG Bonus",
        ["HealingBonus"]       = "Healing Bonus"
    };

    public static string ToDisplay(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        if (KeyToDisplay.TryGetValue(key, out var d)) return d;
        return key;
    }

    public static string FromDisplay(string? display)
    {
        if (string.IsNullOrWhiteSpace(display)) return "";
        foreach (var (k, v) in KeyToDisplay)
        {
            if (string.Equals(v, display, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(k, display, StringComparison.OrdinalIgnoreCase))
                return k;
        }
        return display;
    }

    public static string[] AllDisplayNames => KeyToDisplay.Values.ToArray();
}

public class EditableSubstat
{
    public bool IsActive { get; set; } = true;
    public string StatKey { get; set; } = "Unknown";
    public float Value { get; set; }
    public string RawValue { get; set; } = "";
    public float? SnappedValue { get; set; }
    public float Confidence { get; set; } = 1.0f;
}

public class EchoReviewItem
{
    public string ImagePath { get; set; } = "";
    public string ImageFileName => Path.GetFileName(ImagePath);

    // Editable fields
    public string EchoName { get; set; } = "UNKNOWN";
    public int Cost { get; set; } = 1;
    public int Rarity { get; set; } = 5;
    public int Level { get; set; } = 25;
    public string Sonata { get; set; } = "";
    public string EquippedBy { get; set; } = "";
    public string MainStatKey { get; set; } = "";
    public float MainStatValue { get; set; }
    public string SecondMainStatKey { get; set; } = "";
    public float SecondMainStatValue { get; set; }

    public List<EditableSubstat> Substats { get; set; } = [];

    // Metadata & Evidence
    public float NameConfidence { get; set; }
    public string RawNameOcr { get; set; } = "";
    public string RawMainStatOcr { get; set; } = "";
    public string RawSubstatsOcr { get; set; } = "";
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];

    // Review status
    public bool IsEdited { get; set; }
    public bool IsVerified { get; set; }

    [JsonIgnore]
    public bool IsComplete => !string.IsNullOrEmpty(EchoName) && EchoName != "UNKNOWN"
                              && !string.IsNullOrEmpty(MainStatKey) && MainStatKey != "Unknown"
                              && Substats.Count(s => s.IsActive) >= 4;

    [JsonIgnore]
    public string DisplayStatus
    {
        get
        {
            if (IsVerified) return "✓ Verified";
            if (IsEdited) return "✏ Edited";
            if (Errors.Count > 0) return "✗ Error";
            if (IsComplete) return "🟢 Complete";
            return "🟡 Partial";
        }
    }

    public static string ExtractString(object? val, string defaultVal = "")
    {
        if (val == null) return defaultVal;
        if (val is string s) return string.IsNullOrWhiteSpace(s) ? defaultVal : s;
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String)
            {
                var str = je.GetString();
                return string.IsNullOrWhiteSpace(str) ? defaultVal : str;
            }
            if (je.ValueKind == JsonValueKind.Null || je.ValueKind == JsonValueKind.Undefined)
                return defaultVal;
            return je.ToString();
        }
        return val.ToString() ?? defaultVal;
    }

    public static int ExtractInt(object? val, int defaultVal = 0)
    {
        if (val == null) return defaultVal;
        if (val is int i) return i;
        if (val is JsonElement je)
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
        if (val is int i) return (float)i;
        if (val is JsonElement je)
        {
            if (je.TryGetSingle(out float jf)) return jf;
            if (je.TryGetDouble(out double jd)) return (float)jd;
            if (float.TryParse(je.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float js)) return js;
        }
        if (float.TryParse(val.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) return parsed;
        return defaultVal;
    }

    public static EchoReviewItem FromScanResult(EchoScanResult scan)
    {
        string name = ExtractString(scan.EchoName?.Value, "UNKNOWN");
        int cost = ExtractInt(scan.Cost?.Value, 1);
        int rarity = ExtractInt(scan.Rarity?.Value, 5);
        int level = ExtractInt(scan.Level?.Value, 25);
        string sonata = ExtractString(scan.Sonata?.Value, "");
        string equipped = ExtractString(scan.EquippedBy?.Value, "");
        string mainKey = ExtractString(scan.MainStatKey?.Value, "");
        float mainVal = ExtractFloat(scan.MainStatValue?.Value, 0f);
        string secondMainKey = ExtractString(scan.SecondMainStatKey?.Value, "");
        float secondMainVal = ExtractFloat(scan.SecondMainStatValue?.Value, 0f);

        var item = new EchoReviewItem
        {
            ImagePath       = scan.ImagePath,
            EchoName        = name,
            Cost            = cost,
            Rarity          = rarity,
            Level           = level,
            Sonata          = sonata,
            EquippedBy      = equipped,
            MainStatKey     = mainKey,
            MainStatValue   = mainVal,
            SecondMainStatKey   = secondMainKey,
            SecondMainStatValue = secondMainVal,
            NameConfidence  = scan.EchoName?.Confidence ?? 0f,
            RawNameOcr      = scan.RawNameOcr ?? "",
            RawMainStatOcr  = scan.RawMainStatOcr ?? "",
            RawSubstatsOcr  = scan.RawSubstatsOcr ?? "",
            Warnings        = new List<string>(scan.Warnings),
            Errors          = new List<string>(scan.Errors),
            Substats        = []
        };

        foreach (var sub in scan.Substats)
        {
            item.Substats.Add(new EditableSubstat
            {
                IsActive     = true,
                StatKey      = sub.Key,
                Value        = sub.Value,
                RawValue     = sub.Raw ?? sub.Value.ToString(CultureInfo.InvariantCulture),
                SnappedValue = sub.SnappedValue,
                Confidence   = sub.Confidence
            });
        }

        // Ensure 5 substat slots are always available in the UI
        while (item.Substats.Count < 5)
        {
            item.Substats.Add(new EditableSubstat
            {
                IsActive = false,
                StatKey  = "Unknown",
                Value    = 0f,
                Confidence = 0f
            });
        }

        return item;
    }
}
