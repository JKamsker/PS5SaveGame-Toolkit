using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Witcher3.Tests;

internal static class EnabledWriteFixture
{
    public static IReadOnlyList<SaveToolkit.Core.Storage.DiskSaveFile> Files() => SelectSaves(SampleSaves.Load("PPSA04018").AllDiskFiles);

    public static T[] SelectSaves<T>(IEnumerable<T> files) where T : ISaveFile
    {
        var saves = files.Where(f => f.FileName.EndsWith(".sav", StringComparison.Ordinal)).ToArray();
        // Load skips missing archives. An available archive without gameplay saves is a broken fixture.
        Assert.NotEmpty(saves);
        return saves;
    }

    public static (WitcherContainer Container, Sav3Directory Directory, Sav3Tree Tree, PlayerData Player, ObservedFeatures Features) Read(byte[] bytes)
    {
        var (container, directory, tree, player) = PlayerSaveFixture.Read(bytes);
        return (container, directory, tree, player, new ObservedFeatures(player, tree, container.Payload, directory.Names));
    }

    public static void Bind(DocumentSession session, ObservedFeatures features)
    {
        _ = session.BuildView();
        _ = session.QueryCollection(InventoryViews.CollectionId, new QueryableCollectionQuery { Limit = 200 });
        foreach (var array in features.Abilities)
        {
            for (var start = 0; start < array.Entries.Count; start += 64) { _ = session.Expand($"ability/{array.Name}/{start}"); }
        }
        _ = session.QueryCollection(GwentCollection.CollectionId, new() { Limit = 200 });
    }

    public static IncomingEdit Incoming(string id, FieldValue value) => new(id, JsonSerializer.SerializeToElement(value));

    public static void SemanticChanges(byte[] before, byte[] after, IReadOnlyList<string> expectedPaths)
    {
        var decoder = new WitcherSemanticDecoder();
        var limits = new DecodeLimits();
        var oldTree = decoder.Decode(MemorySaveSet.SingleFile("Manual.sav", before, "PPSA04018"), limits);
        var newTree = decoder.Decode(MemorySaveSet.SingleFile("Manual.sav", after, "PPSA04018"), limits);
        AssertOnlyPositionalIdentityLimit(oldTree);
        AssertOnlyPositionalIdentityLimit(newTree);
        var diff = SemanticDiffer.Compare(oldTree, newTree, maxChanges: 10000);
        Assert.False(diff.Truncated);
        Assert.True(diff.InputTruncated);
        Assert.All(diff.Warnings, w => Assert.Contains("positional identity", w, StringComparison.Ordinal));
        Assert.Equal(expectedPaths.Order(), diff.Changes.Select(c => c.Path).Order());
    }

    public static void AssertOnlyPositionalIdentityLimit(SemanticTree tree)
    {
        // These fixtures include identity-less ability slots. They are emitted for evidence, but
        // cannot prove record alignment; exact independent byte audits still protect every write.
        Assert.True(tree.Truncated);
        Assert.NotEmpty(tree.Warnings);
        Assert.All(tree.Warnings, warning => Assert.Contains("positional identity", warning, StringComparison.Ordinal));
    }

    public static void SkillPreservation(byte[] beforeBytes, byte[] afterBytes, SkillRank rank)
    {
        var before = Read(beforeBytes);
        var after = Read(afterBytes);
        Assert.Equal(before.Directory.Names, after.Directory.Names);
        Assert.Equal(before.Directory.Entries.Count, after.Directory.Entries.Count);
        var original = Manager(before.Player.AbilityManager!, before.Container.Payload, before.Directory.Names);
        var edited = Manager(after.Player.AbilityManager!, after.Container.Payload, after.Directory.Names);
        Assert.Equal(original.Write(new Dictionary<string, byte[]>()), edited.Write(new Dictionary<string, byte[]> {
            ["skills"] = original.Find("skills")!.Bytes,
        })); // Every byte outside the skills array inside this opaque manager is preserved.
        var oldSkills = before.Features.Abilities.Single(a => a.Name == "skills");
        var newSkills = after.Features.Abilities.Single(a => a.Name == "skills");
        foreach (var (oldSkill, newSkill) in oldSkills.Entries.Zip(newSkills.Entries))
        {
            Assert.Equal(oldSkill.Id, newSkill.Id);
            var oldProperties = oldSkill.Record.Properties.Where(p => oldSkill.Id != rank.Entry.Id || p.Name != "level").ToArray();
            var newProperties = newSkill.Record.Properties.Where(p => oldSkill.Id != rank.Entry.Id || p.Name != "level").ToArray();
            Assert.Equal(oldProperties.SelectMany(p => p.Bytes), newProperties.SelectMany(p => p.Bytes));
        }

        // Compare the entire expanded image after aligning the one inserted property. Only indexed relocation metadata is excluded.
        var oldData = before.Container.Payload;
        var newData = after.Container.Payload;
        var property = original.Find("skills")!;
        var growth = newData.Length - oldData.Length;
        Assert.Equal(rank.Entry.Record.Find("level") is null ? 12 : 0, growth);
        var excluded = new bool[oldData.Length];
        void Exclude(int offset, int count) => Array.Fill(excluded, true, offset, count);
        Exclude(property.Offset, property.Bytes.Length);
        var table = before.Directory.VariableTableOffset;
        Exclude(table + 4, before.Directory.Entries.Count * 8);
        Exclude(table - 10, 8);
        Exclude(oldData.Length - 6, 4);
        var rb = BinaryData.PayloadOffset(oldData, table - 6, before.Container.HeaderSize);
        for (var i = 0; i < BinaryData.Int32(oldData, rb + 2); i++) { Exclude(rb + 8 + i * 6, 4); }
        foreach (var node in before.Tree.Nodes.Where(n => n.Offset < property.Offset && n.Offset + n.Size >= property.Offset + property.Bytes.Length))
        {
            var lengthOffset = node.Tag switch { "SS" => 2, "BLCK" => 6, "PORP" or "AVAL" => 8, _ => -1 };
            if (lengthOffset >= 0) { Exclude(node.Offset + lengthOffset, 4); }
        }
        for (var i = 0; i < oldData.Length; i++)
        {
            if (!excluded[i]) { Assert.Equal(oldData[i], newData[i >= property.Offset + property.Bytes.Length ? i + growth : i]); }
        }
    }

    private static TaggedRecord Manager(Sav3Node node, byte[] data, IReadOnlyList<string> names)
    {
        var position = node.ValueOffset + 8;
        return TaggedRecord.Read(data, ref position, node.ValueOffset + node.ValueSize, names);
    }

    public static byte[] WithoutSkillLevel(byte[] bytes, string skillId)
    {
        var before = Read(bytes);
        var manager = Manager(before.Player.AbilityManager!, before.Container.Payload, before.Directory.Names);
        var property = manager.Find("skills")!;
        var skills = before.Features.Abilities.Single(a => a.Name == "skills");
        using var values = new MemoryStream();
        values.Write(BinaryData.EncodeInt32(skills.Entries.Count));
        foreach (var entry in skills.Entries)
        {
            var record = entry.Id == skillId ? new TaggedRecord(entry.Record.Properties.Where(p => p.Name != "level").ToArray()) : entry.Record;
            values.Write(record.Write(new Dictionary<string, byte[]>()));
        }
        var replacement = property.Bytes[..8].Concat(values.ToArray()).ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(replacement.AsSpan(4), replacement.Length - 4);
        var payload = Sav3Writer.Write(before.Container, before.Directory, before.Tree,
            [new Sav3Patch(property.Offset, property.Bytes.Length, replacement)]);
        return WitcherWriter.Write(before.Container, payload);
    }
}
