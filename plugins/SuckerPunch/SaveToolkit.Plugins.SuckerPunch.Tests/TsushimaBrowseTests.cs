using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaBrowseTests
{
    [Theory]
    [InlineData(ReferenceSortKey.Name, false)]
    [InlineData(ReferenceSortKey.Name, true)]
    [InlineData(ReferenceSortKey.Key, false)]
    [InlineData(ReferenceSortKey.Key, true)]
    [InlineData(ReferenceSortKey.Category, false)]
    [InlineData(ReferenceSortKey.Category, true)]
    public void ReadOnlyGuidePickerFiltersSortsPagesAndPreservesSaveAndRevision(ReferenceSortKey sort, bool descending)
    {
        const string catalogId = "suckerpunch_charm_guide";
        var plugin = new SuckerPunchPlugin();
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser1"));
        var context = new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle);
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("browse", file, plugin, document, references: context);
        _ = session.BuildView();
        var guide = Assert.IsType<TableNode>(session.Expand("guide/" + catalogId)[1]);
        var field = Assert.IsType<FieldNode>(guide.Rows[0].Cells["name"]);
        Assert.True(Assert.IsType<ReferenceSpec>(field.Spec).ReadOnly);
        var state = session.State;
        var catalog = context.Catalog(catalogId)!;
        var category = catalog.Categories[0];
        var query = new ReferenceQuery { Category = category, SortBy = sort, Descending = descending, Limit = 3 };
        var records = catalog.Search(new ReferenceQuery { Category = category, Limit = 200 }).Items;
        Func<ReferenceRecord, string?> sortValue = record => sort switch
        {
            ReferenceSortKey.Key => record.Key,
            ReferenceSortKey.Name => record.Name,
            _ => record.Category,
        };
        var ordered = (descending ? records.OrderByDescending(sortValue, StringComparer.OrdinalIgnoreCase) :
            records.OrderBy(sortValue, StringComparer.OrdinalIgnoreCase)).ThenBy(record => record.Key, StringComparer.Ordinal).ToArray();
        Assert.True(ordered.Length > query.Limit);
        var page = session.SearchReferences(field.ValueId, query, state.Revision);
        Assert.Equal(ordered.Length, page.Total);
        Assert.Equal(ordered.Take(query.Limit).Select(record => record.Key), page.Items.Select(item => item.RawId));
        Assert.All(Enum.GetValues<ReferenceSortKey>(), key => Assert.Contains(key, page.SortKeys));
        Assert.All(page.Items, item =>
        {
            Assert.Equal(category, item.Category);
            Assert.NotNull(item.Dataset);
            Assert.Equal("CC-BY-SA-3.0", item.Dataset!.Attribution.License);
        });
        var next = session.SearchReferences(field.ValueId, query with { Offset = query.Limit }, state.Revision);
        Assert.Equal(ordered.Skip(query.Limit).Take(query.Limit).Select(record => record.Key),
            next.Items.Select(item => item.RawId));
        Assert.Empty(page.Items.Select(item => item.RawId).Intersect(next.Items.Select(item => item.RawId)));
        var candidate = page.Items[0];
        Assert.Equal(candidate.RawId, session.FindReference(field.ValueId, candidate.RawId, state.Revision).RawId);
        var searched = session.SearchReferences(field.ValueId, query with { Text = candidate.RawId }, state.Revision);
        Assert.Equal(candidate.RawId, Assert.Single(searched.Items).RawId);
        Assert.False(session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(candidate.RawId))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.Equal(state, session.State);
        Assert.Equal(file.ReadAllBytes(), SuckerPunchTestData.PrimaryOutput(document).Content.ToArray());
    }
}
