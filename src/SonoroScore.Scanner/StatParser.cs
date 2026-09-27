using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SonoroScore.Scanner;

public enum StatKey
{
    Unknown,
    Hp, Atk, Def,
    HpPercent, AtkPercent, DefPercent,
    CritRate, CritDamage,
    EnergyRegen,
    BasicDamage, HeavyDamage, SkillDamage, LiberationDamage,
    SpectroDamage, FusionDamage, GlacioDamage, ElectroDamage, AeroDamage, HavocDamage,
    HealingBonus
}

public record ParsedStat(StatKey Key, float Value, string RawLabel, string RawValue);

/// <summary>
/// Ported from Tacet-Lab's statAliases + parseStatLine in core.ts / parser.ts.
/// Includes robust normalization for OCR artifacts (e.g. "0/0" for "%", "ho Skill" for "Res. Skill").
/// </summary>
public static class StatParser
{
    private static readonly (Regex Pattern, StatKey Key)[] StatAliases =
    [
        (new Regex(@"^hp\s*%$", RegexOptions.IgnoreCase), StatKey.HpPercent),
        (new Regex(@"^atk\s*%$", RegexOptions.IgnoreCase), StatKey.AtkPercent),
        (new Regex(@"^def\s*%$", RegexOptions.IgnoreCase), StatKey.DefPercent),
        (new Regex(@"^hp$", RegexOptions.IgnoreCase), StatKey.Hp),
        (new Regex(@"^atk$", RegexOptions.IgnoreCase), StatKey.Atk),
        (new Regex(@"^def$", RegexOptions.IgnoreCase), StatKey.Def),
        (new Regex(@"crit(?:ical)?\.?\s*rate", RegexOptions.IgnoreCase), StatKey.CritRate),
        (new Regex(@"crit(?:ical)?\.?\s*(?:dmg|damage)", RegexOptions.IgnoreCase), StatKey.CritDamage),
        (new Regex(@"energy(?:\s*regen)?", RegexOptions.IgnoreCase), StatKey.EnergyRegen),
        (new Regex(@"basic\s*attack", RegexOptions.IgnoreCase), StatKey.BasicDamage),
        (new Regex(@"heavy\s*attack", RegexOptions.IgnoreCase), StatKey.HeavyDamage),
        (new Regex(@"(?:res\.?|resonance|ho)\s*skill", RegexOptions.IgnoreCase), StatKey.SkillDamage),
        (new Regex(@"(?:res\.?|resonance)?\s*liberation", RegexOptions.IgnoreCase), StatKey.LiberationDamage),
        (new Regex(@"spectro\s*dmg", RegexOptions.IgnoreCase), StatKey.SpectroDamage),
        (new Regex(@"fusion\s*dmg", RegexOptions.IgnoreCase), StatKey.FusionDamage),
        (new Regex(@"glacio\s*dmg", RegexOptions.IgnoreCase), StatKey.GlacioDamage),
        (new Regex(@"electro\s*dmg", RegexOptions.IgnoreCase), StatKey.ElectroDamage),
        (new Regex(@"aero\s*dmg", RegexOptions.IgnoreCase), StatKey.AeroDamage),
        (new Regex(@"havoc\s*dmg", RegexOptions.IgnoreCase), StatKey.HavocDamage),
        (new Regex(@"healing\s*bonus", RegexOptions.IgnoreCase), StatKey.HealingBonus),
    ];

    public static string NormalizeOcrArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // Clean leading non-letters like "+ ATK", "( ATK", "✦ HP" -> "ATK", "HP"
        string s = Regex.Replace(text, @"^[\(\[\{<\|+*~✦\-•\s]+", "");
        // Clean trailing OCR junk like "07)" or "12.6]": keep letters, digits, %, dots, spaces.
        s = Regex.Replace(s, @"[^0-9a-zA-Z%\.\s]+$", "");
        // Normalize OCR percentage artifacts: "30.00/0" -> "30.0%", "10.90/0" -> "10.9%", "69/0" -> "6.9%"
        s = Regex.Replace(s, @"(\d+)\s*(?:0/0|/0|o/o|O/O)", "$1%");
        return s.Trim();
    }

    /// <summary>True for stat keys whose values are always percentages.</summary>
    public static bool IsPercentKey(StatKey key) => key is not StatKey.Unknown
        and not StatKey.Hp and not StatKey.Atk and not StatKey.Def;

    public static StatKey? MatchLabel(string label)
    {
        string cleaned = NormalizeOcrArtifacts(label);
        foreach (var (pattern, key) in StatAliases)
        {
            if (pattern.IsMatch(cleaned)) return key;
        }
        return null;
    }

    /// <summary>
    /// Parses a single OCR line like "Crit. Rate 22.0%" or "( ATK 30.00/0".
    /// </summary>
    public static ParsedStat? ParseLine(string line)
    {
        string cleaned = NormalizeOcrArtifacts(line);
        if (string.IsNullOrWhiteSpace(cleaned)) return null;

        // Try extracting value from the end of the line
        var match = Regex.Match(cleaned, @"^(.+?)\s+([\d,]+\.?\d*)\s*(%?)$");
        if (!match.Success) return null;

        string labelPart = match.Groups[1].Value.Trim();
        string valueStr  = match.Groups[2].Value.Replace(",", "");
        bool isPercent   = match.Groups[3].Value == "%";

        if (!float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            return null;

        StatKey? key = MatchLabel(labelPart);
        if (key == null) return null;

        // Decimal recovery for percentages: e.g. "840%" -> "8.4%" or "69%" -> "6.9%"
        if (isPercent && value > 50 && key is not StatKey.Hp and not StatKey.Atk and not StatKey.Def)
        {
            while (value > 50) value /= 10f;
        }

        // Percentage vs Flat differentiation for HP/ATK/DEF
        if (key == StatKey.Hp && isPercent) key = StatKey.HpPercent;
        else if (key == StatKey.Atk && isPercent) key = StatKey.AtkPercent;
        else if (key == StatKey.Def && isPercent) key = StatKey.DefPercent;

        return new ParsedStat(key.Value, value, labelPart, match.Groups[2].Value + match.Groups[3].Value);
    }

    /// <summary>
    /// Pairs a list of label lines and a list of value lines (2-column crop layout).
    /// Filters out non-stat lines like "Echo Skill".
    /// </summary>
    public static List<ParsedStat> ParseColumns(IEnumerable<string> rawLabels, IEnumerable<string> rawValues)
    {
        var cleanLabels = new List<(StatKey Key, string Raw)>();
        foreach (var lbl in rawLabels)
        {
            string s = NormalizeOcrArtifacts(lbl);
            // Ignore footer text
            if (Regex.IsMatch(s, @"echo\s*skill", RegexOptions.IgnoreCase)) continue;
            var key = MatchLabel(s);
            if (key != null)
            {
                cleanLabels.Add((key.Value, s));
            }
        }

        var cleanValues = new List<(float Value, bool IsPercent, string Raw)>();
        foreach (var val in rawValues)
        {
            string s = NormalizeOcrArtifacts(val);
            var m = Regex.Match(s, @"([\d,]+\.?\d*)\s*(%?)");
            if (m.Success && float.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                bool isPct = m.Groups[2].Value == "%";
                if (isPct && v > 50)
                {
                    while (v > 50) v /= 10f;
                }
                cleanValues.Add((v, isPct, s));
            }
        }

        var results = new List<ParsedStat>();
        int count = Math.Min(cleanLabels.Count, cleanValues.Count);
        for (int i = 0; i < count; i++)
        {
            var (key, rawLbl) = cleanLabels[i];
            var (val, isPct, rawVal) = cleanValues[i];

            if (key == StatKey.Hp && isPct) key = StatKey.HpPercent;
            else if (key == StatKey.Atk && isPct) key = StatKey.AtkPercent;
            else if (key == StatKey.Def && isPct) key = StatKey.DefPercent;

            results.Add(new ParsedStat(key, val, rawLbl, rawVal));
        }

        return results;
    }
}
