using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ProgressionSampleTests
{
    [Fact]
    public void EverySnapshotHasKnownDevelopmentValuesAndMatchingStatHashes()
    {
        foreach (var file in Files())
        {
            var container = CsavContainer.Parse(file.ReadAllBytes());
            var tree = CsavTree.Parse(container);
            var progression = Assert.IsType<ProgressionData>(ProgressionData.TryRead(container, tree, out var warning));
            Assert.Null(warning);
            Assert.Null(progression.AttributeWarning);
            Assert.Equal(5, progression.Attributes.Count);
            Assert.All(progression.Attributes, a => { Assert.Equal(3, a.Value); Assert.NotNull(a.MirrorOffset); });
            Assert.Equal(new[] { ("Level", 1, 0, 0), ("StreetCred", 1, 0, 0) },
                progression.Proficiencies.Select(p => (p.Type, p.Level, p.Experience, p.Threshold)));
            Assert.Equal(new[] { ("Attribute", 7), ("Espionage", 0), ("Primary", 0), ("Secondary", 0) },
                progression.Points.Select(p => (p.Type, p.Value)));
            using var document = new Cyberpunk2077Plugin().Open(file);
            var fields = CyberpunkSampleTests.ExpandAll(document);
            var proficiencies = fields.Where(f => f.ValueId.StartsWith("progression/Level/", StringComparison.Ordinal) ||
                f.ValueId.StartsWith("progression/StreetCred/", StringComparison.Ordinal)).ToArray();
            Assert.Equal(6, proficiencies.Length);
            Assert.All(proficiencies, f => Assert.False(f.Spec.ReadOnly));
            // Facts live in an F8 collection: expanding ordinary views never loads these bindings.
            var facts = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("facts", new() { Limit = 200 });
            Assert.Equal(2750, facts.TotalCount);
            Assert.Equal(200, facts.Rows.Count);
            Assert.All(facts.Rows, row =>
            {
                var field = Assert.IsType<FieldNode>(row.Cells["value"]);
                Assert.StartsWith("facts/", field.ValueId);
                Assert.False(field.Spec.ReadOnly);
                Assert.Equal(Confidence.Unknown, field.Confidence);
                Assert.Contains("Not validated on console", field.Warning);
                Assert.Equal(field.Value, document.GetValue(field.ValueId));
            });
        }
    }

    [Theory]
    [InlineData("Strength")]
    [InlineData("Reflexes")]
    [InlineData("TechnicalAbility")]
    [InlineData("Intelligence")]
    [InlineData("Cool")]
    public void EachAttributePatchesExactlyItsTwoCopiesAndReportsDerivedChange(string name)
    {
        foreach (var file in Files())
        {
            var original = CsavContainer.Parse(file.ReadAllBytes());
            var data = ProgressionData.TryRead(original, CsavTree.Parse(original), out _)!;
            var attribute = Assert.Single(data.Attributes, a => a.Id == $"attributes/{name}");
            using var document = new Cyberpunk2077Plugin().Open(file);
            _ = CyberpunkSampleTests.ExpandAll(document);
            var outcome = document.ApplyEdits([new($"progression/{attribute.Id}", FieldValue.Of(20L))]);
            Assert.True(outcome.Succeeded);
            Assert.Equal(2, outcome.Changes.Count);
            var derived = Assert.Single(outcome.Changes, c => c.ValueId == $"stats/{attribute.Id}");
            Assert.Equal(FieldValue.Of(3.0), derived.OldValue);
            Assert.Equal(FieldValue.Of(20.0), derived.NewValue);
            var bytes = Assert.Single(document.Serialize()).Content.ToArray();
            var edited = CsavContainer.Parse(bytes);
            var expected = original.Payload.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(attribute.Offset), 20);
            BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(attribute.MirrorOffset!.Value), 20);
            Assert.Equal(expected, edited.Payload);
            using var reopened = Open(bytes);
            _ = CyberpunkSampleTests.ExpandAll(reopened);
            Assert.Equal(FieldValue.Of(20L), reopened.GetValue($"progression/{attribute.Id}"));
            Assert.Equal(FieldValue.Of(20.0), reopened.GetValue($"stats/{attribute.Id}"));
            Assert.True(document.ApplyEdits([new($"progression/{attribute.Id}", FieldValue.Of(3L))]).Succeeded);
            Assert.Equal(original.Original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData("Attribute", 0)]
    [InlineData("Primary", 12)]
    [InlineData("Espionage", 12)]
    public void EachPointBalanceRoundTripsWithOnlyNecessaryPackageGrowth(string type, int growth)
    {
        foreach (var file in Files())
        {
            var before = CsavContainer.Parse(file.ReadAllBytes());
            var beforeTree = CsavTree.Parse(before);
            var beforeData = ProgressionData.TryRead(before, beforeTree, out _)!;
            var point = Assert.Single(beforeData.Points, p => p.Type == type);
            using var document = new Cyberpunk2077Plugin().Open(file);
            _ = CyberpunkSampleTests.ExpandAll(document);
            var id = $"progression/points/{type}";
            Assert.True(document.ApplyEdits([new(id, FieldValue.Of(42L))]).Succeeded);
            var bytes = Assert.Single(document.Serialize()).Content.ToArray();
            var after = CsavContainer.Parse(bytes);
            var afterTree = CsavTree.Parse(after);
            Assert.Equal(before.Payload.Length + growth, after.Payload.Length);
            var oldNode = Assert.Single(beforeTree.Nodes, n => n.Name == "ScriptableSystemsContainer");
            var newNode = afterTree.Nodes[oldNode.Id];
            Assert.Equal(oldNode.Size + growth, newNode.Size);
            Assert.Equal(before.Payload.AsSpan(0, oldNode.Offset).ToArray(), after.Payload.AsSpan(0, newNode.Offset).ToArray());
            Assert.Equal(before.Payload.AsSpan(oldNode.Offset + oldNode.Size).ToArray(),
                after.Payload.AsSpan(newNode.Offset + newNode.Size).ToArray());
            foreach (var node in beforeTree.Nodes)
            {
                var next = afterTree.Nodes[node.Id];
                Assert.Equal(node.Name, next.Name);
                Assert.Equal(node.NextId, next.NextId);
                Assert.Equal(node.ChildId, next.ChildId);
                Assert.Equal(node.Offset + (node.Offset > oldNode.Offset ? growth : 0), next.Offset);
                Assert.Equal(node.Size + (node.Id == oldNode.Id ? growth : 0), next.Size);
            }

            var oldPackage = new RedPackage(ProgressionData.PackageBytes(before.Payload, oldNode), true);
            var newPackage = new RedPackage(ProgressionData.PackageBytes(after.Payload, newNode), true);
            Assert.Equal(oldPackage.Names, newPackage.Names);
            Assert.Equal(oldPackage.Types, newPackage.Types);
            for (var i = 0; i < oldPackage.Types.Count; i++)
            {
                if (oldPackage.Types[i] == "PlayerDevelopmentData")
                {
                    foreach (var field in oldPackage.Chunk(i).Fields.Where(f => f.Name != "devPoints"))
                    {
                        Assert.Equal(oldPackage.Chunk(i).Raw(field), newPackage.Chunk(i).Raw(newPackage.Chunk(i).Find(field.Name)!));
                    }
                }
                else
                {
                    Assert.Equal(oldPackage.Chunk(i).Rebuild(new Dictionary<string, byte[]>()),
                        newPackage.Chunk(i).Rebuild(new Dictionary<string, byte[]>()));
                }
            }

            using var reopened = Open(bytes);
            _ = CyberpunkSampleTests.ExpandAll(reopened);
            Assert.Equal(FieldValue.Of(42L), reopened.GetValue(id));
            Assert.True(document.ApplyEdits([new(id, FieldValue.Of(point.Original))]).Succeeded);
            Assert.Equal(before.Original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void MixedEditsAndUndoRedoThroughFrameworkPreserveOriginalBytes()
    {
        var file = Files()[0];
        var plugin = new Cyberpunk2077Plugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("test", (DiskSaveFile)file, plugin, document);
        _ = session.BuildView();
        var outcome = session.ApplyEdits([new("progression/attributes/Strength", JsonSerializer.SerializeToElement("15")),
            new("progression/points/Primary", JsonSerializer.SerializeToElement("12")),
            new("progression/points/Espionage", JsonSerializer.SerializeToElement("9"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(outcome.Ok);
        var bytes = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        using var reopened = Open(bytes);
        _ = CyberpunkSampleTests.ExpandAll(reopened);
        Assert.Equal(FieldValue.Of(15L), reopened.GetValue("progression/attributes/Strength"));
        Assert.Equal(FieldValue.Of(12L), reopened.GetValue("progression/points/Primary"));
        Assert.Equal(FieldValue.Of(9L), reopened.GetValue("progression/points/Espionage"));
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.Serialize(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(bytes, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
    }

    internal static ISaveFile[] Files()
    {
        var files = SampleSaves.Load("PPSA04027").AllDiskFiles.Where(f => f.FileName == "sav.dat").Cast<ISaveFile>().ToArray();
        Assert.NotEmpty(files); // Missing archives skip in Load; malformed/empty fixtures must not pass vacuously.
        return files;
    }
    internal static ISaveDocument Open(byte[] bytes) => new Cyberpunk2077Plugin().Open(MemorySaveSet.SingleFile("sav.dat", bytes, "PPSA04027"));
}
