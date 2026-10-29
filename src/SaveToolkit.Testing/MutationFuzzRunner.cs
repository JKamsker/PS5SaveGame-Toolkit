using System.Diagnostics;
using System.Security.Cryptography;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing;

/// <summary>Synchronous parser work makes per-thread allocation accounting independent of parallel tests.</summary>
internal sealed class MutationFuzzRunner(
    IGamePlugin plugin,
    uint seed,
    int count,
    IReadOnlyList<Type> rejectionTypes,
    IReadOnlyList<ExpectedFuzzFailure> expectedFailures,
    TimeSpan caseTimeout,
    Func<ISaveFile, long> allocationBudget,
    Func<ISaveFile, IReferenceContext> referencesFor,
    Action<string> progress,
    TimeSpan? timeBudget = null)
{
    internal static IEnumerable<ISaveFile> SelectSamples(IEnumerable<ISaveFile> eligible, int limit) =>
        eligible.OrderBy(file => file.Length).ThenBy(file => file.RelativePath, StringComparer.Ordinal).Take(limit);

    public MutationFuzzResult Run(IEnumerable<ISaveFile> files)
    {
        ValidateConfiguration();
        var failures = new List<MutationFuzzFailure>();
        var known = new List<MutationFuzzFailure>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var samples = 0;
        var cases = 0;
        var expected = 0;
        var elapsed = TimeSpan.Zero;
        var runStarted = Stopwatch.GetTimestamp();
        progress("Preparing samples");
        foreach (var original in files)
        {
            samples++;
            progress($"Preparing {original.RelativePath}");
            var bytes = original.ReadAllBytes();
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var references = referencesFor(original); // Pack loading is setup, not malformed-input work.
            var budget = allocationBudget(original);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget);
            for (var index = 0; index < count; index++)
            {
                var mutation = SaveMutation.Create(bytes, seed, index);
                var reproducer = $"case seed=0x{seed:X8} index={index} sample={original.RelativePath} sha256={hash}: {mutation.Description}";
                progress(reproducer);
                if (timeBudget is { } limit && Stopwatch.GetElapsedTime(runStarted) >= limit)
                {
                    failures.Add(new(reproducer, "PluginTimeBudgetExceeded", $"Per-plugin limit {limit}."));
                    return Result();
                }
                var overlay = new OverlaySaveSet(original.Directory.SaveSet,
                    new Dictionary<string, ReadOnlyMemory<byte>> { [original.RelativePath] = mutation.Content });
                var file = ((ISaveSet)overlay).FindFile(original.RelativePath)!;
                var (failure, duration) = RunCase(file, references, budget, reproducer);
                elapsed += duration;
                cases++;
                if (failure is null) continue;
                var isExpected = expectedFailures.Any(e => e.Matches(failure));
                if (isExpected) expected++;
                // Keep the lowest index for each sample/error/stack, without flooding CI with duplicate traces.
                var signature = $"{hash}/{failure.ExceptionType}/{failure.StackTrace}";
                if (signatures.Add(signature)) (isExpected ? known : failures).Add(failure);
            }

            progress($"Preparing next sample after {original.RelativePath}");
        }

        return Result();

        MutationFuzzResult Result() => new()
        {
            Plugin = plugin.Info.Id, Seed = seed, MutationsPerSeed = count,
            Samples = samples, Cases = cases, ExpectedFailures = expected, ElapsedSeconds = elapsed.TotalSeconds,
            Failures = failures, KnownFailures = known,
        };
    }

    private (MutationFuzzFailure? Failure, TimeSpan Duration) RunCase(
        ISaveFile file, IReferenceContext references, long budget, string reproducer)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        MutationFuzzFailure? failure = null;
        ISaveDocument? document = null;
        try
        {
            if (plugin.Probe(file).IsMatch)
            {
                document = plugin.Open(file, references);
                _ = document.BuildView();
                if (document is IQueryableDocument queryable)
                    foreach (var collection in queryable.Collections)
                        _ = queryable.Query(collection.Id, new()
                        {
                            Limit = QueryableCollectionLimits.DefaultPageSize, IncludeEmpty = true,
                        });
            }
        }
        catch (Exception ex)
        {
            failure = Classify(ex, reproducer);
        }
        finally
        {
            try { document?.Dispose(); }
            catch (Exception ex)
            {
                // A clean rejection during Dispose must not mask a parser bug thrown earlier.
                failure ??= Classify(ex, reproducer);
            }
        }

        var duration = Stopwatch.GetElapsedTime(started);
        var allocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // Work limits apply even to documented format rejections, and cannot be hidden by a custom rejection type.
        if (allocation > budget)
            failure = new(reproducer, "AllocationBudgetExceeded", $"Allocated {allocation} managed bytes; budget {budget}.");
        if (duration > caseTimeout)
            failure = new(reproducer, "CaseTimeoutExceeded", $"Elapsed {duration}; limit {caseTimeout}.");
        return (failure, duration);
    }

    private MutationFuzzFailure? Classify(Exception exception, string reproducer) =>
        exception is InvalidDataException or SaveFormatException || rejectionTypes.Contains(exception.GetType())
            ? null
            : new(reproducer, exception.GetType().FullName!, exception.StackTrace ?? "(no stack trace)");

    private void ValidateConfiguration()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(caseTimeout, TimeSpan.Zero);
        foreach (var type in rejectionTypes)
        {
            if (!typeof(Exception).IsAssignableFrom(type) || type == typeof(Exception) || type == typeof(SystemException)
                || typeof(ArgumentException).IsAssignableFrom(type) || typeof(IndexOutOfRangeException).IsAssignableFrom(type)
                || typeof(NullReferenceException).IsAssignableFrom(type) || typeof(OverflowException).IsAssignableFrom(type)
                || typeof(OutOfMemoryException).IsAssignableFrom(type))
                throw new ArgumentException($"{type.Name} is not a documented format rejection type.", nameof(rejectionTypes));
        }

        foreach (var expected in expectedFailures)
            if (string.IsNullOrWhiteSpace(expected.ExceptionType) || string.IsNullOrWhiteSpace(expected.StackFrame)
                || string.IsNullOrWhiteSpace(expected.TrackingNote))
                throw new ArgumentException("Expected fuzz failures require an exception, stack frame and tracking note.", nameof(expectedFailures));
    }
}
