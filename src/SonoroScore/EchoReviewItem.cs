using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using SonoroScore.Scanner;

namespace SonoroScore;

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
    public string MainStatKey { get; set; } = "";
    public float MainStatValue { get; set; }

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

    public static EchoReviewItem FromScanResult(EchoScanResult scan)
    {
        var item = new EchoReviewItem
        {
            ImagePath       = scan.ImagePath,
            EchoName        = scan.EchoName?.Value as string ?? "UNKNOWN",
            Cost            = scan.Cost?.Value is int c ? c : (scan.Cost?.Value is System.Text.Json.JsonElement jc && jc.TryGetInt32(out int jcv) ? jcv : 1),
            Rarity          = scan.Rarity?.Value is int r ? r : (scan.Rarity?.Value is System.Text.Json.JsonElement jr && jr.TryGetInt32(out int jrv) ? jrv : 5),
            Level           = scan.Level?.Value is int l ? l : (scan.Level?.Value is System.Text.Json.JsonElement jl && jl.TryGetInt32(out int jlv) ? jlv : 25),
            Sonata          = scan.Sonata?.Value as string ?? "",
            MainStatKey     = scan.MainStatKey?.Value as string ?? "",
            MainStatValue   = scan.MainStatValue?.Value is float v ? v : (scan.MainStatValue?.Value is double d ? (float)d : (scan.MainStatValue?.Value is System.Text.Json.JsonElement jv && jv.TryGetSingle(out float jsv) ? jsv : 0f)),
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
                RawValue     = sub.Raw ?? sub.Value.ToString(),
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
