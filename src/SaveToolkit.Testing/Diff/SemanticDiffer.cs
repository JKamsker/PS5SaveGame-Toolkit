using System.Text.Json.Serialization;

namespace SaveToolkit.Testing.Diff;

[JsonConverter(typeof(JsonStringEnumConverter<SemanticChangeKind>))]
public enum SemanticChangeKind
{
    Added,
    Removed,
    Changed,
}

public sealed record SemanticChange(string Path, SemanticChangeKind Kind)
{
    public string? BeforeKind { get; init; }
    public string? Before { get; init; }
    public string? AfterKind { get; init; }
    public string? After { get; init; }
    public string? Label { get; init; }
}

public sealed record SemanticDiffResult
{
    public string? Decoder { get; init; }

    /// <summary>Set when decoding failed; the counts are then zero.</summary>
    public string? Error { get; init; }

    public int BeforeValues { get; init; }
    public int AfterValues { get; init; }
    public int Added { get; init; }
    public int Removed { get; init; }
    public int Changed { get; init; }

    /// <summary>Changes masked by churn rules.</summary>
    public int Ignored { get; init; }

    /// <summary>Changes kept for the report (bounded); the counts above cover all of them.</summary>
    public IReadOnlyList<SemanticChange> Changes { get; init; } = [];

    /// <summary>True if either tree hit its decode limits, so absent paths are not proof of removal.</summary>
    public bool InputTruncated { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public int Total => Added + Removed + Changed;

    public bool Truncated => Total > Changes.Count;

    public static SemanticDiffResult Failed(string? decoder, string error) => new() { Decoder = decoder, Error = error };
}

/// <summary>Aligns two semantic trees by path (identity) and reports added, removed and changed values.</summary>
public static class SemanticDiffer
{
    public static SemanticDiffResult Compare(SemanticTree before, SemanticTree after, FileChurn? churn = null, int maxChanges = 500)
    {
        churn ??= FileChurn.None;
        var old = new Dictionary<string, SemanticValue>(before.Values.Count, StringComparer.Ordinal);
        foreach (var value in before.Values)
        {
            old.TryAdd(value.Path, value);
        }

        var changes = new List<SemanticChange>();
        int added = 0, removed = 0, changed = 0, ignored = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Report(SemanticChange change)
        {
            if (churn.MatchPath(change.Path) is not null)
            {
                ignored++;
                return;
            }

            _ = change.Kind switch
            {
                SemanticChangeKind.Added => added++,
                SemanticChangeKind.Removed => removed++,
                _ => changed++,
            };
            if (changes.Count < maxChanges)
            {
                changes.Add(change);
            }
        }

        foreach (var value in after.Values)
        {
            if (!seen.Add(value.Path))
            {
                continue;
            }

            if (!old.TryGetValue(value.Path, out var previous))
            {
                Report(new SemanticChange(value.Path, SemanticChangeKind.Added) { AfterKind = value.Kind, After = value.Value, Label = value.Label });
            }
            else if (previous.Kind != value.Kind || previous.Value != value.Value)
            {
                Report(new SemanticChange(value.Path, SemanticChangeKind.Changed)
                {
                    BeforeKind = previous.Kind,
                    Before = previous.Value,
                    AfterKind = value.Kind,
                    After = value.Value,
                    Label = value.Label ?? previous.Label,
                });
            }
        }

        foreach (var value in before.Values)
        {
            if (!seen.Contains(value.Path) && seen.Add(value.Path))
            {
                Report(new SemanticChange(value.Path, SemanticChangeKind.Removed) { BeforeKind = value.Kind, Before = value.Value, Label = value.Label });
            }
        }

        return new SemanticDiffResult
        {
            Decoder = after.Decoder,
            BeforeValues = before.Values.Count,
            AfterValues = after.Values.Count,
            Added = added,
            Removed = removed,
            Changed = changed,
            Ignored = ignored,
            Changes = changes,
            InputTruncated = before.Truncated || after.Truncated,
            Warnings = [.. before.Warnings.Select(w => "before: " + w), .. after.Warnings.Select(w => "after: " + w)],
        };
    }
}
