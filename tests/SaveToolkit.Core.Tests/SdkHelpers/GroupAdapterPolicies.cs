using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests.SdkHelpers;

// Explicit migration recipes. Production SDK code contains no plugin/title-specific policy.
internal static class GroupAdapterPolicies
{
    public static DocumentMemberIdentityPolicy Gearbox { get; } = new()
    {
        QueryRowIdentity = static (collection, key) => collection + "/" + key,
        NavigationPath = static (role, path) => ["section:Gearbox character + profile",
            role == "profile" ? "section:Shared profile / bank" : "section:Character progression / backpack",
            .. path.Select(part => part.StartsWith("section:", StringComparison.Ordinal) ? "section:" + role + "/" + part[8..] : part)],
    };

    public static DocumentMemberIdentityPolicy XorJson { get; } = new()
    {
        TableCellScope = static (scope, row, _) => scope + "/" + row,
        ExpansionScope = static (_, _) => "view",
        PrefixGraphIds = false,
        PrefixMapMarkerIds = false,
        CollectionColumn = static (document, column) => column with
        { Spec = column.Spec is { } spec ? spec with { ReadOnly = !document.CanEdit } : null },
        NavigationPath = static (role, path) => ["section:XorJson character + global", "section:" + role, .. path],
    };
}
