using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonoroScore.Scanner;

/// <summary>Nanoka echo entry from the API or local cache.</summary>
public record NanokaEchoEntry(
    [property: JsonPropertyName("en")]   string Name,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("rank")] int[]  Rarities,
    [property: JsonPropertyName("group")] int[]  Groups,
    [property: JsonPropertyName("intensity")] int Intensity
);

/// <summary>Resolved, scanner-ready echo definition.</summary>
public record EchoCatalogEntry(string Name, int Cost, int[] Rarities, string[] Sonatas)
{
    // Cost derived from intensity: 0=cost1, 1=cost3, 2+=cost4
    public static int CostFromIntensity(int intensity) => intensity switch { 0 => 1, 1 => 3, _ => 4 };
}

/// <summary>
/// Loads and caches the echo catalog from nanoka.cc or a local JSON file.
/// The local cache is stored at <c>echo_catalog.json</c> next to the exe.
/// </summary>
public static class GameDatabase
{
    private const string ApiUrl = "https://static.nanoka.cc/ww/3.6/echo.json";
    private const string CacheFileName = "echo_catalog.json";
    public const string DataVersion = "nanoka-3.6";

    private static EchoCatalogEntry[]? _catalog;

    // Sonata groups mapped to their set name.
    // Group IDs from Nanoka: 10=Freezing Frost,11=Molten Rift,12=Void Thunder,
    // 13=Sierra Gale,14=Celestial Light,15=Sunsinken Eclipse,16=Rejuvenating Glow,
    // 17=Moonlit Clouds,18=Lingering Tunes,19=Frosty Resolve,20=Eternal Radiance,
    // 21=Midnight Veil,22=Empyrean Anthem,23=Luminary Radiance,
    // 33=Havoc,34=Spectro,35=multi
    private static readonly Dictionary<int, string> SonataGroupMap = new()
    {
        [10] = "Freezing Frost",
        [11] = "Molten Rift",
        [12] = "Void Thunder",
        [13] = "Sierra Gale",
        [14] = "Celestial Light",
        [15] = "Sunsinken Eclipse",
        [16] = "Rejuvenating Glow",
        [17] = "Moonlit Clouds",
        [18] = "Lingering Tunes",
        [19] = "Frosty Resolve",
        [20] = "Eternal Radiance",
        [21] = "Midnight Veil",
        [22] = "Empyrean Anthem",
        [23] = "Luminary Radiance",
        [33] = "Havoc Sonata",
        [34] = "Spectro Sonata",
        [35] = "Mixed Sonata",
    };

    public static async Task<EchoCatalogEntry[]> LoadAsync(
        string? localCatalogPath = null,
        bool forceRefresh = false,
        Action<string>? log = null)
    {
        if (_catalog != null && !forceRefresh) return _catalog;

        string cachePath = localCatalogPath
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CacheFileName);

        // Try local cache first (unless forced refresh)
        if (!forceRefresh && File.Exists(cachePath))
        {
            try
            {
                log?.Invoke($"[DB] Loading echo catalog from local cache: {cachePath}");
                _catalog = ParseCacheSafe(await File.ReadAllTextAsync(cachePath));
                if (_catalog != null)
                {
                    log?.Invoke($"[DB] Loaded {_catalog.Length} echoes from cache.");
                    return _catalog;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[DB] Cache read failed ({ex.Message}), falling back to API.");
            }
        }

        // Fetch from nanoka API
        log?.Invoke($"[DB] Fetching echo catalog from {ApiUrl} ...");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        string json = await http.GetStringAsync(ApiUrl);

        // Parse the nanoka format (dict keyed by ID)
        var raw = JsonSerializer.Deserialize<Dictionary<string, NanokaEchoEntry>>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (raw == null) throw new InvalidDataException("Failed to parse nanoka echo catalog.");

        _catalog = raw.Values
            .Select(e => new EchoCatalogEntry(
                Name:     e.Name,
                Cost:     EchoCatalogEntry.CostFromIntensity(e.Intensity),
                Rarities: e.Rarities,
                Sonatas:  e.Groups
                           .Where(g => SonataGroupMap.ContainsKey(g))
                           .Select(g => SonataGroupMap[g])
                           .ToArray()))
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .OrderBy(e => e.Name)
            .ToArray();

        log?.Invoke($"[DB] Fetched and parsed {_catalog.Length} echoes from API.");

        // Save to local cache
        await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(_catalog,
            new JsonSerializerOptions { WriteIndented = false }));
        log?.Invoke($"[DB] Catalog cached at {cachePath}.");

        return _catalog;
    }

    private static EchoCatalogEntry[]? ParseCacheSafe(string json)
    {
        try { return JsonSerializer.Deserialize<EchoCatalogEntry[]>(json); }
        catch { return null; }
    }

    // Allow loading from a user-provided JSON file with same schema as API output
    public static async Task<EchoCatalogEntry[]> LoadFromFileAsync(string path)
    {
        string json = await File.ReadAllTextAsync(path);
        // Try cache format first
        var cached = ParseCacheSafe(json);
        if (cached != null) { _catalog = cached; return cached; }
        // Try nanoka format
        var raw = JsonSerializer.Deserialize<Dictionary<string, NanokaEchoEntry>>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Cannot parse echo database file.");
        _catalog = raw.Values
            .Select(e => new EchoCatalogEntry(
                e.Name, EchoCatalogEntry.CostFromIntensity(e.Intensity),
                e.Rarities,
                e.Groups.Where(g => SonataGroupMap.ContainsKey(g)).Select(g => SonataGroupMap[g]).ToArray()))
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .OrderBy(e => e.Name)
            .ToArray();
        return _catalog;
    }
}
