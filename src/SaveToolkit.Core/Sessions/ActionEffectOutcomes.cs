using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

/// <summary>Freezes every provider-owned outcome collection before transaction ownership or publication changes.</summary>
internal static class ActionEffectOutcomes
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private const int MaximumDiagnosticChars = ActionPlanSnapshots.MaxPresentationTextLength;
    private const int MaximumRecoveryDiagnosticChars = 512;
    private const int MaximumRecoveryDiagnostics = 2;

    public static EditOutcome Snapshot(EditOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if ((long)outcome.Changes.Count + outcome.Warnings.Count + outcome.Errors.Count > ActionPlanSnapshots.MaxPlanEntries)
            throw new InvalidOperationException("Action outcome exceeds its bounded entry limit.");
        BoundedMetadataCollections.Visit(outcome.Changes, ActionPlanSnapshots.MaxPlanEntries, change =>
        {
            ArgumentNullException.ThrowIfNull(change);
            if (change.ValueId is null) throw new InvalidOperationException("Action outcome identity is missing.");
        });
        BoundedMetadataCollections.Visit(outcome.Warnings, ActionPlanSnapshots.MaxPlanEntries, ValidateDiagnostic);
        BoundedMetadataCollections.Visit(outcome.Errors, ActionPlanSnapshots.MaxPlanEntries, error =>
        {
            ArgumentNullException.ThrowIfNull(error);
            ValidateDiagnostic(error.ValueId);
            ValidateDiagnostic(error.Message);
        });
        // Do not serialize an opaque provider token: only its declared size is part of the
        // host contract. The actual ordered changes/diagnostics must fit before any copy.
        _ = BoundedJson.Measure(new { outcome.Changes, outcome.Warnings, outcome.Errors }, MaximumBytes);
        return outcome with
        {
            Changes = Array.AsReadOnly(BoundedMetadataCollections.Copy(outcome.Changes, ActionPlanSnapshots.MaxPlanEntries)),
            Warnings = Array.AsReadOnly(BoundedMetadataCollections.Copy(outcome.Warnings, ActionPlanSnapshots.MaxPlanEntries)),
            Errors = Array.AsReadOnly(BoundedMetadataCollections.Copy(outcome.Errors, ActionPlanSnapshots.MaxPlanEntries)),
        };
    }

    public static string FailureSummary(IReadOnlyList<FieldError> errors) =>
        string.Join("; ", errors.Take(MaximumRecoveryDiagnostics).Select(error => Bound(error.Message)));

    public static string Bound(string message) => message.Length <= MaximumRecoveryDiagnosticChars
        ? message : message[..MaximumRecoveryDiagnosticChars] + "…";

    public static string Diagnostic(Exception failure) => failure is AggregateException aggregate
        ? string.Join("; ", aggregate.InnerExceptions.Take(MaximumRecoveryDiagnostics).Select(inner => inner is AggregateException
            ? "Additional nested recovery failure." : Bound(inner.Message)))
        : Bound(failure.Message);

    private static void ValidateDiagnostic(string value)
    {
        if (value is null || value.Length > MaximumDiagnosticChars)
            throw new InvalidOperationException("Action outcome diagnostic exceeds its bounded limit.");
    }
}
