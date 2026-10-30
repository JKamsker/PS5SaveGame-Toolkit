using System.Text;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk;

/// <summary>Shared pre-consumption wire lower bounds. Exact capped JSON measurement follows admission.</summary>
/// <remarks>Charges never exceed corresponding default JSON bytes: UTF-8 content and minimum
/// container/record punctuation, rather than UTF-16 storage estimates. Each visit spends finite
/// allowance before descending; repeated references are charged once per serialized occurrence.</remarks>
public sealed class ActionWireBudget(long maximumBytes, CancellationToken cancellationToken = default)
{
    private const int MinimumOptionBytes = 20;
    private const int MinimumNodeBytes = 2;
    private long _remaining = maximumBytes;

    /// <summary>Admits complete ordered plan content before any detached graph copy or codec.</summary>
    public void AdmitPlan(ActionPlan plan)
    {
        AdmitDiagnostics(plan);
        BoundedMetadataCollections.Visit(plan.Changes, ActionPlanSnapshots.MaxPlanEntries, AdmitChange, cancellationToken);
    }

    /// <summary>Admits plan diagnostics independently of row mapping.</summary>
    public void AdmitDiagnostics(ActionPlan plan)
    {
        Spend(MinimumNodeBytes);
        BoundedMetadataCollections.Visit(plan.SkippedRows, ActionPlanSnapshots.MaxPlanEntries, row =>
        {
            Spend(MinimumNodeBytes); Text(row.RowKey); Text(row.Reason);
        }, cancellationToken);
        BoundedMetadataCollections.Visit(plan.Warnings, ActionPlanSnapshots.MaxPlanEntries, Text, cancellationToken);
        BoundedMetadataCollections.Visit(plan.Errors, ActionPlanSnapshots.MaxPlanEntries, error =>
        {
            Spend(MinimumNodeBytes); Text(error.ValueId); Text(error.Message);
        }, cancellationToken);
    }

    /// <summary>Admits one mapped row, spending the same allowance across all rows.</summary>
    public void AdmitChange(PlannedChange change)
    {
        Spend(MinimumNodeBytes);
        Text(change.ValueId); Text(change.Label);
        Value(change.OldValue); Value(change.NewValue);
        if (change.Effect is not { } effect) return;
        Text(effect.Source);
        Presentation(effect.Presentation);
        if (effect.Path is { } path) Path(path, ActionPlanSnapshots.MaxEffectPathParts);
        Spec(effect.Spec, forbidCallbacks: true);
    }

    /// <summary>Admits canonical descriptor work against the actual remaining operation allowance.</summary>
    public void AdmitMetadata(FieldMetadata field)
    {
        Spend(MinimumNodeBytes);
        Text(field.ValueId); Text(field.Label); Text(field.CanonicalValueId);
        Presentation(field.Presentation);
        if (field.Path is { } path)
            Path(path, checked((int)Math.Min(int.MaxValue, Math.Max(0, _remaining / 3))));
        Spec(field.Spec, forbidCallbacks: false);
    }

    private void Presentation(FieldPresentation? presentation)
    {
        if (presentation is null) return;
        Spend(MinimumNodeBytes);
        Text(presentation.Warning); Text(presentation.Description);
    }

    private void Path(IReadOnlyList<string> path, int maximum)
    {
        // Empty strings still consume quotes and separators. Reject the declared minimum
        // before enumerating a path whose actual wire shape cannot fit.
        Reserve(path.Count, 2);
        BoundedMetadataCollections.Visit(path, maximum, part => Content(part), cancellationToken);
    }

    private void Spec(FieldSpec spec, bool forbidCallbacks)
    {
        var active = new HashSet<FieldSpec>(ReferenceEqualityComparer.Instance);
        var nodes = 0;
        Visit(spec, 0);
        void Visit(FieldSpec current, int depth)
        {
            Spend(MinimumNodeBytes);
            if (depth > FieldMetadataLimits.MaxSpecDepth || ++nodes > FieldMetadataLimits.MaxClosureIds
                || !active.Add(current)) throw new InvalidOperationException("Action effect spec exceeds bounded depth or node limit.");
            try
            {
                Text(current.Unit);
                switch (current)
                {
                    case EnumSpec choice:
                        Reserve(choice.Options.Count, MinimumOptionBytes);
                        BoundedMetadataCollections.Visit(choice.Options, FieldMetadataLimits.MaxClosureIds, option =>
                        {
                            Content(option.Value); Content(option.Label); Content(option.Description);
                        }, cancellationToken);
                        break;
                    case FlagsSpec flags:
                        Reserve(flags.Bits.Count, MinimumOptionBytes);
                        BoundedMetadataCollections.Visit(flags.Bits, FieldMetadataLimits.MaxClosureIds, bit =>
                        {
                            Content(bit.Label); Content(bit.Description);
                        }, cancellationToken);
                        break;
                    case UnionSpec union:
                        Reserve(union.Options.Count, MinimumOptionBytes);
                        BoundedMetadataCollections.Visit(union.Options, FieldMetadataLimits.MaxClosureIds, option =>
                        {
                            Content(option.Id); Content(option.Label); Visit(option.Spec, depth + 1);
                        }, cancellationToken);
                        break;
                    case NullableSpec nullable: Visit(nullable.Inner, depth + 1); break;
                    case ReferenceSpec reference:
                        if (forbidCallbacks && reference.Assess is not null)
                            throw new InvalidOperationException("Action effect references cannot execute eligibility callbacks.");
                        Text(reference.CatalogId);
                        break;
                    case FloatSpec floating when floating.Min is { } min && !double.IsFinite(min)
                        || floating.Max is { } max && !double.IsFinite(max):
                        throw new InvalidOperationException("Action effect spec has nonfinite float bounds.");
                    case ExactNumberSpec exact: Text(exact.Min); Text(exact.Max); break;
                }
            }
            finally { active.Remove(current); }
        }
    }

    private void Value(FieldValue value)
    {
        var depth = 0;
        while (value.Kind == FieldValueKind.Union)
        {
            if (++depth > FieldMetadataLimits.MaxSpecDepth)
                throw new InvalidOperationException("Action effect value exceeds its bounded depth limit.");
            Spend(MinimumNodeBytes);
            Text(value.AsUnion().Type);
            value = value.AsUnion().Value;
        }
        if (value.Kind == FieldValueKind.Text) Text(value.AsString());
        else if (value.Kind == FieldValueKind.ExactNumber) Text(value.ToString());
        else Spend(1); // Every other scalar has at least one byte on the wire.
    }

    private void Reserve(int count, int minimumBytes)
    {
        if (count < 0) throw new InvalidOperationException("Metadata collection has an invalid bounded entry count.");
        Spend((long)count * minimumBytes);
    }

    private void Text(string? text) { Spend(2); Content(text); }
    private void Content(string? text)
    {
        if (text is not null) Spend(Encoding.UTF8.GetByteCount(text));
    }
    private void Spend(long bytes)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes < 0 || bytes > _remaining)
            throw new InvalidOperationException("Action metadata exceeds its bounded budget.");
        _remaining -= bytes;
    }
}
