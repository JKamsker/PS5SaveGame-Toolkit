using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsSessionTests
{
    [Fact]
    public void HostRegistersQueryableReferenceCellsAndResolvesTheirAttributedCandidates()
    {
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file =>
            file.Directory.Name == "HL0010" && file.FileName == "ue4savegame.ps5.sav");
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01603");
        using var session = new DocumentSession("hogwarts", sample, plugin, plugin.Open(sample, references), references: references);
        _ = session.BuildView();
        FieldNode? reference = null;
        foreach (var collection in session.QueryableCollections)
        {
            var query = new QueryableCollectionQuery { Limit = QueryableCollectionLimits.MaxPageSize };
            var first = session.QueryCollection(collection.Id, query);
            var second = session.QueryCollection(collection.Id, query);
            Assert.Equal(first.Rows.Select(row => row.Key), second.Rows.Select(row => row.Key));
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(first).Length < 2 * 1024 * 1024);
            reference ??= first.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>().FirstOrDefault();
        }
        Assert.NotNull(reference);
        var candidates = session.SearchReferences(reference.ValueId, new() { Text = reference.Value.AsString(), Limit = 5 });
        Assert.Contains(candidates.Items, candidate => candidate.RawId == reference.Value.AsString() && candidate.Dataset?.Attribution.License == "MIT");
        Assert.False(session.CanEdit);
    }
}
