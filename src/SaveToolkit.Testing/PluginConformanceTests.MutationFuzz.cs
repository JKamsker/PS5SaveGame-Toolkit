using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using Xunit;
using Xunit.Sdk;

namespace SaveToolkit.Testing;

/// <summary>Bounded malformed-input coverage, isolated from the rest of the conformance process.</summary>
public abstract partial class PluginConformanceTests<TPlugin>
{
    /// <summary>Deterministic mutations per selected real file and per synthetic seed.</summary>
    protected virtual int MutationFuzzCount => 200;

    protected virtual uint MutationFuzzSeed => SaveMutation.DefaultSeed;

    /// <summary>Independent of exhaustive sample overrides used by other conformance tests.</summary>
    protected virtual int MaxMutationFuzzFilesPerTitle => 1;

    /// <summary>Optional small valid files, including their title, name and any required siblings.</summary>
    protected virtual IEnumerable<ISaveFile> MutationFuzzSyntheticSeeds =>
        global::SaveToolkit.Testing.MutationFuzzSyntheticSeeds.For(Plugin.Info.Id);

    /// <summary>Documented format rejections only; general programming/runtime errors cannot be accepted.</summary>
    protected virtual IReadOnlyList<Type> MutationFuzzRejectionTypes => [];

    /// <summary>Temporary, narrowly matched known failures with an issue/tracking note.</summary>
    protected virtual IReadOnlyList<ExpectedFuzzFailure> ExpectedFuzzFailures => MutationFuzzKnownFailures.For(Plugin.Info.Id);

    /// <summary>Generous wall-clock guard, enforced by killing the isolated worker on a stuck case.</summary>
    protected virtual TimeSpan MutationFuzzCaseTimeout => TimeSpan.FromSeconds(30);

    /// <summary>Managed bytes allocated on the synchronous parser thread, including rejected inputs and Dispose.</summary>
    protected virtual long MutationFuzzAllocationBudget(ISaveFile file) =>
        Math.Min(1024L * 1024 * 1024, 64L * 1024 * 1024 + 32 * file.Length);

    [Fact]
    public async Task MutationFuzz()
    {
        var options = MutationFuzzOptions.Read(MutationFuzzSeed, MutationFuzzCount);
        var context = $"MutationFuzz plugin={Plugin.Info.Id} seed=0x{options.Seed:X8}";
        MutationFuzzResult result;
        if (Environment.GetEnvironmentVariable(MutationFuzzProcess.WorkerVariable) is { Length: > 0 } worker)
        {
            var synthetic = MutationFuzzSyntheticSeeds.ToArray();
            var sampleSeeds = 0;
            result = new MutationFuzzRunner(Plugin, options.Seed, options.Mutations,
                MutationFuzzRejectionTypes, ExpectedFuzzFailures, MutationFuzzCaseTimeout,
                MutationFuzzAllocationBudget, ReferencesFor, message => MutationFuzzProcess.Progress(worker, message),
                options.TimeBudget).Run(FuzzFiles(synthetic, () => sampleSeeds++));
            result = result with { SyntheticSeeds = synthetic.Length, SampleSeeds = sampleSeeds };
            File.WriteAllText(Path.Combine(worker, MutationFuzzProcess.ResultFile), JsonSerializer.Serialize(result));
            return;
        }

        Assert.True(options.Mutations > 0, $"{context}: MutationFuzzCount must be positive.");
        Assert.True(MaxMutationFuzzFilesPerTitle > 0, $"{context}: MaxMutationFuzzFilesPerTitle must be positive.");
        Assert.True(MutationFuzzCaseTimeout > TimeSpan.Zero, $"{context}: MutationFuzzCaseTimeout must be positive.");
        try
        {
            result = await MutationFuzzProcess.RunAsync(GetType(), MutationFuzzCaseTimeout, options.TimeBudget);
        }
        catch (XunitException exception)
        {
            // Worker diagnostics contain only checkpoints, never its raw output or exception messages.
            result = new()
            {
                Plugin = Plugin.Info.Id, Seed = options.Seed, MutationsPerSeed = options.Mutations,
                Failures = [new(context, "WorkerFailure", exception.Message)],
            };
        }

        MutationFuzzReport.Write(result);
        TestContext.Current.AddAttachment("mutation-fuzz.json", JsonSerializer.Serialize(result));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{context}: {result.SyntheticSeeds} synthetic seeds, {result.SampleSeeds} sample seeds, {result.Cases} cases, {result.ExpectedFailures} known failures, {result.ElapsedSeconds:F3}s parser work.");
        Assert.True(result.Failures.Count == 0, context + "\n" + string.Join("\n\n", result.Failures));
        Assert.SkipWhen(result.Samples == 0, $"{context}: No available samples or synthetic mutation-fuzz seeds.");
    }

    private IEnumerable<ISaveFile> FuzzFiles(IEnumerable<ISaveFile> synthetic, Action sampleSelected)
    {
        foreach (var seed in synthetic) yield return seed;
        foreach (var title in SampleTitleIds)
        {
            // A missing title must not hide available titles or synthetic coverage.
            if (SampleSaves.FindArchive(title) is null) continue;
            foreach (var file in MutationFuzzRunner.SelectSamples(SelectFiles(SampleSaves.Load(title)), MaxMutationFuzzFilesPerTitle))
            {
                sampleSelected();
                yield return file;
            }
        }
    }
}
