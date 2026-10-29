using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Editing;

/// <summary>Resolves current bindings and replays writable primaries, excluding derived aliases.</summary>
public static class FieldMetadataReplay
{
    public static IReadOnlyList<ValueEdit> Edits(ViewIndex index, IReadOnlyList<ValueChange> changes, bool useOld)
    {
        index.ResolveMetadata(changes.Select(change => change.ValueId));
        var result = new List<ValueEdit>();
        foreach (var change in useOld ? changes.Reverse() : changes)
        {
            var metadata = index.ResolvedMetadata(change.ValueId);
            if (index.HasFieldMetadata && metadata is null && index.ResolvedSpec(change.ValueId) is null)
                throw new InvalidOperationException($"Missing replay metadata for '{change.ValueId}'.");
            if (metadata?.BindingKind == FieldBindingKind.Derived || index.ResolvedSpec(change.ValueId) is { ReadOnly: true }) continue;
            result.Add(new ValueEdit(change.ValueId, useOld ? change.OldValue : change.NewValue));
        }
        return result;
    }
}
