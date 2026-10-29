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
        LocalScope = LocalScope,
    };

    private static ActionScope LocalScope(DocumentMemberAdapter member, ActionScope scope) => new(scope.RowKeys?.Select(key =>
    {
        var local = member.Local(key);
        var separator = local.IndexOf('/');
        if (separator < 0) throw new SaveFormatException("The selection is not an existing item row in this member.");
        if (local[..separator] == "ammo")
        {
            if (scope.QueryScope is { } ammoQuery && member.Local(ammoQuery.CollectionId) != "ammo")
                throw new SaveFormatException("The resolved ammo selection belongs to another collection.");
            return local[(separator + 1)..];
        }
        // Read the current adapter's model: whole-group restore replaces both documents.
        var entry = ((GearboxDocument)member.Document).Inventory.Find(local[(separator + 1)..]);
        if (local[..separator] != entry.List) throw new SaveFormatException("The item row belongs to another collection.");
        if (scope.QueryScope is { } query && member.Local(query.CollectionId) != entry.List)
            throw new SaveFormatException("The resolved item selection belongs to another collection.");
        return entry.Id;
    }).ToArray())
    {
        // Preserve the existing query/exclusion contract; Core already resolves the selected rows.
        QueryScope = scope.QueryScope is { } query ? query with { CollectionId = member.Local(query.CollectionId) } : null,
    };
}
