using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Editing;

/// <summary>Stages descriptor changes without copying unrelated cached fields before every lookup.</summary>
internal sealed class FieldMetadataTransaction(ImmutableDictionary<string, FieldMetadata> original)
    : IReadOnlyDictionary<string, FieldMetadata>
{
    private readonly Dictionary<string, FieldMetadata> _changes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);

    public int Count { get; private set; } = original.Count;
    public IEnumerable<string> Keys => this.Select(entry => entry.Key);
    public IEnumerable<FieldMetadata> Values => this.Select(entry => entry.Value);

    public FieldMetadata this[string key]
    {
        get => TryGetValue(key, out var field) ? field : throw new KeyNotFoundException();
        set
        {
            if (!ContainsKey(key)) Count++;
            _removed.Remove(key);
            _changes[key] = value;
        }
    }

    public bool ContainsKey(string key) => TryGetValue(key, out _);

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out FieldMetadata value)
    {
        if (_changes.TryGetValue(key, out value)) return true;
        if (!_removed.Contains(key)) return original.TryGetValue(key, out value);
        value = null;
        return false;
    }

    public bool Remove(string key, [MaybeNullWhen(false)] out FieldMetadata value)
    {
        if (!TryGetValue(key, out value)) return false;
        _changes.Remove(key);
        if (original.ContainsKey(key)) _removed.Add(key);
        Count--;
        return true;
    }

    /// <summary>Returns the original root with only the staged removals and replacements applied.</summary>
    public ImmutableDictionary<string, FieldMetadata> Commit() => original.RemoveRange(_removed).SetItems(_changes);

    public IEnumerator<KeyValuePair<string, FieldMetadata>> GetEnumerator()
    {
        foreach (var (key, field) in original)
        {
            if (_removed.Contains(key)) continue;
            yield return new(key, _changes.GetValueOrDefault(key, field));
        }
        foreach (var entry in _changes)
            if (!original.ContainsKey(entry.Key)) yield return entry;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
