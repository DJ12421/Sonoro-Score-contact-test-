using System.Text.Json.Serialization;

namespace SonoroScore.Scanner;

// ── Scan result types ─────────────────────────────────────────────────────────

/// <summary>A recognized field with its raw OCR evidence and a confidence score [0,1].</summary>
public record FieldResult(
    object? Value,
    float Confidence,
    string? Raw = null)
{
    /// Convenience typed accessor — returns null if Value is null or wrong type.
    public T? As<T>() => Value is T v ? v : default;
}

public record SubstatResult(
    string Key,
    float Value,
    float? SnappedValue,
    float Confidence,
    string? Raw = null);

/// <summary>
/// Full structured result from scanning one echo image.
/// Serialized to JSON in the test-suite output.
/// </summary>
public record EchoScanResult
{
    public required string ImageFile       { get; init; }
    public required string ImagePath       { get; init; }
    public required DateTime ScannedAt    { get; init; }

    // ── Identified fields ──────────────────────────────────────────────────
    public FieldResult?  EchoName     { get; init; }
    public FieldResult?  Cost         { get; init; }
    public FieldResult?  Rarity       { get; init; }
    public FieldResult?  Level        { get; init; }
    public FieldResult?  Sonata       { get; init; }
    public FieldResult?  EquippedBy   { get; init; }
    public FieldResult?  MainStatKey  { get; init; }
    public FieldResult?  MainStatValue{ get; init; }
    public List<SubstatResult> Substats { get; init; } = [];

    // ── Debug evidence ─────────────────────────────────────────────────────
    public string? RawNameOcr          { get; init; }
    public string? RawMainStatOcr      { get; init; }
    public string? RawSubstatsOcr      { get; init; }
    public string? RawLevelOcr         { get; init; }
    public string? RawSonataOcr        { get; init; }

    // ── Errors / warnings ─────────────────────────────────────────────────
    public List<string> Errors         { get; init; } = [];
    public List<string> Warnings       { get; init; } = [];

    [JsonIgnore]
    public bool IsComplete => EchoName?.Value != null && MainStatKey?.Value != null && Substats.Count >= 4;
}


/// <summary>
/// Aggregated summary across all images in a test session.
/// </summary>
public record ScanSessionResult
{
    public required string SessionPath  { get; init; }
    public required DateTime RunAt      { get; init; }
    public required string DatabaseVersion { get; init; }
    public int TotalImages              { get; init; }
    public int SuccessfulScans         { get; init; }
    public int CompleteEchoes          { get; init; }
    public float NameDetectionRate     { get; init; }
    public float MainStatDetectionRate { get; init; }
    public float SubstatAvg            { get; init; }
    public required List<EchoScanResult> Results { get; init; }
}
