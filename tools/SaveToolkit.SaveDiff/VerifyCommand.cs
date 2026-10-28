using System.Text;
using System.Text.Json.Serialization;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff;

[JsonConverter(typeof(JsonStringEnumConverter<FixtureOutcome>))]
internal enum FixtureOutcome
{
    Pass,
    Fail,

    /// <summary>An archive is not present locally (samples are private); like sample tests, this is not a failure.</summary>
    Skipped,
}

internal sealed record FixtureResult(string FixtureId, FixtureOutcome Outcome, string? Message, FixtureVerification? Verification);

internal static class VerifyCommand
{
    public static int Run(CliArguments args, TextWriter stdout, Func<DecoderResolver> resolver)
    {
        args.ExpectPositionals(1, "savediff verify <manifest.json> [--artifacts DIR] [--fixture ID]");
        var json = CliApp.WantsJson(args);
        var manifestPath = Path.GetFullPath(args.Positionals[0]);
        var manifest = FixtureManifest.Load(manifestPath);
        var artifacts = ArtifactsDirectory(args, manifestPath);
        var only = args.GetAll("fixture");
        var fixtures = manifest.Fixtures.Where(f => only.Count == 0 || only.Contains(f.Id, StringComparer.OrdinalIgnoreCase)).ToList();
        if (fixtures.Count == 0)
        {
            throw new UsageException("No fixture selected.");
        }

        var decoders = new Lazy<DecoderResolver>(resolver);
        var cache = new Dictionary<string, LoadedSaveSet>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var results = fixtures.Select(f => VerifyOne(f, manifest, artifacts, decoders, cache)).ToList();
            CliApp.Emit(args, stdout, json ? DiffJson.Serialize(results) + "\n" : Render(results, artifacts));
            return results.Any(r => r.Outcome == FixtureOutcome.Fail) ? CliApp.ExitDifferent : CliApp.ExitSame;
        }
        finally
        {
            foreach (var loaded in cache.Values)
            {
                loaded.Dispose();
            }
        }
    }

    private static FixtureResult VerifyOne(Fixture fixture, FixtureManifest manifest, string artifacts, Lazy<DecoderResolver> decoders, Dictionary<string, LoadedSaveSet> cache)
    {
        var beforePath = Resolve(artifacts, fixture.Before!.Archive);
        var afterPath = Resolve(artifacts, fixture.After!.Archive);
        var missing = new[] { beforePath, afterPath }.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            return new FixtureResult(fixture.Id, FixtureOutcome.Skipped, $"missing: {string.Join(", ", missing)}", null);
        }

        var hashErrors = new[] { FixtureVerifier.CheckArchive(fixture.Before, beforePath), FixtureVerifier.CheckArchive(fixture.After, afterPath) }
            .OfType<string>().ToList();
        if (hashErrors.Count > 0)
        {
            return new FixtureResult(fixture.Id, FixtureOutcome.Fail, string.Join(" ", hashErrors), null);
        }

        var decoder = fixture.Decoder is null ? null : decoders.Value.Resolve(fixture.Decoder);
        var options = FixtureVerifier.OptionsFor(fixture, manifest, decoder);
        var verification = FixtureVerifier.Verify(fixture, Load(beforePath, cache).Set, Load(afterPath, cache).Set, options);
        return new FixtureResult(fixture.Id, verification.Passed ? FixtureOutcome.Pass : FixtureOutcome.Fail, null, verification);
    }

    private static LoadedSaveSet Load(string path, Dictionary<string, LoadedSaveSet> cache)
    {
        if (!cache.TryGetValue(path, out var loaded))
        {
            loaded = SaveSetLoader.Load(path);
            cache[path] = loaded;
        }

        return loaded;
    }

    private static string Resolve(string artifacts, string archive) =>
        Path.GetFullPath(Path.IsPathRooted(archive) ? archive : Path.Combine(artifacts, archive));

    private static string ArtifactsDirectory(CliArguments args, string manifestPath)
    {
        if (args.Get("artifacts") is { } explicitDir)
        {
            return Directory.Exists(explicitDir) ? Path.GetFullPath(explicitDir) : throw new UsageException($"--artifacts folder not found: {explicitDir}");
        }

        var local = Path.Combine(Directory.GetCurrentDirectory(), "artifacts");
        return SampleSaves.ArtifactsDirectory ?? (Directory.Exists(local) ? local : Path.GetDirectoryName(manifestPath)!);
    }

    private static string Render(IReadOnlyList<FixtureResult> results, string artifacts)
    {
        var w = new StringBuilder();
        w.Append($"artifacts: {artifacts}\n");
        foreach (var result in results)
        {
            if (result.Verification is { } verification)
            {
                w.Append(TextReportWriter.Render(verification));
                if (!verification.Passed)
                {
                    w.Append(TextReportWriter.Render(verification.Report)).Append('\n');
                }
            }
            else
            {
                w.Append($"fixture {result.FixtureId}: {result.Outcome.ToString().ToUpperInvariant()} ({result.Message})\n");
            }
        }

        w.Append($"{results.Count(r => r.Outcome == FixtureOutcome.Pass)} passed, {results.Count(r => r.Outcome == FixtureOutcome.Fail)} failed, {results.Count(r => r.Outcome == FixtureOutcome.Skipped)} skipped\n");
        return w.ToString();
    }
}
