using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ListTableTests
{
    private const int RecordCount = 205;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LargeSduList_UsesOneTableAndRetainsUnknownZeroRecords(bool wonderlands, bool profile)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(ContainerTests.Create(wonderlands, profile), title, profile);
        var root = container.Decode();
        var list = profile ? "profile_sdu_list" : "sdu_list";
        for (var i = 0; i < RecordCount; i++)
        {
            var sdu = new ProtoMessage(root.Schema, root.Schema.Spec(root.Type, list).Type, []);
            sdu.SetText("sdu_data_path", "Synthetic/unknown-sdu/" + i);
            sdu.SetInteger("sdu_level", 0);
            Add(root, list, sdu);
        }
        var bytes = container.Encode(root);
        using var document = new GearboxDocument(profile ? "profile" : "1", title,
            GearboxContainer.Parse(bytes, title, profile), root, IReferenceContext.Empty);
        _ = document.BuildView();
        var table = Assert.IsType<QueryableTableNode>(Assert.Single(document.ExpandNode("sdus")));
        Assert.Equal(RecordCount, table.TotalCount);
        var page = document.Query(table.CollectionId, new() { Offset = 200, Limit = 10 });
        Assert.Equal(5, page.Rows.Count);
        Assert.Equal("200", page.Rows[0].Key);
        Assert.Equal(RecordCount, page.FilteredCount);
        Assert.Equal(0, page.HiddenEmptyCount);
        Assert.All(page.Rows, row => Assert.False(row.IsEmpty));
        var field = Assert.IsType<FieldNode>(page.Rows[^1].Cells["level"]);
        Assert.Equal("sdu/204", field.ValueId);
        Assert.True(field.Spec.ReadOnly);
        Assert.Equal(0, field.Value.AsInt64());
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false, "rank_rewards", "reward_data_path", "rewards")]
    [InlineData(false, "rank_perks", "perk_data_path", "perks")]
    [InlineData(true, "rank_rewards", "reward_data_path", "rewards")]
    public void LargeGuardianList_UsesOneQueryableTableAndRetainsZeroAllocations(bool profile, string list, string asset, string lazySuffix)
    {
        const string title = "PPSA01463";
        var container = GearboxContainer.Parse(ContainerTests.Create(false, profile), title, profile);
        var root = container.Decode();
        var parentName = profile ? "guardian_rank" : "guardian_rank_character_data";
        var parent = new ProtoMessage(root.Schema, root.Schema.Spec(root.Type, parentName).Type, []);
        for (var i = 0; i < RecordCount; i++)
        {
            var entry = new ProtoMessage(root.Schema, root.Schema.Spec(parent.Type, list).Type, []);
            entry.SetText(asset, "Synthetic/unknown-reward/" + i);
            if (list == "rank_rewards") entry.SetInteger("num_tokens", i % 2);
            Add(parent, list, entry);
        }
        Add(root, parentName, parent);
        var bytes = container.Encode(root);
        using var document = new GearboxDocument(profile ? "profile" : "1", title,
            GearboxContainer.Parse(bytes, title, profile), root, IReferenceContext.Empty);
        _ = document.BuildView();
        var id = profile ? "account/profile" : "account/character";
        var table = Assert.IsType<QueryableTableNode>(Assert.Single(document.ExpandNode(id + "/" + lazySuffix)));
        Assert.Equal(RecordCount, table.TotalCount);
        var page = document.Query(table.CollectionId, new() { Offset = 200, Limit = 10 });
        Assert.Equal(5, page.Rows.Count);
        Assert.Equal(id + "/" + list + "/200", page.Rows[0].Key);
        Assert.All(page.Rows, row => Assert.False(row.IsEmpty));
        var target = document.Query(table.CollectionId, new() { Text = "Synthetic/unknown-reward/204" });
        Assert.Equal(id + "/" + list + "/204", Assert.Single(target.Rows).Key);
        if (list == "rank_rewards")
        {
            var zeros = document.Query(table.CollectionId, new() { Filters = new Dictionary<string, string> { ["num_tokens"] = "0" } });
            Assert.Equal(103, zeros.FilteredCount);
            Assert.Equal(0, zeros.HiddenEmptyCount);
        }
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    private static void Add(ProtoMessage parent, string list, ProtoMessage child) =>
        parent.Add(new(parent.Schema.Tag(parent.Type, list), 2, parent.Schema.Spec(parent.Type, list), [], Array.Empty<byte>(), child));
}
