using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace SonoroScore;

/// <summary>
/// Persists SS window sizes + splitter positions across sessions
/// (<c>ss_layout.json</c> next to the exe). Best-effort: missing or corrupt
/// files simply yield no saved values and the built-in defaults apply.
/// </summary>
internal static class GuiLayout
{
    private static string LayoutPath
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ss_layout.json");

    public static Dictionary<string, int> Load()
    {
        try
        {
            if (!File.Exists(LayoutPath)) return new Dictionary<string, int>();
            using var doc = JsonDocument.Parse(File.ReadAllText(LayoutPath));
            var map = new Dictionary<string, int>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int v))
                    map[prop.Name] = v;
            }
            return map;
        }
        catch { return new Dictionary<string, int>(); }
    }

    public static void Save(Dictionary<string, int> values)
    {
        try
        {
            var merged = Load();
            foreach (var (k, v) in values) merged[k] = v;
            File.WriteAllText(LayoutPath,
                JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort */ }
    }
}
