using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class RelationshipTests
{
    [Fact]
    public void RebirthCharactersAndOwnedRelationshipsUseStableRows()
    {
        var schema = Ff7Schema.ForTitle("PPSA08664")!;
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(f => f.FileName == "ff7rebirth000");
        using var document = new FinalFantasy7Plugin().Open(file);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var characters = Assert.IsType<TableNode>(view.Tabs.Single(t => t.Title == "Characters").Children.Single());
        Assert.Equal("Cloud Strife", Assert.IsType<TextNode>(characters.Rows[0].Cells["name"]).Text);
        Assert.Equal(41, Assert.IsType<FieldNode>(characters.Rows[0].Cells["level"]).Value.AsInt32());
        Assert.Equal(148409, Assert.IsType<FieldNode>(characters.Rows[0].Cells["experience"]).Value.AsInt32());
        Assert.Equal(300001, Assert.IsType<FieldNode>(characters.Rows[0].Cells["weapon"]).Value.AsInt32());
        Assert.All(characters.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>(), field =>
        {
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Equal(field.ValueId.Contains("/max-", StringComparison.Ordinal), field.Spec.ReadOnly);
        });

        var weaponPage = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("owned/weapon/0")));
        Assert.Contains(weaponPage.Rows, row => Assert.IsType<TextNode>(row.Cells["name"]).Text == "Buster Sword");
        var materiaPage = Assert.IsType<TableNode>(Assert.Single(document.ExpandNode("owned/materia/0")));
        Assert.Contains(materiaPage.Rows, row => Assert.IsType<TextNode>(row.Cells["name"]).Text == ItemNames.Lookup(schema, true, 10020));
        Assert.All(weaponPage.Rows.Concat(materiaPage.Rows).SelectMany(row => row.Cells.Values), cell =>
            Assert.False(cell is FieldNode));
    }

    [Theory]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void RebirthCharacterWeaponReferencesResolveToMeasuredDetails(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f =>
            schema.TryGetRole(f.FileName, out var common) && !common && !f.Directory.IsBackup))
        {
            var bytes = file.ReadAllBytes();
            var details = Enumerable.Range(0, 128)
                .Select(i => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0xB3A68 + i * 0x60)))
                .ToHashSet();
            for (var character = 0; character < Ff7Schema.CharacterCount; character++)
            {
                var reference = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0xCC + character * 0x100));
                Assert.Contains(reference, details);
            }
        }
    }

    [Fact]
    public void F1DiffEvidence_DemoGenerationChangesBodiesOnly()
    {
        var files = SampleSaves.Load("PPSA18936").AllDiskFiles;
        var before = files.First(f => f.FileName == "ff7rebirth000").ReadAllBytes();
        var after = files.First(f => f.FileName == "ff7rebirth001").ReadAllBytes();
        var diff = ByteDiffer.Compare(before, after, options: new ByteDiffOptions { MergeGap = 0, MaxRanges = 200 });
        Assert.Equal(800, diff.ChangedBytes);

        var records = LinkedRecords.Parse(before, Ff7Schema.ForTitle("PPSA18936")!, common: false);
        var metadata = records.SelectMany(record => Enumerable.Range(record.Offset, record.IsTail ? 16 : 40)).ToArray();
        Assert.DoesNotContain(diff.Ranges, range => metadata.Any(offset => offset >= range.Offset && offset < range.End));
    }
}
