using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ReadOnlyCatalogActionTests
{
    [Theory]
    [InlineData(4)] // Unsupported save schema.
    [InlineData(8)] // Unsupported game version.
    [InlineData(21)] // Unsupported archive version.
    public void EveryCatalogRemainsInspectableWithoutChangingReadOnlySession(int versionOffset)
    {
        var file = ProgressionSampleTests.Files()[0];
        var bytes = file.ReadAllBytes();
        bytes[versionOffset] ^= 1;
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        var altered = new MemorySaveSet().With(file.Directory.TitleId!, file.Directory.Name,
            new Dictionary<string, byte[]> { [file.PathInDirectory] = bytes }, file.Directory.IsBackup).Directories[0].Files[0];
        Assert.Equal(file.RelativePath, altered.RelativePath);
        using var document = plugin.Open(altered, context);
        Assert.False(document.CanEdit);
        using var session = new DocumentSession("readonly-catalog", (DiskSaveFile)file, plugin, document, references: context);
        var view = Assert.IsType<TabsNode>(session.BuildView());
        var catalogs = view.Tabs.Single(tab => tab.Title == "Reference catalogs");
        var actions = catalogs.Children.OfType<SectionNode>().SelectMany(section => section.Children).OfType<ActionNode>().ToArray();
        Assert.Equal(5, actions.Length);
        var initialState = session.State;

        foreach (var action in actions)
        {
            Assert.False(action.Mutates);
            Assert.False(action.SupportsPlanning);
            var parameter = Assert.Single(action.Parameters);
            var candidates = session.SearchReferences(parameter.ValueId, new ReferenceQuery
                { SortBy = ReferenceSortKey.Category, Limit = 2 }, actionId: action.ActionId);
            Assert.NotEmpty(candidates.Items);
            Assert.Contains(ReferenceSortKey.Category, candidates.SortKeys);
            var selected = candidates.Items[0];
            var filtered = session.SearchReferences(parameter.ValueId, new ReferenceQuery
                { Category = selected.Category, SortBy = ReferenceSortKey.Key, Limit = 2 }, actionId: action.ActionId);
            Assert.NotEmpty(filtered.Items);
            Assert.All(filtered.Items, candidate => Assert.Equal(selected.Category, candidate.Category));
            var result = session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement>
                { [parameter.ValueId] = JsonSerializer.SerializeToElement(selected.RawId) }, initialState.Revision);
            Assert.True(result.Ok, string.Join("; ", result.Errors.Select(error => error.Message)));
            Assert.Contains(selected.RawId, result.Message);
            Assert.Empty(result.Changes);
            Assert.Equal(initialState, session.State);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.False(session.InvokeAction(action.ActionId, new Dictionary<string, JsonElement>
                { [parameter.ValueId] = JsonSerializer.SerializeToElement("unknown") }).Ok);
            Assert.Equal(initialState, session.State);
        }

        Assert.Empty(session.Serialize());
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.CanRedo);
    }
}
