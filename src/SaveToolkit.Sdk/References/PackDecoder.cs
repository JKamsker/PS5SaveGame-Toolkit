using SaveToolkit.Sdk;

namespace SaveToolkit.Sdk.References;

/// <summary>
/// Validates every listed file at load time. Embedded files remain in their assembly; only metadata
/// is retained until a catalog or image is requested. Disposable archive sources are snapshotted eagerly.
/// </summary>
internal static class PackDecoder
{
    public static DecodedReferencePack Load(IPackFileSource source, ReferencePackOrigin origin,
        EmbeddedReferencePackLimits limits, ReferenceStringPool? strings = null)
    {
        strings ??= new();
        var manifestBytes = source.Read(PackManifest.FileName, limits.MaxManifestBytes)
            ?? throw new EmbeddedReferencePackException($"Reference pack {source.Origin} has no {PackManifest.FileName}.");
        var manifest = PackJson.Parse<PackManifest>(manifestBytes, $"{PackManifest.FileName} of {source.Origin}");
        var problems = new PackProblems(manifest.Id is { } id && PackPaths.IsValidPackId(id) ? $"'{id}'" : source.Origin);
        PackManifestValidator.Validate(manifest, problems, limits);
        problems.ThrowIfAny();

        var budget = new PackByteBudget(limits.MaxTotalBytes);
        var datasets = manifest.Datasets!;
        var orderedDatasets = new List<ReferenceDatasetInfo>();
        var info = new ReferencePackInfo(manifest.Id!, manifest.Version!, manifest.Title!.Trim(), origin)
        {
            Description = manifest.Description,
            PluginIds = [.. manifest.Plugins!],
            TitleIds = [.. manifest.TitleIds ?? []],
            GameBuilds = [.. manifest.GameBuilds ?? []],
            Locale = manifest.Locale,
            Datasets = orderedDatasets,
        };
        // Only an immutable embedded source may outlive this call. User ZIP streams are disposed by
        // the caller; retaining their complete decoded snapshot also preserves replacement/removal.
        var deferred = source is EmbeddedPackSource;
        var datasetInfos = new Dictionary<string, ReferenceDatasetInfo>(StringComparer.Ordinal);
        var assets = new Dictionary<string, DecodedAsset>(StringComparer.Ordinal);
        foreach (var dataset in datasets.Where(d => d.Kind == "assets"))
        {
            var content = LoadAssets(source, dataset, limits, budget, problems);
            var datasetInfo = DatasetInfo(info.Id, dataset, content.Count);
            datasetInfos.Add(datasetInfo.Id, datasetInfo);
            foreach (var file in dataset.Files!)
            {
                if (!content.TryGetValue(file.Path!, out var image)) continue;
                assets.Add(file.Path!, deferred
                    ? DeferredAsset(source, file, datasetInfo, limits)
                    : new DecodedAsset(new Lazy<AssetContent>(() => image), datasetInfo));
            }
        }

        var assetPaths = assets.Keys.ToHashSet(StringComparer.Ordinal);
        var catalogs = new Dictionary<string, Lazy<DecodedReferenceCatalog>>(StringComparer.Ordinal);
        foreach (var dataset in datasets.Where(d => d.Kind == "records"))
        {
            var records = PackRecordReader.Load(source, dataset, limits, budget, assetPaths, problems);
            var datasetInfo = DatasetInfo(info.Id, dataset, records.Count);
            datasetInfos.Add(datasetInfo.Id, datasetInfo);
            catalogs.Add(datasetInfo.Id, deferred
                ? DeferredCatalog(source, dataset, info, datasetInfo, limits, strings)
                : SnapshotCatalog(records, info, datasetInfo, strings));
        }
        problems.ThrowIfAny();
        // Preserve manifest ordering and the same metadata instances used by catalogs/provenance.
        orderedDatasets.AddRange(datasets.Select(d => datasetInfos[d.Id!]));
        // Construct snapshots only after the whole pack passes validation. In particular, duplicate
        // keys must produce the pack diagnostic above, not a catalog dictionary exception.
        if (!deferred)
            foreach (var catalog in catalogs.Values) _ = catalog.Value;
        return new DecodedReferencePack(info, catalogs, assets);
    }

    private static ReferenceDatasetInfo DatasetInfo(string packId, PackDatasetModel dataset, int count) =>
        new(packId, dataset.Id!, PackManifestValidator.Kind(dataset.Kind)!.Value,
            PackManifestValidator.ToAttribution(dataset.Attribution!)) { Title = dataset.Title, ItemCount = count };

    private static Lazy<DecodedReferenceCatalog> DeferredCatalog(IPackFileSource source, PackDatasetModel model,
        ReferencePackInfo pack, ReferenceDatasetInfo dataset, EmbeddedReferencePackLimits limits,
        ReferenceStringPool strings) => new(() =>
        {
            var records = PackRecordReader.ReadValidated(source, model, limits, strings);
            return CreateCatalog(records, pack, dataset, strings, attributesAreShared: true);
        }, LazyThreadSafetyMode.ExecutionAndPublication);

    private static Lazy<DecodedReferenceCatalog> SnapshotCatalog(List<RecordModel> records, ReferencePackInfo pack,
        ReferenceDatasetInfo dataset, ReferenceStringPool strings) =>
        new(() => CreateCatalog(records, pack, dataset, strings));

    private static DecodedReferenceCatalog CreateCatalog(List<RecordModel> records, ReferencePackInfo pack,
        ReferenceDatasetInfo dataset, ReferenceStringPool strings, bool attributesAreShared = false) =>
        new(pack, dataset, records.Select(record => strings.ToRecord(record, pack.Id, dataset, attributesAreShared)).ToArray());

    private static DecodedAsset DeferredAsset(IPackFileSource source, PackFileModel file,
        ReferenceDatasetInfo dataset, EmbeddedReferencePackLimits limits) => new(new Lazy<AssetContent>(() =>
        {
            var problems = new PackProblems($"'{dataset.PackId}'");
            var image = LoadAsset(source, file, limits, new PackByteBudget(limits.MaxTotalBytes), problems);
            problems.ThrowIfAny();
            return image!;
        }, LazyThreadSafetyMode.ExecutionAndPublication), dataset);

    private static Dictionary<string, AssetContent> LoadAssets(IPackFileSource source, PackDatasetModel dataset,
        EmbeddedReferencePackLimits limits, PackByteBudget budget, PackProblems problems)
    {
        var result = new Dictionary<string, AssetContent>(StringComparer.Ordinal);
        foreach (var file in dataset.Files!)
        {
            if (LoadAsset(source, file, limits, budget, problems) is { } image) result.Add(file.Path!, image);
        }
        return result;
    }

    private static AssetContent? LoadAsset(IPackFileSource source, PackFileModel file,
        EmbeddedReferencePackLimits limits, PackByteBudget budget, PackProblems problems)
    {
        if (PackFileVerifier.Read(source, file, limits.MaxAssetBytes, normaliseText: false, budget, problems) is not { } bytes)
            return null;
        var image = ImageSniffer.Sniff(bytes);
        if (image is null)
            problems.Add($"'{file.Path}' is not a PNG, JPEG or WebP image");
        else if (!ImageSniffer.ExtensionMatches(file.Path!, image.ContentType))
            problems.Add($"'{file.Path}' contains {image.ContentType} but its extension says otherwise");
        else if (image.Width > limits.MaxImageDimension || image.Height > limits.MaxImageDimension)
            problems.Add($"'{file.Path}' is {image.Width}x{image.Height}; the limit is {limits.MaxImageDimension}x{limits.MaxImageDimension}");
        else
            return new AssetContent(image.ContentType, bytes);
        return null;
    }

    // Kept as an internal facade for the host's shared normalization behavior.
    internal static byte[] NormaliseLineEndings(byte[] bytes) => PackFileVerifier.NormaliseLineEndings(bytes);
}
