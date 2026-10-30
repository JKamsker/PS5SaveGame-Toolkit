using System.Globalization;
using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk.References;

/// <summary>Where a reference pack came from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferencePackOrigin>))]
public enum ReferencePackOrigin
{
    /// <summary>Shipped inside a plugin assembly as embedded resources.</summary>
    Embedded,

    /// <summary>Imported by the user from a local file.</summary>
    User,
}

/// <summary>The content category of an attributed reference-pack dataset.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReferenceDatasetKind>))]
public enum ReferenceDatasetKind
{
    /// <summary>Keyed records (id, name, category, limit, description, asset, source).</summary>
    Records,

    /// <summary>Images (PNG, JPEG, WebP) addressed by asset id.</summary>
    Assets,
}

/// <summary>
/// Who made a dataset and under which terms it may be used. Every dataset in a pack carries one;
/// the UI shows it wherever the data appears (see rule 9 in docs/PLUGIN_GUIDE.md).
/// </summary>
/// <param name="SourceUrl">Where the data was taken from (http/https).</param>
/// <param name="Author">Author, project or community, e.g. "Fextralife wiki contributors".</param>
/// <param name="License">Licence name or SPDX id, e.g. "CC-BY-SA-4.0".</param>
/// <param name="LicenseUrl">Link to the licence text (http/https).</param>
/// <param name="Retrieved">Date the data was retrieved.</param>
/// <param name="Modifications">What was changed or extracted ("None" when used verbatim).</param>
public sealed record ReferenceAttribution(
    string SourceUrl,
    string Author,
    string License,
    string LicenseUrl,
    DateOnly Retrieved,
    string Modifications)
{
    /// <summary>One line suitable for a field description, e.g. "Example wiki contributors, CC-BY-SA-4.0, https://…".</summary>
    public string ToShortText() => string.Create(CultureInfo.InvariantCulture,
        $"{Author}, {License}, {SourceUrl} (retrieved {Retrieved:yyyy-MM-dd})");
}

/// <summary>One attributed dataset of a pack: a records catalog or a set of assets.</summary>
public sealed record ReferenceDatasetInfo(string PackId, string Id, ReferenceDatasetKind Kind, ReferenceAttribution Attribution)
{
    /// <summary>Optional human-readable dataset title.</summary>
    public string? Title { get; init; }

    /// <summary>Records or assets in the dataset.</summary>
    public int ItemCount { get; init; }
}

/// <summary>Identity of a loaded reference pack.</summary>
public sealed record ReferencePackInfo(string Id, string Version, string Title, ReferencePackOrigin Origin)
{
    /// <summary>SHA-256 identity of the validated manifest and every listed file hash.</summary>
    /// <remarks>This is content identity, not a display-name, locale or source-path identity.</remarks>
    public string? ContentFingerprint { get; init; }

    /// <summary>Optional explanation of the pack's contents and limitations.</summary>
    public string? Description { get; init; }

    /// <summary>Plugins that may use the pack.</summary>
    public IReadOnlyList<string> PluginIds { get; init; } = [];

    /// <summary>Titles the pack applies to. Empty: every title the plugins open.</summary>
    public IReadOnlyList<string> TitleIds { get; init; } = [];

    /// <summary>Game builds the pack was made for. Empty: build-agnostic.</summary>
    public IReadOnlyList<string> GameBuilds { get; init; } = [];

    /// <summary>BCP-47 tag such as "en" or "de-DE"; null for language-neutral data.</summary>
    public string? Locale { get; init; }

    /// <summary>Attributed record and asset datasets supplied by this pack.</summary>
    public IReadOnlyList<ReferenceDatasetInfo> Datasets { get; init; } = [];
}
