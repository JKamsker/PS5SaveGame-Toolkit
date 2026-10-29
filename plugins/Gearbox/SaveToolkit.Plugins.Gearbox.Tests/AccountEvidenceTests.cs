using Google.Protobuf;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class AccountEvidenceTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EverySampleAccountView_AgreesWithIndependentGameplayOnlyWireProbe(string title)
    {
        var plugin = new GearboxPlugin();
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName is "1" or "profile").ToArray();
        Assert.Equal(12, files.Length);
        foreach (var file in files)
        {
            var profile = file.FileName == "profile";
            var bytes = file.ReadAllBytes();
            using var document = plugin.Open(file);
            var tab = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(t => t.Title == "Account / character mirrors");
            var fields = tab.Children.SelectMany(n => GearboxGroupTests.Walk(document, n)).OfType<FieldNode>().ToDictionary(f => f.ValueId);
            Assert.All(fields.Values, f => { Assert.True(f.Spec.ReadOnly); Assert.Equal(f.Value, document.GetValue(f.ValueId)); });
            if (title == "PPSA01463")
            {
                if (profile) Guardian(IndependentSaveProbe.SelectMessage(bytes, true, 146), fields, "account/profile", true, false);
                else
                {
                    Guardian(IndependentSaveProbe.SelectMessage(bytes, false, 33), fields, "account/legacy", false, true);
                    Guardian(IndependentSaveProbe.SelectMessage(bytes, false, 57), fields, "account/character", false, false);
                }
            }
            else
            {
                var myth = profile ? IndependentSaveProbe.SelectMessage(bytes, true, 208) : IndependentSaveProbe.SelectMessage(bytes, false, 64, 15);
                var id = profile ? "account/myth" : "account/myth-mirror";
                Scalar(myth, profile ? 1 : 2, fields, id + "/prestige_experience");
                if (profile) Scalar(myth, 2, fields, id + "/last_upgrade_tree_index");
                var allocations = myth.Where(r => r.Tag == 3).SelectMany(r => IndependentSaveProbe.Scalars(r.Data)).Select(v => unchecked((int)v)).ToArray();
                Assert.Equal(allocations.Length, fields.Keys.Count(k => k.StartsWith(id + "/", StringComparison.Ordinal) && k.EndsWith("/points", StringComparison.Ordinal)));
                for (var i = 0; i < allocations.Length; i++) Assert.Equal(allocations[i].ToString(System.Globalization.CultureInfo.InvariantCulture), fields[$"{id}/{i}/points"].Value.AsString());
                if (!profile)
                {
                    var hero = IndependentSaveProbe.SelectMessage(bytes, false, 50);
                    string[] names = ["strength", "dexterity", "intelligence", "wisdom", "constitution", "luck"];
                    for (var i = 0; i < names.Length; i++) Scalar(hero, i + 1, fields, "account/hero/" + names[i]);
                }
            }
            Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
        }
    }

    private static void Guardian(IReadOnlyList<WireRecord> records, Dictionary<string, FieldNode> fields, string id, bool profile, bool legacy)
    {
        Scalar(records, legacy ? 1 : profile ? 3 : 2, fields, id + "/guardian_rank");
        Scalar(records, legacy ? 2 : profile ? 4 : 3, fields, id + "/guardian_experience");
        if (legacy) return;
        Scalar(records, profile ? 6 : 7, fields, id + "/new_guardian_experience");
        Scalar(records, 1, fields, id + (profile ? "/available_tokens" : "/guardian_available_tokens"));
        Scalar(records, profile ? 5 : 6, fields, id + "/guardian_reward_random_seed");
        if (!profile) Scalar(records, 8, fields, id + "/is_rank_system_enabled");
        var allocations = records.Where(r => r.Tag == (profile ? 2 : 4)).ToArray();
        for (var i = 0; i < allocations.Length; i++)
        {
            var reward = IndependentSaveProbe.Records(allocations[i].Data).ToArray();
            Scalar(reward, 1, fields, $"{id}/rank_rewards/{i}/num_tokens");
            if (!profile) Scalar(reward, 2, fields, $"{id}/rank_rewards/{i}/is_enabled");
        }
    }

    private static void Scalar(IReadOnlyList<WireRecord> records, int tag, Dictionary<string, FieldNode> fields, string id)
    {
        var selected = records.Where(r => r.Tag == tag).ToArray();
        if (selected.Length == 0) { Assert.False(fields.ContainsKey(id), "Absent gameplay records must not be fabricated."); return; }
        Assert.Equal(unchecked((long)IndependentSaveProbe.Scalar(Assert.Single(selected).Data)), fields[id].Value.AsInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MythPackedAndUnpackedValues_UseOneQueryableTableAndPreserveUnknownIndices(bool packed)
    {
        var values = Enumerable.Range(0, 205).Select(i => i == 204 ? -1 : i).ToArray();
        using var payload = new MemoryStream();
        using (var writer = new CodedOutputStream(payload, leaveOpen: true))
        {
            if (packed)
            {
                using var packedBytes = new MemoryStream();
                using (var inner = new CodedOutputStream(packedBytes, leaveOpen: true))
                    foreach (var value in values) inner.WriteInt32(value);
                writer.WriteTag(3, WireFormat.WireType.LengthDelimited);
                writer.WriteBytes(ByteString.CopyFrom(packedBytes.ToArray()));
            }
            else foreach (var value in values) { writer.WriteTag(3, WireFormat.WireType.Varint); writer.WriteInt32(value); }
        }
        var prestige = ProtoMessage.Parse(payload.ToArray(), GearboxSchema.Tina, "PlayerPrestigeProfileData");
        Assert.Equal(values, GearboxAccountViews.Integers(prestige, "points_spent_by_index_order"));
        var root = new ProtoMessage(GearboxSchema.Tina, "Profile", [new(208, 2, GearboxSchema.Tina.Spec("Profile", "player_prestige"), [], payload.ToArray(), prestige)]);
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(true, true), "PPSA01493", true);
        using var document = new GearboxDocument("profile", "PPSA01493", container, root, IReferenceContext.Empty);
        var lazy = new GearboxLazyViews();
        var view = new GearboxAccountViews(document, lazy).Build();
        var section = Assert.Single(view.OfType<SectionNode>());
        var first = lazy.Expand(Assert.Single(section.Children.OfType<SectionNode>()).LazyId!);
        var table = Assert.Single(first.OfType<QueryableTableNode>());
        Assert.Empty(first.OfType<SectionNode>());
        Assert.Equal(205, table.TotalCount);
        var page = document.Query(table.CollectionId, new() { Offset = 200, Limit = 100, SortColumn = "index" });
        Assert.Equal(205, page.FilteredCount);
        Assert.Equal(0, page.HiddenEmptyCount);
        var last = page.Rows;
        Assert.Equal(5, last.Count);
        Assert.All(last, row => Assert.False(row.IsEmpty));
        Assert.Equal("Unknown index", Assert.IsType<FieldNode>(last[^1].Cells["name"]).Value.AsString());
        Assert.Equal("-1", Assert.IsType<FieldNode>(last[^1].Cells["points"]).Value.AsString());
        var zero = document.Query(table.CollectionId, new() { Filters = new Dictionary<string, string> { ["points"] = "0" } });
        Assert.Equal("account/myth/0", Assert.Single(zero.Rows).Key);
        Assert.Equal(payload.ToArray(), prestige.Encode());
    }

    [Fact]
    public void MixedPackedAndUnpackedMythOccurrences_PreserveOrder()
    {
        byte[] bytes = [0x18, 3, 0x1A, 3, 0, 127, 1, 0x18, 2];
        var message = ProtoMessage.Parse(bytes, GearboxSchema.Tina, "PlayerPrestigeProfileData");
        Assert.Equal([3, 0, 127, 1, 2], GearboxAccountViews.Integers(message, "points_spent_by_index_order"));
        Assert.Equal(bytes, message.Encode());
    }
}
