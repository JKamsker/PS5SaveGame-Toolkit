namespace SaveToolkit.Sdk.References;

/// <summary>Validates record files without retaining their temporary JSON models.</summary>
internal static class PackRecordReader
{
    private const int MaxKeyLength = 256;
    private const int MaxNameLength = 512;
    private const int MaxCategoryLength = 128;
    private const int MaxRecordDescriptionLength = 8192;
    private const int MaxAttributes = 32;
    private const int MaxAttributeLength = 1024;

    /// <summary>
    /// Reopens immutable embedded files whose hashes, shape and cross-references already passed Load.
    /// Avoid repeating per-record validation/diagnostic allocation during the first document open.
    /// </summary>
    public static List<RecordModel> ReadValidated(IPackFileSource source, PackDatasetModel dataset,
        EmbeddedReferencePackLimits limits, ReferenceStringPool strings)
    {
        var records = new List<RecordModel>();
        foreach (var file in dataset.Files!)
        {
            var bytes = source.Read(file.Path!, limits.MaxRecordsFileBytes)
                ?? throw new EmbeddedReferencePackException($"'{file.Path}' is listed but missing.");
            var parsed = PackJson.Parse<RecordsFileModel>(bytes, $"'{file.Path}'", strings.JsonOptions);
            records.AddRange(parsed.Records!);
        }
        return records;
    }

    public static List<RecordModel> Load(
        IPackFileSource source,
        PackDatasetModel dataset,
        EmbeddedReferencePackLimits limits,
        PackByteBudget budget,
        HashSet<string> assetPaths,
        PackProblems problems)
    {
        var records = new List<RecordModel>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in dataset.Files!)
        {
            if (PackFileVerifier.Read(source, file, limits.MaxRecordsFileBytes, normaliseText: true, budget, problems) is not { } bytes)
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

}
