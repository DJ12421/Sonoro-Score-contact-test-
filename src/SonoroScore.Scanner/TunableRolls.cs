namespace SonoroScore.Scanner;

/// <summary>
/// Tunable substat roll values — ported from Tacet-Lab tunable-rolls.ts (updated 2026-07-28).
/// </summary>
public static class TunableRolls
{
    private record Roll(float Value, float Probability);

    private static Roll[] Common(params (float v, float p)[] pairs)
        => pairs.Select(x => new Roll(x.v, x.p)).ToArray();

    private static readonly Dictionary<StatKey, Roll[]> Rolls = new()
    {
        [StatKey.Hp] = Common((320, 6.8f), (360, 7.77f), (390, 20.39f), (430, 24.27f),
                               (470, 17.48f), (510, 14.56f), (540, 5.83f), (580, 2.91f)),
        [StatKey.Atk] = Common((30, 6.8f), (40, 52.43f), (50, 37.86f), (60, 2.91f)),
        [StatKey.Def] = Common((40, 14.56f), (50, 44.66f), (60, 32.04f), (70, 8.74f)),
        [StatKey.CritRate]     = Common((6.3f,23.33f),(6.9f,23.33f),(7.5f,23.33f),(8.1f,8),(8.7f,8),(9.3f,8),(9.9f,3),(10.5f,3)),
        [StatKey.CritDamage]   = Common((12.6f,23.33f),(13.8f,23.33f),(15,23.33f),(16.2f,8),(17.4f,8),(18.6f,8),(19.8f,3),(21,3)),
        [StatKey.EnergyRegen]  = Common((6.8f,6.8f),(7.6f,7.77f),(8.4f,20.39f),(9.2f,24.27f),(10,17.48f),(10.8f,14.56f),(11.6f,5.83f),(12.4f,2.91f)),
    };

    // All percent-based common substats share the same roll table
    private static readonly Roll[] CommonPercent = Common(
        (6.4f, 6.8f), (7.1f, 7.77f), (7.9f, 20.39f), (8.6f, 24.27f),
        (9.4f, 17.48f), (10.1f, 14.56f), (10.9f, 5.83f), (11.6f, 2.91f));

    private static Roll[]? GetRolls(StatKey key)
    {
        if (Rolls.TryGetValue(key, out var r)) return r;
        return key is StatKey.HpPercent or StatKey.AtkPercent or StatKey.DefPercent
                   or StatKey.BasicDamage or StatKey.HeavyDamage or StatKey.SkillDamage
                   or StatKey.LiberationDamage ? CommonPercent : null;
    }

    private static readonly float[] FlatTolerances = [3f]; // hp/atk/def: max(3, value*0.08)

    /// <summary>Returns the exact matching roll, or null.</summary>
    public static float? Exact(StatKey key, float value)
    {
        var rolls = GetRolls(key);
        if (rolls == null) return null;
        var match = rolls.FirstOrDefault(r => MathF.Abs(r.Value - value) < 0.001f);
        return match?.Value;
    }

    /// <summary>Returns the closest roll within tolerance, or null.</summary>
    public static float? Closest(StatKey key, float value)
    {
        var rolls = GetRolls(key);
        if (rolls == null || !float.IsFinite(value)) return null;
        var closest = rolls.MinBy(r => MathF.Abs(r.Value - value))!;
        float tol = (key is StatKey.Hp or StatKey.Atk or StatKey.Def)
            ? MathF.Max(3f, closest.Value * 0.08f)
            : 0.35f;
        return MathF.Abs(closest.Value - value) <= tol ? closest.Value : null;
    }

    /// <summary>
    /// Try exact, then OCR 1↔7 digit swaps, then closest.
    /// Returns (snapped value, confidence).
    /// </summary>
    public static (float? Value, float Confidence) Resolve(StatKey key, float value)
    {
        var exact = Exact(key, value);
        if (exact.HasValue) return (exact.Value, 0.92f);

        // Digit swap: 1↔7 (common OCR confusion)
        string raw = value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c != '1' && c != '7') continue;
            char[] swapped = raw.ToCharArray();
            swapped[i] = c == '1' ? '7' : '1';
            if (float.TryParse(new string(swapped), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float swappedVal))
            {
                var swapExact = Exact(key, swappedVal);
                if (swapExact.HasValue) return (swapExact.Value, 0.85f);
            }
        }

        var closest = Closest(key, value);
        if (closest.HasValue) return (closest.Value, 0.78f);

        return (null, 0.50f);
    }
}
