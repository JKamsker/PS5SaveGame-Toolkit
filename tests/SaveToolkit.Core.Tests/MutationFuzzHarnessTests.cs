using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

#pragma warning disable CA2201 // Deliberately inject runtime parser failures to verify harness rejection policy.

public sealed class MutationFuzzHarnessTests
{
    private static readonly string[] ExpectedCollectionOrder = ["empty", "items"];

    [Fact]
    public void Success_ExercisesTheWholeLifecycleAndEveryCollectionOncePerCase()
    {
        var plugin = new FuzzPlugin();
        var result = Run(plugin);
        Assert.Empty(result.Failures);
        Assert.Equal(12, result.Cases);
        Assert.Equal(12, plugin.ProbeCalls);
        Assert.Equal(12, plugin.OpenCalls);
        Assert.Equal(12, plugin.ViewCalls);
        Assert.Equal(12, plugin.DisposeCalls);
        Assert.Equal(Enumerable.Repeat(ExpectedCollectionOrder, 12).SelectMany(x => x), plugin.Queries);
    }

    [Fact]
    public void NonmatchingProbe_DoesNotOpen()
    {
        var plugin = new FuzzPlugin { Matches = false };
        Assert.Empty(Run(plugin).Failures);
        Assert.Equal(0, plugin.OpenCalls);
    }

    [Theory]
    [InlineData("probe")]
    [InlineData("open")]
    [InlineData("view")]
    [InlineData("query")]
    [InlineData("dispose")]
    public void InvalidDataAndSdkRejections_AreAcceptedAtEveryStage(string stage)
    {
        foreach (var failure in new Func<Exception>[] { () => new InvalidDataException(), () => new SaveFormatException() })
        {
            var plugin = new FuzzPlugin { ThrowDuring = stage, Failure = failure };
            Assert.Empty(Run(plugin).Failures);
            if (stage is "view" or "query" or "dispose") Assert.Equal(12, plugin.DisposeCalls);
        }
    }

    [Theory]
    [InlineData("probe")]
    [InlineData("open")]
    [InlineData("view")]
    [InlineData("query")]
    [InlineData("dispose")]
    public void UnexpectedExceptions_AreFailuresWithLowestIndexAndStack(string stage)
    {
        var plugin = new FuzzPlugin { ThrowDuring = stage, Failure = () => new IndexOutOfRangeException() };
        var failure = Assert.Single(Run(plugin).Failures);
        Assert.Equal(typeof(IndexOutOfRangeException).FullName, failure.ExceptionType);
        Assert.Contains("seed=0x40C0FFEE index=0 ", failure.Reproducer);
        Assert.Contains("sha256=", failure.Reproducer);
        Assert.Contains("FuzzPlugin.ThrowIf", failure.StackTrace);
        if (stage is "view" or "query" or "dispose") Assert.Equal(12, plugin.DisposeCalls);
    }

    [Fact]
    public void ExplicitRejectionTypes_AreExactAndCannotWhitelistProgrammingErrors()
    {
        var plugin = new FuzzPlugin { ThrowDuring = "open", Failure = () => new SyntheticRejection() };
        Assert.Single(Run(plugin).Failures);
        Assert.Empty(Run(plugin, rejectionTypes: [typeof(SyntheticRejection)]).Failures);
        foreach (var type in new[] { typeof(Exception), typeof(SystemException), typeof(ArgumentException),
            typeof(ArgumentOutOfRangeException), typeof(IndexOutOfRangeException), typeof(NullReferenceException),
            typeof(OverflowException), typeof(OutOfMemoryException) })
            Assert.Throws<ArgumentException>(() => Run(plugin, rejectionTypes: [type]));
    }

    [Fact]
    public void AllocationBudget_AppliesToSuccessfulAndRejectedCases()
    {
        foreach (var stage in new string?[] { null, "open" })
        {
            var plugin = new FuzzPlugin { ThrowDuring = stage, InspectFile = _ => GC.KeepAlive(new byte[100_000]) };
            var result = Run(plugin, budget: 50_000);
            Assert.All(result.Failures, f => Assert.Equal("AllocationBudgetExceeded", f.ExceptionType));
            Assert.NotEmpty(result.Failures);
        }
    }

    [Fact]
    public void RuntimeErrors_AreNeverCleanRejections()
    {
        foreach (var factory in new Func<Exception>[] { () => new NullReferenceException(),
            () => new ArgumentOutOfRangeException(), () => new OverflowException(), () => new OutOfMemoryException() })
        {
            var result = Run(new FuzzPlugin { ThrowDuring = "open", Failure = factory });
            Assert.Equal(factory().GetType().FullName, Assert.Single(result.Failures).ExceptionType);
        }
    }

    [Fact]
    public void DisposalRejections_DoNotMaskParserBugsAndDisposalBugsStillFail()
    {
        var parserBug = Run(new FuzzPlugin
        {
            ThrowDuring = "view", Failure = () => new NullReferenceException(),
            DisposeFailure = () => new InvalidDataException(),
        });
        Assert.Equal(typeof(NullReferenceException).FullName, Assert.Single(parserBug.Failures).ExceptionType);
        var disposalBug = Run(new FuzzPlugin
        {
            ThrowDuring = "view", Failure = () => new InvalidDataException(),
            DisposeFailure = () => new NullReferenceException(),
        });
        Assert.Equal(typeof(NullReferenceException).FullName, Assert.Single(disposalBug.Failures).ExceptionType);
    }

    [Fact]
    public void ExpectedFailures_SuppressOnlyTheDocumentedSignatureAndStillRunEveryCase()
    {
        var plugin = new FuzzPlugin { ThrowDuring = "open", Failure = () => new OverflowException() };
        ExpectedFuzzFailure expected = new(typeof(OverflowException).FullName!, "FuzzPlugin.ThrowIf", "#40 synthetic test");
        var result = Run(plugin, expected: [expected]);
        Assert.Empty(result.Failures);
        Assert.Equal(12, result.ExpectedFailures);
        Assert.Single(result.KnownFailures);
        Assert.Single(Run(plugin, expected: [expected with { StackFrame = "OtherParser" }]).Failures);
    }

    [Fact]
    public void Overlay_PreservesOriginalMetadataAndSiblingBytes()
    {
        var set = new MemorySaveSet().With("PPSA12345", "SLOT", new Dictionary<string, byte[]>
        {
            ["save/data.bin"] = [1, 2, 3, 4], ["companion.bin"] = [9, 8, 7],
        }, backup: true);
        var original = set.Directories[0].Files[0];
        var plugin = new FuzzPlugin { InspectFile = file =>
        {
            Assert.Equal(original.RelativePath, file.RelativePath);
            Assert.Equal(original.FileName, file.FileName);
            Assert.Equal("PPSA12345", file.Directory.TitleId);
            Assert.True(file.Directory.IsBackup);
            Assert.Equal(new byte[] { 9, 8, 7 }, file.Directory.Files.Single(f => f.FileName == "companion.bin").ReadAllBytes());
        } };
        Assert.Empty(Run(plugin, files: [original]).Failures);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, original.ReadAllBytes());
    }

    [Fact]
    public void NoSeeds_IsReportedWithoutHidingConfigurationErrors()
    {
        Assert.Equal(0, Run(new FuzzPlugin(), files: []).Samples);
        Assert.Throws<ArgumentException>(() => Run(new FuzzPlugin(), expected: [new("", "", "")]));
    }

    [Fact]
    public void MultipleSeeds_GetTheFullCountWithReferencesPreparedOncePerSeed()
    {
        var plugin = new FuzzPlugin();
        var referenceCalls = 0;
        var result = new MutationFuzzRunner(plugin, SaveMutation.DefaultSeed, 12, [], [], TimeSpan.FromSeconds(30),
            _ => 1024 * 1024, _ => { referenceCalls++; return IReferenceContext.Empty; }, _ => { })
            .Run([MemorySaveSet.SingleFile("a.bin", new byte[64]), MemorySaveSet.SingleFile("b.bin", new byte[32])]);
        Assert.Empty(result.Failures);
        Assert.Equal(2, result.Samples);
        Assert.Equal(24, result.Cases);
        Assert.Equal(24, plugin.DisposeCalls);
        Assert.Equal(2, referenceCalls);
    }

    [Fact]
    public void Sampling_PrefersSmallFilesWithStableOrdinalTiesAndHonorsTheCap()
    {
        var set = new MemorySaveSet().With("PPSA12345", "SLOT", new Dictionary<string, byte[]>
        {
            ["large.bin"] = new byte[32], ["z.bin"] = new byte[4], ["b.bin"] = new byte[4], ["a.bin"] = new byte[4],
        });
        var selected = MutationFuzzRunner.SelectSamples(set.Directories[0].Files, 2).ToArray();
        Assert.Equal(["a.bin", "b.bin"], selected.Select(file => file.FileName));
    }

    private static MutationFuzzResult Run(FuzzPlugin plugin, long budget = 1024 * 1024,
        IReadOnlyList<Type>? rejectionTypes = null, IReadOnlyList<ExpectedFuzzFailure>? expected = null,
        IEnumerable<ISaveFile>? files = null) =>
        new MutationFuzzRunner(plugin, SaveMutation.DefaultSeed, 12, rejectionTypes ?? [], expected ?? [],
            TimeSpan.FromSeconds(30), _ => budget, _ => IReferenceContext.Empty, _ => { })
            .Run(files ?? [MemorySaveSet.SingleFile("data.bin", new byte[64])]);

    private sealed class SyntheticRejection : Exception;
}
