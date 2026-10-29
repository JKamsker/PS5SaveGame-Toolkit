using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseReadOnlyViewTests
{
    [Fact]
    public void CatalogPagingFilteringAndSortingCoverRowsAfterTheFirstTwoHundred()
    {
        var plugin = new TalesOfArisePlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02745");
        using var document = plugin.Open(AriseSampleTests.GameplayFiles().First(), context);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var catalog = context.Catalog("equipment")!;
        var beyondFirstPage = catalog.Search(new() { Offset = 400, Limit = 1 }).Items[0];
        var found = queryable.Query("equipment", new() { Text = beyondFirstPage.Key });
        Assert.Equal(beyondFirstPage.Key, Assert.Single(found.Rows).Key);
        var all = new List<TableRow>();
        for (var offset = 0; offset < catalog.Count; offset += 73)
        {
            var page = queryable.Query("equipment", new() { Offset = offset, Limit = 73, SortColumn = "name" });
            Assert.Equal(catalog.Count, page.FilteredCount);
            Assert.Equal(73, page.Limit);
            all.AddRange(page.Rows);
        }

        Assert.Equal(catalog.Count, all.Count);
        Assert.Equal(catalog.Count, all.Select(row => row.Key).Distinct().Count());
        var names = all.Select(row => Assert.IsType<TextNode>(row.Cells["name"]).Text).ToArray();
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
        var reverse = queryable.Query("equipment", new() { SortColumn = "name", Descending = true, Limit = 1 });
        Assert.Equal(names[^1], Assert.IsType<TextNode>(Assert.Single(reverse.Rows).Cells["name"]).Text);
        var skills = queryable.Query("skill-nodes", new()
        {
            Filters = new Dictionary<string, string> { ["character"] = "Shionne", ["title"] = "Escape Artist" },
        });
        Assert.Equal(5, skills.FilteredCount);
        Assert.Equal(5, skills.Rows.Count);
        Assert.Empty(queryable.Query("equipment", new() { Text = "No such reference record" }).Rows);
        Assert.Throws<ArgumentException>(() => queryable.Query("equipment", new() { SortColumn = "unpublished" }));
    }

    [Fact]
    public void AllSampleViewsAndQueryPagesAreReadOnlyAndPreserveEveryByte()
    {
        var plugin = new TalesOfArisePlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02745");
        foreach (var file in AriseSampleTests.GameplayFiles())
        {
            using var document = plugin.Open(file, context);
            var view = Assert.IsType<TabsNode>(document.BuildView());
            foreach (var title in new[] { "Character sheets", "Artes and skills", "Progress" })
            {
                Assert.Contains(view.Tabs, tab => tab.Title == title);
            }

            var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
            foreach (var collection in queryable.Collections.Where(collection => collection.Id != AriseInventoryQuery.CollectionId))
            {
                foreach (var offset in new[] { 0, checked((int)Math.Max(0, collection.TotalCount - 3)) })
                {
                    var page = queryable.Query(collection.Id, new() { Offset = offset, Limit = 3 });
                    Assert.InRange(page.Rows.Count, 1, 3);
                    foreach (var field in page.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>())
                    {
                        Assert.True(field.Spec.ReadOnly);
                        Assert.Equal(field.Value, document.GetValue(field.ValueId));
                        Assert.False(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
                    }
                }
            }

            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
            Assert.Throws<KeyNotFoundException>(() => document.InvokeAction("skills/unlock-all", new Dictionary<string, FieldValue>()));
            Assert.Throws<KeyNotFoundException>(() => document.InvokeAction("progress/complete", new Dictionary<string, FieldValue>()));
        }
    }

    [Fact]
    public void CharacterSheetsAndRawEvidenceStayLiveWithoutUnlockOrRecruitmentClaims()
    {
        var plugin = new TalesOfArisePlugin();
        using var document = plugin.Open(AriseSampleTests.GameplayFiles().First());
        var view = document.BuildView();
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = queryable.Query("evidence/032737EA", new() { Offset = 1, Limit = 1 });
        var level = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["value"]);
        Assert.Equal(Confidence.Unknown, level.Confidence);
        Assert.Equal(5, level.Value.AsInt64());
        Assert.True(document.ApplyEdits([new("character/0/level", FieldValue.Of(6L))]).Succeeded);
        Assert.Equal(6, document.GetValue(level.ValueId).AsInt64());
        Assert.Equal(6, document.GetValue("sheet/0/level").AsInt64());
        Assert.False(document.ApplyEdits([new("sheet/0/level", FieldValue.Of(7L))]).Succeeded);
        var text = JsonSerializer.Serialize(view);
        Assert.Contains("Recruitment: Unknown", text);
        Assert.Contains("No completion percentage", text);
        Assert.Empty(queryable.Query("artes", new()).Rows); // Raw evidence works without any pinned pack.
    }

    [Fact]
    public void CoreRegistersPagedFieldsAndProtectsUnknownProgressFromEdits()
    {
        var plugin = new TalesOfArisePlugin();
        var file = Assert.IsType<DiskSaveFile>(AriseSampleTests.GameplayFiles().First());
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02745");
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("arise-r2", file, plugin, document, references: references);
        _ = session.BuildView();
        var equipment = session.QueryCollection("equipment", new() { Offset = 300, Limit = 17 });
        Assert.Equal(17, equipment.Rows.Count);
        var quest = session.QueryCollection("evidence/31047C4A", new() { Offset = 300, Limit = 7 });
        var field = Assert.IsType<FieldNode>(quest.Rows[0].Cells["value"]);
        Assert.False(session.ApplyEdits([new IncomingEdit(field.ValueId, JsonSerializer.SerializeToElement(1))]).Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
