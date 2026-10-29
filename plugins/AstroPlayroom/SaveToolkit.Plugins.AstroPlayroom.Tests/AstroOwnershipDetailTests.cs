using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroOwnershipDetailTests
{
    private const string CollectionId = "astro.collectibles";
    private const string OwnershipSuffix = "/IsGot[0]/text[0]";
    private const string OwnershipLabel = "Saved IsGot / ownership flag";
    private const string CollectAction = "collectibles/collect-all";

    [Theory]
    [InlineData("Artifact", false)]
    [InlineData("Artifact", true)]
    [InlineData("Puzzle", false)]
    [InlineData("Puzzle", true)]
    [InlineData("Prop", false)]
    [InlineData("Prop", true)]
    public void DetailUsesExistingBindingAndEditsOnlyChosenDuplicateOccurrence(string group, bool initiallyOwned)
    {
        var text = DuplicateText(group, initiallyOwned);
        using var document = AstroTestData.Open(AstroTestData.Bytes(text));
        var rows = EagerRows(document, group);
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0].Key, rows[1].Key);
        var detail = Ownership(rows[0]);
        Assert.Equal(rows[0].Key + OwnershipSuffix, detail.ValueId);
        Assert.Equal(FieldValue.Of(initiallyOwned), detail.Value);
        Assert.Equal(Confidence.Inferred, detail.Confidence);
        Assert.False(Assert.IsType<BoolSpec>(detail.Spec).ReadOnly);
        Assert.Equal(AstroTestData.Bytes(text), AstroTestData.Output(document));
        var raw = Assert.Single(document.ExpandNode(rows[0].Key + "/IsGot[0]").OfType<FieldNode>());
        Assert.Equal(raw with { Label = OwnershipLabel }, detail);
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query(CollectionId, new() { Limit = 2 });
        Assert.Equal(detail, Ownership(page.Rows.Single(row => row.Key == rows[0].Key)));
        Assert.Contains("not validated on console", detail.Warning, StringComparison.Ordinal);
        Assert.Contains("game may reject", detail.Warning, StringComparison.Ordinal);

        var result = document.ApplyEdits([new(detail.ValueId, FieldValue.Of(!initiallyOwned))]);
        Assert.True(result.Succeeded);
        Assert.Equal(new ValueChange(detail.ValueId, detail.Value, FieldValue.Of(!initiallyOwned)), Assert.Single(result.Changes));
        Assert.Contains(detail.Warning!, result.Warnings);
        Assert.True(result.StructureChanged);
        var expected = DuplicateText(group, initiallyOwned, !initiallyOwned);
        Assert.Equal(AstroTestData.Bytes(expected), AstroTestData.Output(document));
        using var reopened = AstroTestData.Open(AstroTestData.Output(document));
        Assert.Equal(FieldValue.Of(!initiallyOwned), Ownership(EagerRows(reopened, group)[0]).Value);
        Assert.False(Ownership(EagerRows(reopened, group)[1]).Value.AsBool());
        Assert.True(document.ApplyEdits([new(detail.ValueId, detail.Value)]).Succeeded);
        Assert.Equal(AstroTestData.Bytes(text), AstroTestData.Output(document));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<IsGot>true</IsGot><IsGot>false</IsGot>")]
    [InlineData("<IsGot>1</IsGot>")]
    [InlineData("<IsGot>unknown</IsGot>")]
    [InlineData("<IsGot value='true'/>")]
    [InlineData("<IsGot marker='kept'>true</IsGot>")]
    [InlineData("<IsGot><Nested>true</Nested></IsGot>")]
    [InlineData("<IsCinematicsPlayed>true</IsCinematicsPlayed>")]
    public void UncertainOwnershipHasNoDetailControlInEagerOrPagedRows(string flags)
    {
        var bytes = AstroTestData.Bytes($"\r\n<PreInstall><Artifact><unknown>{flags}</unknown></Artifact></PreInstall>");
        using var document = AstroTestData.Open(bytes);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var eager = Assert.Single(EagerRows(document, "Artifact"));
        var paged = Assert.Single(queryable.Query(CollectionId, new() { Limit = 1 }).Rows);
        Assert.Equal(eager.Key, paged.Key);
        foreach (var row in new[] { eager, paged })
        {
            Assert.Equal("Unknown", Assert.IsType<TextNode>(row.Cells["status"]).Text);
            var details = Assert.IsType<SectionNode>(row.Cells["details"]);
            Assert.Empty(details.Children.OfType<FieldNode>());
            Assert.Contains(details.Children.OfType<TextNode>(), node => node.Text.Contains("Saved IsGot: Unknown", StringComparison.Ordinal));
        }
        Assert.Equal(bytes, AstroTestData.Output(document));
    }

    [Fact]
    public void SampleDetailsRetainCatalogueAttributionAndMatchPagedOwnershipWithoutEdits()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        foreach (var file in AstroTestData.Samples())
        {
            using var document = plugin.Open(file, context);
            var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
            foreach (var group in new[] { "Artifact", "Puzzle", "Prop" })
            {
                var row = EagerRows(document, group)[0];
                var field = Ownership(row);
                var details = Assert.IsType<SectionNode>(row.Cells["details"]);
                var name = Assert.IsType<FieldNode>(row.Cells["name"]);
                Assert.Contains("CC-BY-SA-3.0", name.Description, StringComparison.Ordinal);
                Assert.Contains(details.Children.OfType<TextNode>(), node => node.Text.Contains("CC-BY-SA-3.0", StringComparison.Ordinal));
                var paged = queryable.Query(CollectionId + "." + group, new() { Limit = 200 }).Rows.Single(item => item.Key == row.Key);
                Assert.Equal(field, Ownership(paged));
                Assert.Equal(Confidence.Inferred, field.Confidence);
                Assert.Equal(field.Value, document.GetValue(field.ValueId));
            }
            Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionDiscoversDetailsWithoutRawAndPreservesPreviewReviewAndHistory(bool paged)
    {
        var count = paged ? 101 : 2;
        var text = "\r\n<PreInstall><Artifact>" + string.Concat(Enumerable.Range(0, count - 2).Select(index =>
            MissingRecord("item" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)))) +
            string.Concat(Enumerable.Repeat(MissingRecord("same"), 2)) +
            "</Artifact><Unknown note='中'>keep</Unknown></PreInstall>opaque-残存<broken";
        var file = ImportAuthoredAllocation(AstroTestData.Bytes(text));
        var plugin = new AstroPlayroomPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("astro-detail", file, plugin, document);
        _ = session.BuildView();
        var row = DetailRow(session, document, paged, count);
        var field = Ownership(row);
        Assert.Equal("/PreInstall[0]/Artifact[0]/same[1]" + OwnershipSuffix, field.ValueId);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
        var preview = session.PlanAction(CollectAction, new Dictionary<string, JsonElement>(),
            new ActionScope([row.Key]), session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(field.ValueId, Assert.Single(preview.Plan.Changes).ValueId);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
        Assert.False(session.State.CanUndo);
        var result = session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(true))],
            session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Ok);
        Assert.True(result.StructureChanged);
        Assert.Equal(new ValueChange(field.ValueId, FieldValue.Of(false), FieldValue.Of(true)), Assert.Single(result.Changes));
        Assert.Contains(field.Warning!, result.Warnings);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        Assert.Equal("Owned", Assert.IsType<TextNode>(DetailRow(session, document, paged, count).Cells["status"]).Text);
        Assert.StartsWith($"1/{count} owned", AstroCatalogueTestData.Group(document, "Artifacts").Badge, StringComparison.Ordinal);
        var missing = session.QueryCollection(CollectionId, new()
        { Filters = new Dictionary<string, string> { ["status"] = "Missing" }, Limit = 1 }, session.State.Revision,
            TestContext.Current.CancellationToken);
        Assert.Equal(count - 1, missing.FilteredCount);
        var lastRecord = text.LastIndexOf("<IsGot>false</IsGot>", StringComparison.Ordinal);
        var expected = text[..lastRecord] + "<IsGot>true</IsGot>" + text[(lastRecord + "<IsGot>false</IsGot>".Length)..];
        Assert.Equal(AstroTestData.Bytes(expected), Assert.Single(session.Serialize(TestContext.Current.CancellationToken)).Content.ToArray());
        AssertRejectedEditIsAtomic(session, document, field);
        AssertReviewAndHistory(session, document, field, file.ReadAllBytes(), AstroTestData.Bytes(expected));
    }

    private static void AssertRejectedEditIsAtomic(DocumentSession session, ISaveDocument document, FieldNode field)
    {
        var revision = session.State.Revision;
        var bytes = AstroTestData.Output(document);
        var rejected = session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(1))], revision,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(rejected.Ok);
        Assert.Empty(rejected.Changes);
        Assert.Equal(revision, session.State.Revision);
        Assert.Equal(bytes, AstroTestData.Output(document));
    }

    private static void AssertReviewAndHistory(DocumentSession session, ISaveDocument document,
        FieldNode field, byte[] original, byte[] edited)
    {
        // Review expands Raw and F8 as well: duplicate projections must remain one semantic edit.
        var review = session.GetChanges();
        Assert.True(review.Complete, string.Join("; ", review.Warnings));
        var change = Assert.Single(review.Changes);
        Assert.Equal(field.ValueId, change.ValueId);
        Assert.Equal(FieldValue.Of(false), change.OriginalValue);
        Assert.Equal(FieldValue.Of(true), change.CurrentValue);
        Assert.True(change.CanRevert);
        var raw = Assert.Single(document.ExpandNode(field.ValueId[..^"/text[0]".Length]).OfType<FieldNode>());
        Assert.Equal(field.Spec, raw.Spec);
        Assert.Equal(field.Warning, raw.Warning);
        using var reopened = AstroTestData.Open(edited);
        Assert.True(reopened.GetValue(field.ValueId).AsBool());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.Empty(session.GetChanges().Changes);
        Assert.Equal(original, AstroTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, AstroTestData.Output(document));
        Assert.Single(session.GetChanges().Changes);
        Assert.True(session.RevertChange(field.ValueId, session.State.Revision).Ok);
        Assert.Equal(original, AstroTestData.Output(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(edited, AstroTestData.Output(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, AstroTestData.Output(document));
        var preview = session.PlanAction(CollectAction, new Dictionary<string, JsonElement>(),
            new ActionScope([field.ValueId[..^OwnershipSuffix.Length]]), session.State.Revision,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(field.ValueId, Assert.Single(preview.Plan.Changes).ValueId);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(edited, AstroTestData.Output(document));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, AstroTestData.Output(document));
    }

    private static TableRow DetailRow(DocumentSession session, ISaveDocument document, bool paged, int count)
    {
        if (!paged) return EagerRows(document, "Artifact")[^1];
        Assert.Single(AstroCatalogueTestData.Group(document, "Artifacts").Children.OfType<QueryableTableNode>());
        var query = new QueryableCollectionQuery { Offset = count - 1, Limit = 1 };
        var page = session.QueryCollection(CollectionId + ".Artifact", query, session.State.Revision);
        Assert.Equal(count, page.TotalCount);
        Assert.Equal(count, page.FilteredCount);
        var row = Assert.Single(page.Rows);
        Assert.InRange(Assert.IsType<SectionNode>(row.Cells["details"]).Children.Count, 1, 10);
        return row;
    }

    private static FieldNode Ownership(TableRow row) => Assert.Single(
        Assert.IsType<SectionNode>(row.Cells["details"]).Children.OfType<FieldNode>(), field => field.Label == OwnershipLabel);

    private static IReadOnlyList<TableRow> EagerRows(ISaveDocument document, string group) =>
        AstroCatalogueTestData.Table(document, group switch
        {
            "Artifact" => "Artifacts", "Puzzle" => "Puzzle pieces", "Prop" => "Props",
            _ => throw new ArgumentException("Unknown test group.", nameof(group))
        }).Rows;

    private static string DuplicateText(string group, bool initiallyOwned, bool? changed = null) =>
        $"\r\n<PreInstall><{group}><same marker='first'><IsGot> \t{((changed ?? initiallyOwned) ? "true" : "false")}\r\n </IsGot>" +
        "<IsCinematicsPlayed>true</IsCinematicsPlayed></same><same marker='second'><IsGot>false</IsGot>" +
        $"<IsCinematicsPlayed>false</IsCinematicsPlayed></same></{group}>" +
        "<Unknown a='中'><!--keep-->literal &amp; text</Unknown></PreInstall>opaque-残存<broken";

    private static string MissingRecord(string name) =>
        $"<{name}><IsGot>false</IsGot><IsCinematicsPlayed>false</IsCinematicsPlayed></{name}>";

    private static DiskSaveFile ImportAuthoredAllocation(byte[] bytes)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("PPSA01325/sdimg_sce_sdmemory/memory.dat").Open();
            entry.Write(bytes);
        }
        var set = SaveSetImporter.Import([new ImportSource("authored.zip", () => new MemoryStream(zip.ToArray()))],
            Path.Combine(TestScratch.ProcessFolder, nameof(AstroOwnershipDetailTests), Guid.NewGuid().ToString("N")));
        return Assert.Single(set.AllDiskFiles);
    }
}
