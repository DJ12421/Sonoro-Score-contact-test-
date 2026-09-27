using System.Text.RegularExpressions;

namespace SonoroScore.Scanner;

/// <summary>
/// Levenshtein edit-distance fuzzy matcher — ported from Tacet-Lab's parser.ts.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>Normalize to lowercase alphanumeric only (mirrors Tacet-Lab normalizedIdentity).</summary>
    public static string Normalize(string s)
        => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", "");

    /// <summary>
    /// Levenshtein-based similarity in [0,1].
    /// Substring containment short-circuits to 1.0.
    /// </summary>
    public static float Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;
        if (a.Contains(b) || b.Contains(a)) return 1.0f;
        int dist = EditDistance(a, b);
        int maxLen = Math.Max(a.Length, b.Length);
        return maxLen == 0 ? 1f : 1f - (float)dist / maxLen;
    }

    /// <summary>
    /// Find the best-matching catalog entry for <paramref name="query"/> using
    /// normalized substring then Levenshtein. Returns null if no match above threshold.
    /// Threshold defaults: 0.68 for echo names, 0.76 for sonata names.
    /// </summary>
    public static (T? Entry, float Score) ClosestMatch<T>(
        string query, IEnumerable<T> catalog, Func<T, string> nameSelector, float threshold = 0.68f)
    {
        string normQuery = Normalize(query);
        if (string.IsNullOrEmpty(normQuery)) return (default, 0f);

        T? best = default;
        float bestScore = 0f;

        foreach (var entry in catalog)
        {
            string normName = Normalize(nameSelector(entry));

            // Tier 1: substring containment
            float score;
            if (normQuery.Contains(normName) || normName.Contains(normQuery))
                score = 1.0f;
            else
                score = Similarity(normQuery, normName);

            if (score > bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }

        return bestScore >= threshold ? (best, bestScore) : (default, bestScore);
    }

    private static int EditDistance(string s, string t)
    {
        int m = s.Length, n = t.Length;
        int[] prev = new int[n + 1], curr = new int[n + 1];
        for (int j = 0; j <= n; j++) prev[j] = j;
        for (int i = 1; i <= m; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= n; j++)
            {
                int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[n];
    }
}
