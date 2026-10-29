using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroCatalogueSessionTests
{
    [Fact]
    public void CatalogueInspectionIsReadOnlyAndReferencePickerSupportsBrowseFilteringAndSorting()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var file = AstroTestData.Samples()[0];
        var document = plugin.Open(file, context);
        using var session = new DocumentSession("astro-inspect", Assert.IsType<DiskSaveFile>(file), plugin, document, references: context);
        _ = session.BuildView();
        var catalogue = AstroCatalogueTestData.Catalogue(document);
        var section = Assert.Single(catalogue.Children.OfType<SectionNode>(), node => node.Title == "All saved collectibles");
        var action = Assert.Single(section.Children.OfType<ActionNode>(), node => node.ActionId == "collectibles/inspect");
        Assert.False(action.Mutates);
        var parameter = Assert.Single(action.Parameters);
        var spec = Assert.IsType<ReferenceSpec>(parameter.Spec);
        Assert.True(spec.ReadOnly);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));

        var revision = session.State.Revision;
        var result = session.InvokeAction(action.ActionId, new Dictionary<string, System.Text.Json.JsonElement>(), revision);
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Contains("Inspection only", result.Message, StringComparison.Ordinal);
        Assert.Equal(revision, session.State.Revision);
        Assert.False(session.State.CanUndo);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));

        var page = session.SearchReferences(parameter.ValueId,
            new ReferenceQuery { Category = "Memory Meadow", SortBy = ReferenceSortKey.Name, Limit = 200 },
            revision: session.State.Revision, actionId: action.ActionId);
        Assert.Equal(25, page.Total);
        Assert.NotEmpty(page.Items);
        Assert.Equal(page.Items.OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.RawId, StringComparer.Ordinal), page.Items);
        Assert.All(page.Items, item => Assert.Equal("Memory Meadow", item.Category));
    }

    [Fact]
    public void RawEditUndoAndRedoRefreshCatalogueStatusCountsAndDetailsWithoutChangingCinematics()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        foreach (var file in AstroTestData.Samples().Where(f => !f.Directory.IsBackup))
        {
            var document = plugin.Open(file, context);
            using var session = new DocumentSession("astro-test", Assert.IsType<DiskSaveFile>(file), plugin, document, references: context);
            _ = session.BuildView();
            var labelId = Assert.IsType<FieldNode>(AstroCatalogueTestData.Row(document, "Artifacts", "Artifact/ps1").Cells["name"]).ValueId;
            foreach (var id in new[] { "/PreInstall[0]", "/PreInstall[0]/Artifact[0]", "/PreInstall[0]/Artifact[0]/ps1[0]" })
            {
                _ = session.Expand(id);
            }

            var flag = Assert.Single(session.Expand("/PreInstall[0]/Artifact[0]/ps1[0]/IsGot[0]").OfType<FieldNode>());
            Assert.False(flag.Value.AsBool());
            var original = file.ReadAllBytes();
            var beforeBadge = AstroCatalogueTestData.Group(document, "Artifacts").Badge;
            var result = session.ApplyEdits([new IncomingEdit(flag.ValueId, JsonSerializer.SerializeToElement(true))], session.State.Revision);
            Assert.True(result.Ok);
            Assert.True(result.StructureChanged);
            Assert.Single(result.Changes);
            AssertCatalogueStatus(document, labelId, "Owned", file.Directory.Generation == 0 ? 1 : 4);
            var edited = AstroTestData.Output(document);
            using (var reopened = plugin.Open(AstroTestData.File(edited), context))
            {
                Assert.Equal("Owned", Assert.IsType<TextNode>(AstroCatalogueTestData.Row(reopened, "Artifacts", "Artifact/ps1").Cells["status"]).Text);
            }

            var undo = session.Undo();
            Assert.True(undo.Ok);
            Assert.True(undo.StructureChanged);
            Assert.Equal(beforeBadge, AstroCatalogueTestData.Group(document, "Artifacts").Badge);
            AssertCatalogueStatus(document, labelId, "Missing", file.Directory.Generation == 0 ? 0 : 3);
            Assert.Equal(original, AstroTestData.Output(document));
            Assert.False(session.State.IsDirty);
            Assert.True(session.Redo().Ok);
            AssertCatalogueStatus(document, labelId, "Owned", file.Directory.Generation == 0 ? 1 : 4);
            Assert.Equal(edited, AstroTestData.Output(document));
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, AstroTestData.Output(document));
        }
    }

    private static void AssertCatalogueStatus(ISaveDocument document, string labelId, string status, int owned)
    {
        var row = AstroCatalogueTestData.Row(document, "Artifacts", "Artifact/ps1");
        Assert.Equal(labelId, Assert.IsType<FieldNode>(row.Cells["name"]).ValueId);
        Assert.Equal(status, Assert.IsType<TextNode>(row.Cells["status"]).Text);
        Assert.StartsWith($"{owned}/50 owned", AstroCatalogueTestData.Group(document, "Artifacts").Badge, StringComparison.Ordinal);
        var details = Assert.IsType<SectionNode>(row.Cells["details"]);
        Assert.Contains(details.Children.OfType<TextNode>(), t => t.Text.Contains("Saved IsCinematicsPlayed: false", StringComparison.Ordinal));
    }
}
