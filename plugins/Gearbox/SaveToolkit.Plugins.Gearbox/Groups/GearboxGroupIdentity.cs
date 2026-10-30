using System.Globalization;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Gearbox row namespaces, navigation ancestors and inventory-aware selection validation.</summary>
internal static class GearboxGroupIdentity
{
    public static DocumentMemberIdentityPolicy Policy { get; } = new()
    {
        QueryRowIdentity = static (collection, key) => collection + "/" + key,
        NavigationPath = static (role, path) => ["section:Gearbox character + profile",
            role == GearboxGroups.ProfileRole ? "section:Shared profile / bank" : "section:Character progression / backpack",
            .. path.Select(part => part.StartsWith("section:", StringComparison.Ordinal) ? "section:" + role + "/" + part[8..] : part)],
        LocalNavigationPath = LocalPath,
        LocalScope = LocalScope,
    };

    private static IReadOnlyList<string> LocalPath(string role, IReadOnlyList<string> path)
    {
        var ancestors = Policy.NavigationPath(role, []);
        if (path.Count < ancestors.Count || !path.Take(ancestors.Count).SequenceEqual(ancestors, StringComparer.Ordinal))
            throw new InvalidOperationException("The effect path belongs to another Gearbox member.");
        var prefix = "section:" + role + "/";
        return path.Skip(ancestors.Count).Select(part => part.StartsWith(prefix, StringComparison.Ordinal)
            ? "section:" + part[prefix.Length..] : part).ToArray();
    }

    private static ActionScope LocalScope(DocumentMemberAdapter member, ActionScope scope) => new(LocalRows(member, scope))
    {
        // Preserve the existing query/exclusion contract; Core already resolves the selected rows.
        QueryScope = scope.QueryScope is { } query ? query with { CollectionId = member.Local(query.CollectionId) } : null,
    };

    private static IReadOnlyList<string>? LocalRows(DocumentMemberAdapter member, ActionScope scope)
    {
        if (scope.RowKeys is not { Count: > 0 } keys) return scope.RowKeys;
        // Read the current adapter's model: whole-group restore replaces both documents.
        var document = (GearboxDocument)member.Document;
        var rows = document.Inventory.Entries.ToDictionary(entry => member.Prefix(entry.List + "/" + entry.Id),
            entry => (Collection: entry.List, Key: entry.Id), StringComparer.Ordinal);
        // Ammo is character-only; the profile schema contains no resource_pools field.
        // Profile selections still resolve exclusively through their exact inventory row identities.
        var poolCount = document.Profile ? 0 : document.Root.Children("resource_pools").Count();
        for (var index = 0; index < poolCount; index++)
        {
            var key = index.ToString(CultureInfo.InvariantCulture);
            rows.Add(member.Prefix("ammo/" + key), ("ammo", key));
        }
        return keys.Select(key =>
        {
            if (!member.Owns(key)) throw new KeyNotFoundException("Unknown Gearbox group identity.");
            // Match the complete role/collection/key. Never reinterpret a foreign collection's suffix.
            if (!rows.TryGetValue(key, out var row))
                throw new SaveFormatException("The selection is not an existing row in this member and collection.");
            if (scope.QueryScope is { } query && query.CollectionId != member.Prefix(row.Collection))
            {
                if (!member.Owns(query.CollectionId)) throw new KeyNotFoundException("Unknown Gearbox group identity.");
                throw new SaveFormatException("The resolved selection belongs to another collection.");
            }
            return row.Key;
        }).ToArray();
    }
}
