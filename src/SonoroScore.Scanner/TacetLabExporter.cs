// © 2026 RadianceNoMore (Sonoro-Score, MIT).
// Tacet-Lab backup envelope reverse-engineered from Tacet-Lab
// (https://github.com/DJ12421/Tacet-Lab, GPL-3.0):
//   src/storage/database.ts → exportAccount() / validateAccount()
//   src/domain/types.ts → AccountDocument / Echo / AppSettings
//   src/game-data/core.ts → GAME_DATA_VERSION ('nanoka-3.6-catalog')
// Produces a schemaVersion-7 backup Tacet-Lab imports via top-bar
// Export/Restore → Settings & data. See NOTICES.md.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonoroScore.Scanner;

/// <summary>
/// 1-click Tacet-Lab backup exporter (<c>tacet-lab-backup.json</c>).
/// Primary (lossless) export path; GOOD export is a best-effort bridge.
/// </summary>
public static class TacetLabExporter
{
    public const int SchemaVersion = 7;

    /// <summary>Tacet-Lab <c>GAME_DATA_VERSION</c> (core.ts). Our catalog tracks nanoka 3.6.</summary>
    public const string TacetGameDataVersion = "nanoka-3.6-catalog";

    public const string DefaultFileName = "tacet-lab-backup.json";

    private sealed record TacetStatLine(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] double Value);

    private sealed record TacetEcho(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("cost")] int Cost,
        [property: JsonPropertyName("rarity")] int Rarity,
        [property: JsonPropertyName("level")] int Level,
        [property: JsonPropertyName("sonata")] string Sonata,
        [property: JsonPropertyName("mainStat")] TacetStatLine MainStat,
        [property: JsonPropertyName("subStats")] List<TacetStatLine> SubStats,
        [property: JsonPropertyName("locked")] bool Locked,
        [property: JsonPropertyName("excluded")] bool Excluded,
        [property: JsonPropertyName("equippedBy")] string? EquippedBy,
        [property: JsonPropertyName("equippedByName")] string? EquippedByName,
        [property: JsonPropertyName("createdAt")] long CreatedAt,
        [property: JsonPropertyName("source")] string Source);

    private sealed record TacetSettings(
        [property: JsonPropertyName("displayName")] string DisplayName,
        [property: JsonPropertyName("uid")] string Uid,
        [property: JsonPropertyName("privacyMode")] bool PrivacyMode,
        [property: JsonPropertyName("background")] string Background,
        [property: JsonPropertyName("scanIntervalMs")] int ScanIntervalMs,
        [property: JsonPropertyName("roverGender")] string RoverGender,
        [property: JsonPropertyName("liveCharacterArt")] bool LiveCharacterArt,
        [property: JsonPropertyName("scoreWeights")] Dictionary<string, object> ScoreWeights,
        [property: JsonPropertyName("characterSubstatWeights")] Dictionary<string, object> CharacterSubstatWeights,
        [property: JsonPropertyName("characterEnergyRegenMinimums")] Dictionary<string, object> CharacterEnergyRegenMinimums);

    private sealed record TacetBackup(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("gameDataVersion")] string GameDataVersion,
        [property: JsonPropertyName("exportedAt")] string ExportedAt,
        [property: JsonPropertyName("echoes")] List<TacetEcho> Echoes,
        [property: JsonPropertyName("characters")] List<object> Characters,
        [property: JsonPropertyName("weapons")] List<object> Weapons,
        [property: JsonPropertyName("builds")] List<object> Builds,
        [property: JsonPropertyName("equippedLoadouts")] List<object> EquippedLoadouts,
        [property: JsonPropertyName("theorycraftBuilds")] List<object> TheorycraftBuilds,
        [property: JsonPropertyName("teams")] List<object> Teams,
        [property: JsonPropertyName("optimizerProfiles")] List<object> OptimizerProfiles,
        [property: JsonPropertyName("optimizerRuns")] List<object> OptimizerRuns,
        [property: JsonPropertyName("settings")] TacetSettings Settings);

    /// <summary>
    /// Build the backup JSON. Echoes without a known main stat are skipped
    /// (Tacet <c>isEcho</c> requires a valid mainStat); <paramref name="skipped"/>
    /// reports how many were dropped.
    /// </summary>
    public static string Export(IEnumerable<ExportableEcho> echoes, out int skipped)
    {
        var list = new List<TacetEcho>();
        skipped = 0;
        long baseMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int i = 0;

        foreach (var e in echoes)
        {
            string? mainKey = TacetStatKeys.ToTacetKey(e.MainStatKey);
            if (mainKey == null)
            {
                skipped++;
                continue;
            }

            int cost = e.Cost is 1 or 3 or 4 ? e.Cost : 1;
            int rarity = Math.Clamp(e.Rarity, 1, 5);
            int level = Math.Clamp(e.Level, 0, 25);

            var subs = new List<TacetStatLine>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (k, v) in e.Substats)
            {
                string? tk = TacetStatKeys.ToTacetKey(k);
                if (tk == null || !seen.Add(tk)) continue; // Tacet rejects duplicate substat keys
                if (subs.Count >= 5) break;
                subs.Add(new TacetStatLine(tk, v));
            }

            string safeName = new string(e.Name.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(safeName)) safeName = "echo";
            string id = $"sonoro_{baseMs}_{i:000}_{safeName}";
            string equippedName = string.IsNullOrWhiteSpace(e.EquippedBy) ? "" : e.EquippedBy.Trim();

            list.Add(new TacetEcho(
                Id: id,
                Name: e.Name,
                Cost: cost,
                Rarity: rarity,
                Level: level,
                Sonata: e.Sonata ?? "",
                MainStat: new TacetStatLine(mainKey, e.MainStatValue),
                SubStats: subs,
                Locked: false,
                Excluded: false,
                EquippedBy: null, // no Tacet character IDs in scanner; name links on import
                EquippedByName: string.IsNullOrEmpty(equippedName) ? null : equippedName,
                CreatedAt: baseMs + i,
                Source: "scan"));
            i++;
        }

        var backup = new TacetBackup(
            SchemaVersion: SchemaVersion,
            GameDataVersion: TacetGameDataVersion,
            ExportedAt: DateTime.UtcNow.ToString("o"),
            Echoes: list,
            Characters: [],
            Weapons: [],
            Builds: [],
            EquippedLoadouts: [],
            TheorycraftBuilds: [],
            Teams: [],
            OptimizerProfiles: [],
            OptimizerRuns: [],
            Settings: new TacetSettings(
                DisplayName: "Resonator",
                Uid: "",
                PrivacyMode: false,
                Background: "signal",
                ScanIntervalMs: 900,
                RoverGender: "male",
                LiveCharacterArt: true,
                ScoreWeights: new Dictionary<string, object>(),
                CharacterSubstatWeights: new Dictionary<string, object>(),
                CharacterEnergyRegenMinimums: new Dictionary<string, object>()));

        return JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string Export(IEnumerable<ExportableEcho> echoes)
        => Export(echoes, out _);

    /// <summary>Export scan results directly (skips entries with unknown main stat).</summary>
    public static string ExportScans(IEnumerable<EchoScanResult> scans, out int skipped)
    {
        var models = new List<ExportableEcho>();
        int noMainStat = 0;
        foreach (var s in scans)
        {
            var m = ExportableEcho.FromScanResult(s);
            if (m == null) noMainStat++;
            else models.Add(m);
        }
        string json = Export(models, out int skippedModels);
        skipped = noMainStat + skippedModels;
        return json;
    }

    public static async Task<string> SaveAsync(
        IEnumerable<ExportableEcho> echoes,
        string path,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        string json = Export(echoes, out int skipped);
        await File.WriteAllTextAsync(path, json, ct);
        log?.Invoke($"[EXPORT] Tacet-Lab backup: {path} ({echoes.Count()} echoes, {skipped} skipped w/o main stat).");
        return path;
    }
}
