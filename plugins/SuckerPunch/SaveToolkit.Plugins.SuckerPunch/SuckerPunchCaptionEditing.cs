using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>One metadata transaction and its byte-exact history. Parsed gameplay offsets never move.</summary>
internal sealed class SuckerPunchCaptionEditing(ISaveFile file, byte[] primary, SuckerPunchCaption caption)
{
    private readonly object _owner = new();

    public EditOutcome Apply(IReadOnlyList<ValueEdit> edits)
    {
        if (edits.Count != 1) return EditOutcome.Rejected(SuckerPunchCaption.ValueId, "Apply the manual caption separately from gameplay edits.");
        var edit = edits[0];
        if (caption.Validate(edit.Value) is { } error) return EditOutcome.Rejected(edit.ValueId, error);
        var old = caption.Read();
        if (old == edit.Value) return new EditOutcome();
        var before = Capture();
        try
        {
            caption.Write(edit.Value);
            return new EditOutcome { Changes = [new(edit.ValueId, old, caption.Read())], Revert = before,
                StructureChanged = true, Warnings = [SuckerPunchCaption.Warning] };
        }
        catch
        {
            Restore(before);
            throw;
        }
    }

    public DocumentMemento Capture()
    {
        var state = new Snapshot(_owner, primary.ToArray(), caption.Capture());
        // Include array/object overhead; no parsed object graphs are retained by the token.
        const int retainedOverheadBytes = 256;
        return DocumentMemento.Snapshot(state, state.Primary.LongLength + state.Sfo.LongLength + retainedOverheadBytes);
    }

    public void Restore(DocumentMemento snapshot)
    {
        if (snapshot is not SnapshotMemento<Snapshot> token || !ReferenceEquals(token.State.Owner, _owner) ||
            token.State.Primary.Length != primary.Length) throw new InvalidOperationException("Memento belongs to a different document.");
        _ = SaveContainer.Parse(token.State.Primary, file.Directory.TitleId);
        caption.Restore(token.State.Sfo);
        // Fixed-width gameplay edits preserve all parsed offsets and immutable opening quantity/timer bounds.
        token.State.Primary.CopyTo(primary, 0);
    }

    private sealed record Snapshot(object Owner, byte[] Primary, byte[] Sfo);
}
