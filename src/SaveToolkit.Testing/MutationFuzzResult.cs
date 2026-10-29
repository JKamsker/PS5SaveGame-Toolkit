namespace SaveToolkit.Testing;

/// <summary>A narrow temporary quarantine for a demonstrated parser failure, tracked in an issue.</summary>
public sealed record ExpectedFuzzFailure(string ExceptionType, string StackFrame, string TrackingNote)
{
    public bool Matches(MutationFuzzFailure failure) =>
        failure.ExceptionType == ExceptionType && failure.StackTrace.Contains(StackFrame, StringComparison.Ordinal);
}

/// <summary>Contains coordinates and stack frames, never private sample bytes or exception messages.</summary>
public sealed record MutationFuzzFailure(string Reproducer, string ExceptionType, string StackTrace)
{
    public override string ToString() => $"{Reproducer}\n{ExceptionType}\n{StackTrace}";
}

/// <summary>Machine-readable outcome exchanged with the isolated worker.</summary>
public sealed record MutationFuzzResult
{
    public int Samples { get; init; }
    public int Cases { get; init; }
    public int ExpectedFailures { get; init; }
    public double ElapsedSeconds { get; init; }
    public List<MutationFuzzFailure> Failures { get; init; } = [];
    public List<MutationFuzzFailure> KnownFailures { get; init; } = [];
}
