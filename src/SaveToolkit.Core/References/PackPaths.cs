using System.Text.RegularExpressions;

namespace SaveToolkit.Core.References;

/// <summary>Validation of ids and pack-relative paths. Paths never reach the file system; this keeps them boring anyway.</summary>
internal static partial class PackPaths
{
    public const int MaxPathLength = 200;

    /// <summary>Lower-case, dotted ids: "pragmata.names.en". Also safe as a file name.</summary>
    public static bool IsValidPackId(string? id) => id is not null && PackIdPattern().IsMatch(id);

    /// <summary>Dataset/catalog ids: "items", "weapon-icons".</summary>
    public static bool IsValidDatasetId(string? id) => id is not null && DatasetIdPattern().IsMatch(id);

    /// <summary>
    /// Forward-slash relative path of plain segments: no "..", no leading slash, no drive letters,
    /// no backslashes, no colons (asset ids use ':' as the pack separator).
    /// </summary>
    public static bool IsSafeRelativePath(string? path) =>
        path is { Length: > 0 and <= MaxPathLength } && SafePathPattern().IsMatch(path);

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static string QualifyAsset(string packId, string path) => $"{packId}:{path}";

    public static bool TrySplitAsset(string assetId, out string packId, out string path)
    {
        var colon = assetId.IndexOf(':', StringComparison.Ordinal);
        packId = colon > 0 ? assetId[..colon] : string.Empty;
        path = colon > 0 ? assetId[(colon + 1)..] : string.Empty;
        return colon > 0 && IsValidPackId(packId) && IsSafeRelativePath(path);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex DatasetIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9._-]*(/[A-Za-z0-9_][A-Za-z0-9._-]*)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafePathPattern();
}
