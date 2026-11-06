using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>Document reference lookups (catalog search, records, field candidates), shared by every API host.</summary>
internal static class ReferenceOperations
{
    public const string CrossSiteMessage = "Reference packs can only be changed from the toolkit itself.";

    public static void ValidateSort(ReferenceSortKey? key)
    {
        if (key is { } value && !Enum.IsDefined(value))
        {
            throw new ApiStatusException("Unknown reference sort key.");
        }
    }

    public static DocumentSourcesDto Sources(Workspace workspace, DocumentSession document) =>
        new([.. workspace.References(document.Id).Datasets().Select(s => s.ToDto())]);

    public static ReferencePageDto Search(Workspace workspace, DocumentSession document, string catalogId, string? q,
        string? category, ReferenceSortKey? sortBy, bool? descending, int? offset, int? limit)
    {
        var catalog = Catalog(workspace, document, catalogId);
        ValidateSort(sortBy);
        var query = new ReferenceQuery
        {
            Text = q,
            Category = string.IsNullOrEmpty(category) ? null : category,
            SortBy = sortBy,
            Descending = descending ?? false,
            Offset = Math.Max(0, offset ?? 0),
            Limit = Math.Clamp(limit ?? ReferenceQuery.DefaultLimit, 1, ReferenceQuery.MaxLimit),
        };
        var page = catalog.Search(query);
        return new ReferencePageDto(catalogId, catalog.Pack.Id, [.. page.Items.Select(r => r.ToDto())], page.Total, page.Offset, query.Limit, catalog.Categories)
        {
            SortKeys = catalog.SortKeys,
        };
    }

    public static ReferenceRecordDetailDto Record(Workspace workspace, DocumentSession document, string catalogId, string key)
    {
        var catalog = Catalog(workspace, document, catalogId);
        var record = catalog.Find(key) ?? throw new KeyNotFoundException($"No record '{key}' in catalog '{catalogId}'.");
        return new ReferenceRecordDetailDto(record.ToDto(), catalog.Pack.Title, catalog.Pack.Version, record.Dataset);
    }

    public static ReferenceCandidatePage Candidates(DocumentSession document, string valueId, string? q, string? category,
        int? offset, int? limit, long? revision, string? actionId, ReferenceSortKey? sortBy, bool? descending)
    {
        ValidateSort(sortBy);
        return document.SearchReferences(valueId, new ReferenceQuery
        {
            Text = q,
            Category = string.IsNullOrEmpty(category) ? null : category,
            SortBy = sortBy,
            Descending = descending ?? false,
            Offset = offset ?? 0,
            Limit = limit ?? ReferenceQuery.DefaultLimit,
        }, revision, actionId);
    }

    public static ReferenceRecordsResponse Records(DocumentSession document, ReferenceRecordsRequest request)
    {
        long revision = document.State.Revision;
        if (request.Revision is { } expected && expected != revision) throw new StaleViewException();
        var results = new List<ReferenceRecordResult>(request.Pairs.Count);
        foreach (var pair in request.Pairs)
        {
            try
            {
                // Always resolve the published field, even when another pair has the same key.
                // The document gate holds across the whole batch; the captured revision also
                // rejects interleaving mutations by non-HTTP Core callers.
                var candidate = document.FindReference(pair.FieldId, pair.RecordKey, revision, pair.ActionId);
                results.Add(new(pair.FieldId, pair.RecordKey, pair.ActionId, 200, candidate));
            }
            catch (KeyNotFoundException error)
            {
                results.Add(new(pair.FieldId, pair.RecordKey, pair.ActionId, 404, Error: error.Message));
            }
        }
        return new ReferenceRecordsResponse(revision, results);
    }

    private static IReferenceCatalog Catalog(Workspace workspace, DocumentSession document, string catalogId) =>
        workspace.References(document.Id).CatalogForHost(catalogId)
        ?? throw new KeyNotFoundException($"No reference pack for this document provides catalog '{catalogId}'.");
}
