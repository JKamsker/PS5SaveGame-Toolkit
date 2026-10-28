using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

/// <summary>Runs opt-in inspection on a disposable copy; a broken action cannot alter live history or bindings.</summary>
internal static class InspectionActionRunner
{
    private const long MaxSnapshotBytes = 256L * 1024 * 1024;

    public static ActionOutcome Run(IGamePlugin plugin, ISaveFile file, ISaveDocument live,
        IReferenceContext references, IEnumerable<string> expanded, string actionId,
        IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var snapshot = Snapshot(live);
        var overlay = new OverlaySaveSet(file.Directory.SaveSet, snapshot);
        var currentFile = overlay.Directories.SelectMany(d => d.Files.Concat(d.SystemFiles))
            .Single(f => f.RelativePath == file.RelativePath);
        var reopened = plugin.Open(currentFile, references);
        if (ReferenceEquals(reopened, live))
        {
            throw new InvalidOperationException("Inspection requires a new document instance from Open.");
        }

        using var inspection = reopened;

        var index = new ViewIndex();
        index.Add(inspection.BuildView());
        Expand(inspection, index, expanded);
        var values = index.Specs.Keys.ToDictionary(id => id, inspection.GetValue, StringComparer.Ordinal);
        if (!MatchesSnapshot(snapshot, inspection))
        {
            throw new InvalidOperationException("The inspection copy did not preserve the current serialized save.");
        }

        var outcome = inspection.InvokeAction(actionId, parameters);
        if (outcome.Changes.Count != 0 || outcome.Revert is not null || !outcome.Undoable || outcome.StructureChanged
            || values.Any(pair => inspection.GetValue(pair.Key) != pair.Value)
            || !MatchesSnapshot(snapshot, inspection))
        {
            throw new InvalidOperationException("An inspection action must not change save values, bytes or structure.");
        }

        return outcome;
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> Snapshot(ISaveDocument document)
    {
        var outputs = document.Serialize();
        CheckSize(outputs);
        return outputs.ToDictionary(f => f.RelativePath, f => (ReadOnlyMemory<byte>)f.Content.ToArray(), StringComparer.Ordinal);
    }

    private static bool MatchesSnapshot(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> snapshot, ISaveDocument document)
    {
        var outputs = document.Serialize();
        CheckSize(outputs);
        var files = outputs.ToDictionary(f => f.RelativePath, f => f.Content, StringComparer.Ordinal);
        return snapshot.Count == files.Count && snapshot.All(pair =>
            files.TryGetValue(pair.Key, out var content) && pair.Value.Span.SequenceEqual(content.Span));
    }

    private static void CheckSize(IReadOnlyList<FileOutput> outputs)
    {
        if (outputs.Sum(f => (long)f.Content.Length) > MaxSnapshotBytes)
        {
            throw new InvalidOperationException("Inspection snapshot exceeds the 256 MiB limit.");
        }
    }

    private static void Expand(ISaveDocument document, ViewIndex index, IEnumerable<string> expanded)
    {
        var pending = expanded.ToHashSet(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var ready = pending.Where(index.KnowsLazy).ToArray();
            if (ready.Length == 0) break;
            foreach (var id in ready)
            {
                index.AddAll(document.ExpandNode(id));
                pending.Remove(id);
            }
        }
    }
}
