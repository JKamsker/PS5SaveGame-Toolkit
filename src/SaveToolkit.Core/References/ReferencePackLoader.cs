using System.Collections.ObjectModel;
using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.References;

/// <summary>
/// Loads and validates one pack: manifest, size limits, SHA-256 of every listed file, safe image types
/// and dimensions, record shape, and record → asset references. Anything wrong rejects the whole pack.
/// </summary>
/// <remarks>
/// Records files are hashed after converting CRLF to LF, so a git checkout with <c>core.autocrlf</c>
/// does not break embedded packs. Assets are hashed as stored.
/// </remarks>
internal static class ReferencePackLoader
{
    private const int MaxKeyLength = 256;
    private const int MaxNameLength = 512;
    private const int MaxCategoryLength = 128;
    private const int MaxRecordDescriptionLength = 8192;
    private const int MaxAttributes = 32;
    private const int MaxAttributeLength = 1024;

    public static LoadedReferencePack Load(IPackFileSource source, ReferencePackOrigin origin, ReferencePackLimits limits)
    {
        var manifestBytes = source.Read(PackManifest.FileName, limits.MaxManifestBytes)
            ?? throw new ReferencePackException($"Reference pack {source.Origin} has no {PackManifest.FileName}.");
        var manifest = PackJson.Parse<PackManifest>(manifestBytes, $"{PackManifest.FileName} of {source.Origin}");

        var problems = new PackProblems(manifest.Id is { } id && PackPaths.IsValidPackId(id) ? $"'{id}'" : source.Origin);
        PackManifestValidator.Validate(manifest, problems, limits);
        problems.ThrowIfAny();

        var budget = new ByteBudget(limits.MaxTotalBytes);
        var datasets = manifest.Datasets!;
        var assetModels = datasets.Where(d => d.Kind == "assets").ToList();
        var recordModels = datasets.Where(d => d.Kind == "records").ToList();

        var rawAssets = assetModels.ToDictionary(d => d.Id!, d => LoadAssets(source, d, limits, budget, problems), StringComparer.Ordinal);
        var assetPaths = rawAssets.Values.SelectMany(a => a.Keys).ToHashSet(StringComparer.Ordinal);
        var rawRecords = recordModels.ToDictionary(d => d.Id!, d => LoadRecords(source, d, limits, budget, assetPaths, problems), StringComparer.Ordinal);
        problems.ThrowIfAny();

        var datasetInfos = datasets.Select(d => new ReferenceDatasetInfo(
                manifest.Id!,
                d.Id!,
                PackManifestValidator.Kind(d.Kind)!.Value,
                PackManifestValidator.ToAttribution(d.Attribution!))
            {
                Title = d.Title,
                ItemCount = rawAssets.TryGetValue(d.Id!, out var a) ? a.Count : rawRecords[d.Id!].Count,
            })
            .ToList();

        var info = new ReferencePackInfo(manifest.Id!, manifest.Version!, manifest.Title!.Trim(), origin)
        {
            Description = manifest.Description,
            PluginIds = [.. manifest.Plugins!],
            TitleIds = [.. manifest.TitleIds ?? []],
            GameBuilds = [.. manifest.GameBuilds ?? []],
            Locale = manifest.Locale,
            Datasets = datasetInfos,
        };

        var byId = datasetInfos.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var assets = rawAssets
            .SelectMany(kv => kv.Value.Select(f => (Path: f.Key, Asset: new LoadedAsset(f.Value, byId[kv.Key]))))
            .ToDictionary(x => x.Path, x => x.Asset, StringComparer.Ordinal);
        var catalogs = rawRecords.ToDictionary(
            kv => kv.Key,
            kv => new ReferenceCatalog(info, byId[kv.Key], [.. kv.Value.Select(r => ToRecord(r, info.Id, byId[kv.Key]))]),
            StringComparer.Ordinal);
        return new LoadedReferencePack(info, catalogs, assets);
    }

    private static Dictionary<string, AssetContent> LoadAssets(
        IPackFileSource source, PackDatasetModel dataset, ReferencePackLimits limits, ByteBudget budget, PackProblems problems)
    {
        var result = new Dictionary<string, AssetContent>(StringComparer.Ordinal);
        foreach (var file in dataset.Files!)
        {
            if (ReadVerified(source, file, limits.MaxAssetBytes, normaliseText: false, budget, problems) is not { } bytes)
            {
                continue;
            }

            var image = ImageSniffer.Sniff(bytes);
            if (image is null)
            {
                problems.Add($"'{file.Path}' is not a PNG, JPEG or WebP image");
            }
            else if (!ImageSniffer.ExtensionMatches(file.Path!, image.ContentType))
            {
                problems.Add($"'{file.Path}' contains {image.ContentType} but its extension says otherwise");
            }
            else if (image.Width > limits.MaxImageDimension || image.Height > limits.MaxImageDimension)
            {
                problems.Add($"'{file.Path}' is {image.Width}x{image.Height}; the limit is {limits.MaxImageDimension}x{limits.MaxImageDimension}");
            }
            else
            {
                result[file.Path!] = new AssetContent(image.ContentType, bytes);
            }
        }

        return result;
    }

    private static List<RecordModel> LoadRecords(
        IPackFileSource source,
        PackDatasetModel dataset,
        ReferencePackLimits limits,
        ByteBudget budget,
        HashSet<string> assetPaths,
        PackProblems problems)
    {
        var records = new List<RecordModel>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in dataset.Files!)
        {
            if (ReadVerified(source, file, limits.MaxRecordsFileBytes, normaliseText: true, budget, problems) is not { } bytes)
            {
                continue;
            }

            var parsed = PackJson.Parse<RecordsFileModel>(bytes, $"'{file.Path}'");
            if (parsed.Records is null)
            {
                problems.Add($"'{file.Path}' must be an object with a \"records\" array");
                continue;
            }

            foreach (var (record, index) in parsed.Records.Select((r, i) => (r, i)))
            {
                var where = $"'{file.Path}' record {index}";
                if (ValidateRecord(record, where, assetPaths, problems))
                {
                    problems.Check(keys.Add(record.Id!), $"{where}: duplicate id '{record.Id}'");
                    records.Add(record);
                }
            }
        }

        problems.Check(records.Count <= limits.MaxRecordsPerCatalog, $"dataset '{dataset.Id}' has more than {limits.MaxRecordsPerCatalog:N0} records");
        return records;
    }

    private static bool ValidateRecord(RecordModel? r, string where, HashSet<string> assetPaths, PackProblems problems)
    {
        if (r is null)
        {
            problems.Add($"{where}: null record");
            return false;
        }

        var ok = true;
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                ok = false;
                problems.Add($"{where}: {message}");
            }
        }

        Check(PackManifestValidator.IsText(r.Id, MaxKeyLength), "id is required (max 256 characters)");
        Check(PackManifestValidator.IsText(r.Name, MaxNameLength), "name is required (max 512 characters)");
        Check(r.Category is null || PackManifestValidator.IsText(r.Category, MaxCategoryLength), "category is too long or empty");
        Check(r.Description is null || r.Description.Length <= MaxRecordDescriptionLength, "description is too long");
        Check(r.Limit is null or >= 0, "limit must not be negative");
        Check(r.Source is null || PackPaths.IsHttpUrl(r.Source), "source must be an http(s) URL");
        Check(r.AssetId is null || assetPaths.Contains(r.AssetId), $"assetId '{r.AssetId}' is not an asset of this pack");
        Check(
            r.Attributes is null || (r.Attributes.Count <= MaxAttributes && r.Attributes.All(a => PackManifestValidator.IsText(a.Key, 64) && a.Value is { Length: <= MaxAttributeLength })),
            "attributes: at most 32 entries, keys up to 64 and values up to 1024 characters");
        return ok;
    }

    private static byte[]? ReadVerified(IPackFileSource source, PackFileModel file, long maxBytes, bool normaliseText, ByteBudget budget, PackProblems problems)
    {
        var bytes = source.Read(file.Path!, maxBytes);
        if (bytes is null)
        {
            problems.Add($"'{file.Path}' is listed but missing");
            return null;
        }

        if (!budget.TryTake(bytes.Length))
        {
            throw new ReferencePackException($"Reference pack {source.Origin} is larger than {budget.Limit:N0} bytes in total.");
        }

        var hashed = normaliseText ? NormaliseLineEndings(bytes) : bytes;
        var actual = Convert.ToHexStringLower(SHA256.HashData(hashed));
        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"'{file.Path}' hash mismatch (manifest {file.Sha256!.ToLowerInvariant()}, file {actual})");
            return null;
        }

        return hashed;
    }

    /// <summary>CRLF → LF; lone CR is kept.</summary>
    internal static byte[] NormaliseLineEndings(byte[] bytes)
    {
        if (bytes.AsSpan().IndexOf("\r\n"u8) < 0)
        {
            return bytes;
        }

        var result = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == '\r' && i + 1 < bytes.Length && bytes[i + 1] == '\n')
            {
                continue;
            }

            result.Add(bytes[i]);
        }

        return [.. result];
    }

    private static ReferenceRecord ToRecord(RecordModel r, string packId, ReferenceDatasetInfo dataset) => new(r.Id!, r.Name!.Trim())
    {
        Category = r.Category,
        Limit = r.Limit,
        Description = r.Description,
        AssetId = r.AssetId is null ? null : PackPaths.QualifyAsset(packId, r.AssetId),
        Source = r.Source,
        Attributes = r.Attributes is { } attributes ? attributes.AsReadOnly() : ReadOnlyDictionary<string, string>.Empty,
        Dataset = dataset,
    };

    private sealed class ByteBudget(long limit)
    {
        private long _used;

        public long Limit => limit;

        public bool TryTake(long bytes)
        {
            _used += bytes;
            return _used <= limit;
        }
    }
}
