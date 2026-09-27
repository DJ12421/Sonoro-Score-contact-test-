// © 2026 RadianceNoMore (Sonoro-Score, MIT).
// Fixture-corpus runner: 1080p panel PNG + verified/unverified sidecar per sample.
// Unverified fixtures pin current output exactly (any drift fails → re-baseline).
// Verified fixtures score toward the 0.95 corpus floor. See TODO.md §6.

using System.Text.Json;
using System.Drawing;
using SonoroScore.Scanner;
using Xunit;
using Xunit.Abstractions;

namespace SonoroScore.Scanner.Tests;

public sealed class EchoFixtureTests
{
    private const double VerifiedAccuracyFloor = 0.95;

    private readonly ITestOutputHelper _log;

    public EchoFixtureTests(ITestOutputHelper log)
    {
        _log = log;
    }

    [Fact]
    public async Task FixtureCorpus_RulesHold()
    {
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "fixtures", "echoes", "english-1080p");
        Assert.True(Directory.Exists(dir), $"Fixture dir missing: {dir}");
        string[] sidecars = Directory.GetFiles(dir, "*.json").OrderBy(f => f).ToArray();
        Assert.NotEmpty(sidecars);

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var fixtures = sidecars
            .Select(f => JsonSerializer.Deserialize<EchoFixture>(File.ReadAllText(f), opts)!)
            .ToList();

        // Minimal catalog built from fixture truths (no network): names drive the
        // fuzzy match; costs/sonatas stay consistent because they share the source.
        var catalog = fixtures
            .Where(f => !string.IsNullOrEmpty(f.Name))
            .GroupBy(f => f.Name!)
            .Select(g =>
            {
                var first = g.First();
                string[] sonatas = g.Select(x => x.Sonata).Where(s => !string.IsNullOrEmpty(s)).Distinct()!.Cast<string>().ToArray();
                return new EchoCatalogEntry(first.Name!, first.Cost ?? 1, [5], sonatas);
            })
            .ToArray();

        var recognizer = new EchoRecognizer(catalog);
        int verifiedMatched = 0, verifiedTotal = 0;
        var failures = new List<string>();

        foreach (var fixture in fixtures)
        {
            string png = Path.Combine(dir, fixture.SourceImage);
            Assert.True(File.Exists(png), $"Fixture image missing: {png}");
            Bitmap panel = new Bitmap(png); // ownership transfers to RecognizePanelAsync
            EchoScanResult scan = await recognizer.RecognizePanelAsync(panel, fixture.SourceImage);
            EchoAccuracyReport acc = EchoAccuracy.Score(scan, fixture);

            string? name = scan.EchoName?.Value as string;
            _log.WriteLine($"{fixture.SourceImage} verified={fixture.Verified} " +
                           $"rate={acc.Rate:P1} name={name} " +
                           $"miss=[{string.Join(",", acc.Fields.Where(f => !f.Match).Select(f => f.Field))}]");

            if (!fixture.Verified)
            {
                // Regression pin: output must not drift silently.
                if (acc.Rate < 1.0)
                    failures.Add($"{fixture.SourceImage}: unverified snapshot drifted ({acc.Rate:P1}): " +
                                 string.Join("; ", acc.Fields.Where(f => !f.Match).Select(f => $"{f.Field} exp={f.Expected} got={f.Actual}")));
            }
            else
            {
                verifiedMatched += acc.Matched;
                verifiedTotal += acc.Total;
            }
        }

        double verifiedRate = verifiedTotal == 0 ? double.NaN : (double)verifiedMatched / verifiedTotal;
        _log.WriteLine($"verified fields: {verifiedMatched}/{verifiedTotal} " +
                       (double.IsNaN(verifiedRate) ? "(no verified fixtures yet)" : $"= {verifiedRate:P1} (floor {VerifiedAccuracyFloor:P0})"));

        Assert.True(failures.Count == 0,
            "Unverified fixture drift (re-baseline sidecars if the change is intended):\n" + string.Join("\n", failures));
        if (verifiedTotal > 0)
            Assert.True(verifiedRate >= VerifiedAccuracyFloor,
                $"Verified corpus accuracy {verifiedRate:P1} below floor {VerifiedAccuracyFloor:P0}.");
    }
}
