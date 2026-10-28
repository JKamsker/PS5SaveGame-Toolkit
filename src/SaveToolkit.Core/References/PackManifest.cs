using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Core.References;

/// <summary>Wire shape of <c>pack.json</c>. Unknown members are rejected so typos ("licence") fail loudly.</summary>
internal sealed record PackManifest
{
    public const int CurrentSchema = 1;
    public const string FileName = "pack.json";

    public int Schema { get; init; }
    public string? Id { get; init; }
    public string? Version { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public List<string>? Plugins { get; init; }
    public List<string>? TitleIds { get; init; }
    public List<string>? GameBuilds { get; init; }
    public string? Locale { get; init; }
    public List<PackDatasetModel>? Datasets { get; init; }
}

internal sealed record PackDatasetModel
{
    public string? Id { get; init; }
    public string? Kind { get; init; }
    public string? Title { get; init; }
    public List<PackFileModel>? Files { get; init; }
    public PackAttributionModel? Attribution { get; init; }
}

internal sealed record PackFileModel
{
    public string? Path { get; init; }
    public string? Sha256 { get; init; }
}

internal sealed record PackAttributionModel
{
    public string? SourceUrl { get; init; }
    public string? Author { get; init; }
    public string? License { get; init; }
    public string? LicenseUrl { get; init; }
    public string? Retrieved { get; init; }
    public string? Modifications { get; init; }
}

/// <summary>Wire shape of a records file: <c>{ "records": [ ... ] }</c>.</summary>
internal sealed record RecordsFileModel
{
    public List<RecordModel>? Records { get; init; }
}

internal sealed record RecordModel
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Category { get; init; }
    public long? Limit { get; init; }
    public string? Description { get; init; }
    public string? AssetId { get; init; }
    public string? Source { get; init; }
    public Dictionary<string, string>? Attributes { get; init; }
}

internal static class PackJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 16,
    };

    public static T Parse<T>(ReadOnlySpan<byte> utf8, string what)
    {
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            utf8 = utf8[3..];
        }

        try
        {
            return JsonSerializer.Deserialize<T>(utf8, Options) ?? throw new ReferencePackException($"{what} is empty.");
        }
        catch (JsonException ex)
        {
            throw new ReferencePackException($"{what} is not valid: {ex.Message}", ex);
        }
    }
}
