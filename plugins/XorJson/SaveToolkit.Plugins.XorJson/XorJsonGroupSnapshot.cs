using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class XorJsonGroupSnapshot(IReadOnlyDictionary<string, DocumentMemento> members) : DocumentMemento
{
    private readonly Dictionary<string, DocumentMemento> _members = new(members, StringComparer.Ordinal);

    public override long SizeBytes => 128 + _members.Values.Sum(value => value.SizeBytes);

    public DocumentMemento For(string role) => _members.TryGetValue(role, out var value)
        ? value
        : throw new InvalidOperationException("The XorJson group snapshot is missing a member.");

    public bool HasRoles(IEnumerable<string> roles) => _members.Keys.OrderBy(value => value, StringComparer.Ordinal)
        .SequenceEqual(roles.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);
}
