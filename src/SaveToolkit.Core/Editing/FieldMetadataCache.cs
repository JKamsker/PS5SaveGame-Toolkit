using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Editing;

/// <summary>
/// Immutable resolved-metadata cache with a reverse canonical-alias index. A resolution edits a
/// copy-on-write builder, so its cost follows the batch rather than everything resolved so far
/// (a review walk resolves thousands of fields one at a time), and revalidating aliases after a
/// canonical target refreshes visits only that target's aliases.
/// </summary>
internal sealed class FieldMetadataCache
{
    public static FieldMetadataCache Empty { get; } = new(
        ImmutableDictionary.Create<string, FieldMetadata>(StringComparer.Ordinal),
        ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.Ordinal));

    private readonly ImmutableDictionary<string, ImmutableHashSet<string>> _aliases;

    private FieldMetadataCache(ImmutableDictionary<string, FieldMetadata> fields,
        ImmutableDictionary<string, ImmutableHashSet<string>> aliases)
    {
        Fields = fields;
        _aliases = aliases;
    }

    public ImmutableDictionary<string, FieldMetadata> Fields { get; }

    public Builder ToBuilder() => new(Fields.ToBuilder(), _aliases.ToBuilder());

    /// <summary>The canonical target an entry aliases; an entry without one is its own canonical.</summary>
    internal static string CanonicalOf(FieldMetadata field) =>
        string.IsNullOrEmpty(field.CanonicalValueId) ? field.ValueId : field.CanonicalValueId;

    internal sealed class Builder
    {
        private readonly ImmutableDictionary<string, FieldMetadata>.Builder _fields;
        private readonly ImmutableDictionary<string, ImmutableHashSet<string>>.Builder _aliases;

        internal Builder(ImmutableDictionary<string, FieldMetadata>.Builder fields,
            ImmutableDictionary<string, ImmutableHashSet<string>>.Builder aliases)
        {
            _fields = fields;
            _aliases = aliases;
        }

        /// <summary>Read view of the working entries; writes go through <see cref="Set"/> and <see cref="Remove"/>.</summary>
        public IReadOnlyDictionary<string, FieldMetadata> Fields => _fields;

        public void Set(string id, FieldMetadata field)
        {
            if (_fields.TryGetValue(id, out var previous)) Unlink(id, previous);
            _fields[id] = field;
            Link(id, field);
        }

        public bool Remove(string id, [NotNullWhen(true)] out FieldMetadata? removed)
        {
            if (!_fields.TryGetValue(id, out removed)) return false;
            Unlink(id, removed);
            _fields.Remove(id);
            return true;
        }

        /// <summary>Cached entries, other than the target itself, whose canonical target is <paramref name="canonicalId"/>.</summary>
        public IEnumerable<string> AliasesOf(string canonicalId) =>
            _aliases.TryGetValue(canonicalId, out var aliases) ? aliases : [];

        public FieldMetadataCache ToImmutable() => new(_fields.ToImmutable(), _aliases.ToImmutable());

        private void Link(string id, FieldMetadata field)
        {
            var canonicalId = CanonicalOf(field);
            if (canonicalId == id) return;
            _aliases[canonicalId] = (_aliases.TryGetValue(canonicalId, out var aliases)
                ? aliases : ImmutableHashSet.Create<string>(StringComparer.Ordinal)).Add(id);
        }

        private void Unlink(string id, FieldMetadata field)
        {
            var canonicalId = CanonicalOf(field);
            if (canonicalId == id || !_aliases.TryGetValue(canonicalId, out var aliases)) return;
            aliases = aliases.Remove(id);
            if (aliases.IsEmpty) _aliases.Remove(canonicalId);
            else _aliases[canonicalId] = aliases;
        }
    }
}
