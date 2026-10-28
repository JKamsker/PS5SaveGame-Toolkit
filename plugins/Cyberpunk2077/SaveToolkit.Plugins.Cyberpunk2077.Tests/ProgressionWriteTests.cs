using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ProgressionWriteTests
{
    [Fact]
    public void ConflictingForcedLevelStatRefusesLevelWriteWithoutDisablingXp()
    {
        var original = CsavContainer.Parse(ProgressionSampleTests.Files()[0].ReadAllBytes());
        var tree = CsavTree.Parse(original);
        var stats = PlayerStatsReader.Read(original, tree);
        var levelHash = PlayerStatsReader.NameHash("Level");
        var offset = stats.TryGetValue(levelHash, out var levelOffset) ? levelOffset : stats[PlayerStatsReader.NameHash("Strength")];
        var payload = original.Payload.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset - 12), levelHash);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset), 2);
        var bytes = original.Rebuild(payload);
        using var document = ProgressionSampleTests.Open(bytes);
        var field = Assert.Single(CyberpunkSampleTests.ExpandAll(document), field => field.ValueId == "progression/Level/level");
        Assert.True(field.Spec.ReadOnly);
        Assert.Contains("disagrees", field.Warning);
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of(2L))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new("progression/Level/xp", FieldValue.Of(1L))]).Succeeded);
        using var reopened = ProgressionSampleTests.Open(Assert.Single(document.Serialize()).Content.ToArray());
        _ = reopened.BuildView();
        Assert.Equal(FieldValue.Of(1L), reopened.GetValue("progression/Level/xp"));
    }

    [Fact]
    public void ProficiencyGrowthCombinesWithPointAndAttributeEditsInOneUndoUnit()
    {
        var file = ProgressionSampleTests.Files()[0];
        var plugin = new Cyberpunk2077Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("mixed-progress", (DiskSaveFile)file, plugin, document);
        _ = session.BuildView();
        var values = new Dictionary<string, int>
        {
            ["progression/Level/level"] = 2, ["progression/Level/xp"] = 20, ["progression/Level/threshold"] = 100,
            ["progression/StreetCred/level"] = 2, ["progression/StreetCred/xp"] = 30, ["progression/StreetCred/threshold"] = 150,
            ["progression/points/Primary"] = 4, ["progression/attributes/Strength"] = 5,
        };
        Assert.True(session.ApplyEdits(values.Select(pair => new IncomingEdit(pair.Key, JsonSerializer.SerializeToElement(pair.Value.ToString()))).ToArray()).Ok);
        var bytes = Assert.Single(session.Serialize()).Content.ToArray();
        using var reopened = ProgressionSampleTests.Open(bytes);
        _ = CyberpunkSampleTests.ExpandAll(reopened);
        foreach (var (id, value) in values) Assert.Equal(FieldValue.Of(value), reopened.GetValue(id));
        Assert.Equal(FieldValue.Of(5.0), reopened.GetValue("stats/attributes/Strength"));
        var query = Assert.IsType<CyberpunkDocument>(document).Query("development", new() { Filters = new Dictionary<string, string> { ["name"] = "Level" } });
        Assert.Equal("2", Assert.IsType<SectionNode>(Assert.Single(query.Rows).Cells["rank"]).Title);
        Assert.Equal("20", Assert.IsType<SectionNode>(Assert.Single(query.Rows).Cells["xp"]).Title);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(bytes, Assert.Single(session.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("xp", "currentExp")]
    [InlineData("threshold", "expToLevel")]
    public void AlreadyMaterializedScalarsChangeOnlyTheirFourPayloadBytes(string suffix, string property)
    {
        using var first = new Cyberpunk2077Plugin().Open(ProgressionSampleTests.Files()[0]);
        _ = first.BuildView();
        var id = $"progression/Level/{suffix}";
        Assert.True(first.ApplyEdits([new(id, FieldValue.Of(100L))]).Succeeded);
        var bytes = Assert.Single(first.Serialize()).Content.ToArray();
        var before = CsavContainer.Parse(bytes);
        var tree = CsavTree.Parse(before);
        var data = ProgressionData.TryRead(before, tree, out _)!;
        var node = tree.Nodes.Single(node => node.Name == "ScriptableSystemsContainer");
        var field = data.Proficiencies.Single(p => p.Type == "Level").Record.Require(property, "Int32");
        var offset = node.Offset + 8 + field.Offset;
        using var document = ProgressionSampleTests.Open(bytes);
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new(id, FieldValue.Of(101L))]).Succeeded);
        var expected = before.Payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(offset), 101);
        var after = CsavContainer.Parse(Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(expected, after.Payload);
        Assert.All(ByteDiffer.Compare(before.Payload, after.Payload).Ranges, range => Assert.InRange(range.Offset, offset, offset + 3));
        Assert.True(document.ApplyEdits([new(id, FieldValue.Of(100L))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("Level", "level", "currentLevel", 2)]
    [InlineData("Level", "xp", "currentExp", 123)]
    [InlineData("Level", "threshold", "expToLevel", 456)]
    [InlineData("StreetCred", "level", "currentLevel", 2)]
    [InlineData("StreetCred", "xp", "currentExp", 123)]
    [InlineData("StreetCred", "threshold", "expToLevel", 456)]
    public void EachEnabledScalarPersistsPreservesUnrelatedDataAndSupportsUndoRedo(string type, string suffix, string property, int target)
    {
        foreach (var file in ProgressionSampleTests.Files())
        {
            var before = CsavContainer.Parse(file.ReadAllBytes());
            var beforeTree = CsavTree.Parse(before);
            var data = ProgressionData.TryRead(before, beforeTree, out _)!;
            var proficiency = data.Proficiencies.Single(p => p.Type == type);
            var plugin = new Cyberpunk2077Plugin();
            using var document = plugin.Open(file);
            using var session = new DocumentSession("progression-write", (DiskSaveFile)file, plugin, document);
            var id = $"progression/{type}/{suffix}";
            var field = Assert.Single(CyberpunkSampleTests.ExpandAll(document), field => field.ValueId == id);
            _ = session.BuildView();
            Assert.False(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Contains("Not validated on console", field.Description);
            Assert.Contains("game may reject", field.Warning);
            Assert.True(session.ApplyEdits([new(id, JsonSerializer.SerializeToElement(target.ToString()))]).Ok);
            var bytes = Assert.Single(session.Serialize()).Content.ToArray();
            var after = CsavContainer.Parse(bytes);
            var afterTree = CsavTree.Parse(after);
            var expectedOutside = before.Payload.ToArray();
            if (suffix == "level" && proficiency.MirrorOffset is { } offset)
                BinaryPrimitives.WriteSingleLittleEndian(expectedOutside.AsSpan(offset), target);
            var oldNode = beforeTree.Nodes.Single(node => node.Name == "ScriptableSystemsContainer");
            var newNode = afterTree.Nodes[oldNode.Id];
            Assert.Equal(expectedOutside.AsSpan(0, oldNode.Offset).ToArray(), after.Payload.AsSpan(0, newNode.Offset).ToArray());
            Assert.Equal(expectedOutside.AsSpan(oldNode.Offset + oldNode.Size).ToArray(), after.Payload.AsSpan(newNode.Offset + newNode.Size).ToArray());
            var oldPackage = new RedPackage(ProgressionData.PackageBytes(before.Payload, oldNode), true);
            var newPackage = new RedPackage(ProgressionData.PackageBytes(after.Payload, newNode), true);
            Assert.Equal(oldPackage.Names, newPackage.Names.Take(oldPackage.Names.Count));
            Assert.Equal(oldPackage.Types, newPackage.Types);
            foreach (var index in Enumerable.Range(0, oldPackage.Types.Count))
            {
                var oldChunk = oldPackage.Chunk(index);
                var newChunk = newPackage.Chunk(index);
                if (oldPackage.Types[index] != "PlayerDevelopmentData")
                {
                    Assert.Equal(oldChunk.Rebuild(new Dictionary<string, byte[]>()), newChunk.Rebuild(new Dictionary<string, byte[]>()));
                    continue;
                }
                foreach (var other in oldChunk.Fields.Where(field => field.Name != "proficiencies"))
                    Assert.Equal(oldChunk.Raw(other), newChunk.Raw(newChunk.Require(other.Name, other.Type)));
                var oldRecords = oldChunk.StructArray(oldChunk.Require("proficiencies", "array:SProficiency"));
                var newRecords = newChunk.StructArray(newChunk.Require("proficiencies", "array:SProficiency"));
                Assert.Equal(oldRecords.Count, newRecords.Count);
                for (var recordIndex = 0; recordIndex < oldRecords.Count; recordIndex++)
                {
                    var oldRecord = oldRecords[recordIndex];
                    var newRecord = newRecords[recordIndex];
                    var isTarget = oldRecord.Enum("type", "gamedataProficiencyType", "Assault") == type;
                    foreach (var other in oldRecord.Fields.Where(field => !isTarget || field.Name != property))
                        Assert.Equal(oldRecord.Raw(other), newRecord.Raw(newRecord.Require(other.Name, other.Type)));
                    if (isTarget) Assert.Equal(target, newRecord.Int(property));
                }
            }
            using var reopened = ProgressionSampleTests.Open(bytes);
            _ = CyberpunkSampleTests.ExpandAll(reopened);
            Assert.Equal(FieldValue.Of(target), reopened.GetValue(id));
            Assert.True(session.Undo().Ok);
            Assert.Equal(before.Original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(bytes, Assert.Single(session.Serialize()).Content.ToArray());
            Assert.True(session.Undo().Ok);
            Assert.Equal(0, ByteDiffer.Compare(before.Original, Assert.Single(document.Serialize()).Content.ToArray()).ChangedBytes);
        }
    }

    [Fact]
    public void EveryNewScalarRejectsOutOfRangeAndWrongTypeAtomically()
    {
        var file = ProgressionSampleTests.Files()[0];
        using var document = new Cyberpunk2077Plugin().Open(file);
        var fields = CyberpunkSampleTests.ExpandAll(document).Where(field => field.ValueId.StartsWith("progression/Level/", StringComparison.Ordinal) ||
            field.ValueId.StartsWith("progression/StreetCred/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, fields.Length);
        foreach (var field in fields)
        {
            var spec = Assert.IsType<IntegerSpec>(field.Spec);
            foreach (var value in new[] { FieldValue.Of(spec.Min - 1), FieldValue.Of(spec.Max + 1), FieldValue.Of("invalid") })
            {
                Assert.False(document.ApplyEdits([new("inventory/39/quantity", FieldValue.Of(99L)), new(field.ValueId, value)]).Succeeded);
                Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
            }
        }
    }
}
