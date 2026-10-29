using System.Globalization;
using System.Text.RegularExpressions;

namespace SaveToolkit.Sdk.References;

/// <summary>Collects validation problems and throws them as one <see cref="EmbeddedReferencePackException"/>.</summary>
internal sealed class PackProblems(string origin)
{
    private const int MaxListed = 20;
    private readonly List<string> _problems = [];

    public void Add(string problem) => _problems.Add(problem);

    public void Check(bool ok, string problem)
    {
        if (!ok)
        {
            _problems.Add(problem);
        }
    }

    public void ThrowIfAny()
    {
        if (_problems.Count == 0)
        {
            return;
        }

        var listed = string.Join("; ", _problems.Take(MaxListed));
        var more = _problems.Count > MaxListed ? $" (and {_problems.Count - MaxListed} more)" : string.Empty;
        throw new EmbeddedReferencePackException($"Reference pack {origin} is invalid: {listed}{more}.");
    }
}

/// <summary>Structural checks of <c>pack.json</c>. File contents are checked by <see cref="PackDecoder"/>.</summary>
internal static partial class PackManifestValidator
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2000;

    public static void Validate(PackManifest m, PackProblems problems, EmbeddedReferencePackLimits limits)
    {
        problems.Check(m.Schema == PackManifest.CurrentSchema, $"schema must be {PackManifest.CurrentSchema}, found {m.Schema}");
        problems.Check(PackPaths.IsValidPackId(m.Id), "id must be lower-case letters, digits, '.', '_' or '-' (max 64)");
        problems.Check(m.Version is not null && VersionPattern().IsMatch(m.Version), "version is required (e.g. \"1.0.0\")");
        problems.Check(IsText(m.Title, MaxTitleLength), "title is required (max 200 characters)");
        problems.Check(m.Description is null || m.Description.Length <= MaxDescriptionLength, "description is longer than 2000 characters");
        problems.Check(m.Plugins is { Count: > 0 } && m.Plugins.All(p => IsText(p, 100)), "plugins must list at least one plugin id");
        problems.Check(m.TitleIds is null || m.TitleIds.All(t => t is not null && TitleIdPattern().IsMatch(t)), "titleIds must look like PPSA01234");
        problems.Check(m.GameBuilds is null || m.GameBuilds.All(b => IsText(b, 64)), "gameBuilds entries must be non-empty (max 64 characters)");
        problems.Check(m.Locale is null || LocalePattern().IsMatch(m.Locale), "locale must be a BCP-47 tag such as \"en\" or \"de-DE\"");

        if (m.Datasets is not { Count: > 0 })
        {
            problems.Add("datasets must contain at least one dataset");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in m.Datasets)
        {
            if (d is null)
            {
                problems.Add("datasets must not contain null entries");
                continue;
            }

            ValidateDataset(d, problems, ids, paths);
        }

        problems.Check(paths.Count <= limits.MaxFiles, $"more than {limits.MaxFiles:N0} files");
    }

    public static ReferenceDatasetKind? Kind(string? kind) => kind switch
    {
        "records" => ReferenceDatasetKind.Records,
        "assets" => ReferenceDatasetKind.Assets,
        _ => null,
    };

    public static ReferenceAttribution ToAttribution(PackAttributionModel a) => new(
        a.SourceUrl!, a.Author!.Trim(), a.License!.Trim(), a.LicenseUrl!, ParseDate(a.Retrieved)!.Value, a.Modifications!.Trim());

    private static void ValidateDataset(PackDatasetModel d, PackProblems problems, HashSet<string> ids, HashSet<string> paths)
    {
        var name = d.Id ?? "?";
        problems.Check(PackPaths.IsValidDatasetId(d.Id), $"dataset '{name}': id must be lower-case letters, digits, '_' or '-'");
        problems.Check(d.Id is null || ids.Add(d.Id), $"dataset '{name}' is declared twice");
        problems.Check(Kind(d.Kind) is not null, $"dataset '{name}': kind must be \"records\" or \"assets\"");
        problems.Check(d.Title is null || d.Title.Length <= MaxTitleLength, $"dataset '{name}': title is too long");
        problems.Check(d.Files is { Count: > 0 }, $"dataset '{name}': files must list at least one file");
        foreach (var f in d.Files ?? [])
        {
            if (f is null)
            {
                problems.Add($"dataset '{name}': files must not contain null entries");
                continue;
            }

            problems.Check(PackPaths.IsSafeRelativePath(f.Path), $"dataset '{name}': unsafe path '{f.Path}' (use relative forward-slash paths without '..')");
            problems.Check(f.Path is null || !string.Equals(f.Path, PackManifest.FileName, StringComparison.OrdinalIgnoreCase), $"dataset '{name}': cannot list {PackManifest.FileName}");
            problems.Check(f.Path is null || paths.Add(f.Path), $"dataset '{name}': file '{f.Path}' is listed twice");
            problems.Check(f.Sha256 is not null && Sha256Pattern().IsMatch(f.Sha256), $"dataset '{name}': file '{f.Path}' needs a sha256 (64 hex digits)");
        }

        ValidateAttribution(d.Attribution, $"dataset '{name}'", problems);
    }

    private static void ValidateAttribution(PackAttributionModel? a, string where, PackProblems problems)
    {
        if (a is null)
        {
            problems.Add($"{where}: attribution is required (sourceUrl, author, license, licenseUrl, retrieved, modifications)");
            return;
        }

        problems.Check(PackPaths.IsHttpUrl(a.SourceUrl), $"{where}: attribution.sourceUrl must be an http(s) URL");
        problems.Check(IsText(a.Author, MaxTitleLength), $"{where}: attribution.author is required");
        problems.Check(IsText(a.License, 100), $"{where}: attribution.license is required");
        problems.Check(PackPaths.IsHttpUrl(a.LicenseUrl), $"{where}: attribution.licenseUrl must be an http(s) URL");
        problems.Check(ParseDate(a.Retrieved) is not null, $"{where}: attribution.retrieved must be a date (yyyy-MM-dd)");
        problems.Check(IsText(a.Modifications, MaxDescriptionLength), $"{where}: attribution.modifications is required (\"None\" if unchanged)");
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static bool IsText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Any(char.IsControl);

    [GeneratedRegex("^[0-9A-Za-z][0-9A-Za-z.+-]{0,31}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[A-Z]{4}[0-9]{5}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex TitleIdPattern();

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex LocalePattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
