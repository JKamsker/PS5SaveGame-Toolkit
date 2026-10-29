namespace SaveToolkit.Testing;

/// <summary>A narrow temporary quarantine for a demonstrated parser failure, tracked in an issue.</summary>
/// <param name="ExceptionType">Exact exception type's full name.</param>
/// <param name="StackFrame">Ordinal substring identifying the known failing stack frame.</param>
/// <param name="TrackingNote">Issue or explanation tracking removal of the quarantine.</param>
public sealed record ExpectedFuzzFailure(string ExceptionType, string StackFrame, string TrackingNote)
{
    /// <summary>Matches an exact exception type and an ordinal stack-frame substring.</summary>
    public bool Matches(MutationFuzzFailure failure) =>
        failure.ExceptionType == ExceptionType && failure.StackTrace.Contains(StackFrame, StringComparison.Ordinal);
}

/// <summary>Contains coordinates and stack frames, never private sample bytes or exception messages.</summary>
/// <param name="Reproducer">Seed, mutation index and sample identity needed to reproduce the case.</param>
/// <param name="ExceptionType">Exception type or harness work-limit failure category.</param>
/// <param name="StackTrace">Stack frames or a harness work-limit diagnostic, without raw input bytes.</param>
public sealed record MutationFuzzFailure(string Reproducer, string ExceptionType, string StackTrace)
{
    /// <inheritdoc />
    public override string ToString() => $"{Reproducer}\n{ExceptionType}\n{StackTrace}";
}

/// <summary>Machine-readable outcome exchanged with the isolated worker.</summary>
/// <remarks>Mutable report lists are owned by the collecting worker; do not mutate them concurrently with serialization.</remarks>
public sealed record MutationFuzzResult
{
    /// <summary>Stable plugin ID under test.</summary>
    public string Plugin { get; init; } = "";
    /// <summary>Deterministic seed shared by the run's mutations.</summary>
    public uint Seed { get; init; } = SaveMutation.DefaultSeed;
    /// <summary>Requested mutation count per selected input file.</summary>
    public int MutationsPerSeed { get; init; }
    /// <summary>Number of authored synthetic input files supplied.</summary>
    public int SyntheticSeeds { get; init; }
    /// <summary>Number of private-sample input files selected.</summary>
    public int SampleSeeds { get; init; }
    /// <summary>Total input files reached, including synthetic and private-sample seeds.</summary>
    public int Samples { get; init; }
    /// <summary>Number of completed mutation cases.</summary>
    public int Cases { get; init; }
    /// <summary>Number of cases matching known failures, including repeated signatures.</summary>
    public int ExpectedFailures { get; init; }
    /// <summary>Accumulated parser work duration in seconds, excluding preparation.</summary>
    public double ElapsedSeconds { get; init; }
    /// <summary>Unexpected failures, retaining the first case per sample/type/stack signature.</summary>
    public List<MutationFuzzFailure> Failures { get; init; } = [];
    /// <summary>Quarantined failure signatures; repeated cases are counted in ExpectedFailures.</summary>
    public List<MutationFuzzFailure> KnownFailures { get; init; } = [];
}
