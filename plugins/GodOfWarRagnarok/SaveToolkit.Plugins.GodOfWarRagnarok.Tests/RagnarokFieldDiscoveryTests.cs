using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokFieldDiscoveryTests
{
    private static readonly HashSet<ulong> XpIds = [0x9C7CF61C839B9A8A, 0xAC98E8B5155F2C73, 0xA1D028F9B467E25C, 0xE577D004DA4F5243];

    [Fact]
    public void EverySampleRetainsGearXpAndFlagValuesAndByteIdenticalRoundTrip()
    {
        var files = SampleSaves.Load("PPSA08330").AllDiskFiles
            .Where(f => f.FileName.StartsWith("GOWRSAVE", StringComparison.Ordinal)).ToArray();
        Assert.Equal(152, files.Length);
        var references = new RagnarokReferences(RagnarokTestSupport.PinnedReferences());
        var total = 0;
        var matched = new HashSet<ulong>();
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var index = RagnarokRecordIndex.Parse(bytes, false);
            Assert.NotEmpty(index.Gear.Records);
            Assert.Equal(index.Nested.WalletBlockOffset, index.Gear.End);
            var fingerprint = Fingerprint(bytes, index, references.Gear);
            var key = $"{file.Directory.Generation}/{file.FileName}";
            Assert.True(RagnarokFieldDiscoveryExpectations.Fingerprints[key] == fingerprint, key);
            total += index.Gear.Records.Count;
            foreach (var record in index.Gear.Records)
                if (references.Gear.Find(record.Id) is not null) matched.Add(record.Id);
            using var document = new GodOfWarRagnarokPlugin().Open(file);
            var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
            var page = query.Query("gear", new() { Limit = 200 });
            Assert.Equal(index.Gear.Records.Count, page.TotalCount);
            Assert.All(page.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>(), field =>
            {
                Assert.True(field.Spec.ReadOnly);
                Assert.Equal(field.Value, document.GetValue(field.ValueId));
            });
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        Assert.Equal(14_886, total);
        Assert.Equal(141, matched.Count);
    }

    [Fact]
    public void SyntheticFlagsDoNotAcquireRuntimeUnlockOrNamedSkillSemantics()
    {
        var bytes = new RagnarokNestedFixture().Create();
        using var document = new GodOfWarRagnarokPlugin().Open(RagnarokTestSupport.GameplayFile(bytes));
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = query.Query("flags", new());
        Assert.All(page.Rows, row => Assert.Equal("0x04030201", Assert.IsType<TextNode>(row.Cells["flags"]).Text));
        Assert.All(page.Rows, row => Assert.Equal("Unknown group", Assert.IsType<TextNode>(row.Cells["group-name"]).Text));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static string Fingerprint(byte[] bytes, RagnarokRecordIndex index, RagnarokGearReferences references)
    {
        var text = new StringBuilder();
        foreach (var row in index.Gear.Records)
        {
            var slots = Convert.ToHexString(bytes.AsSpan(row.Offset + RagnarokGearRecords.HeaderSize,
                (int)row.SlotCount * RagnarokGearRecords.SlotSize));
            var level = references.Find(row.Id)?.Attributes["level"] ?? "Unknown";
            text.Append(CultureInfo.InvariantCulture, $"gear/{row.Instance}={row.Id:X16}/{row.Parent}/{slots}/{level}\n");
        }
        foreach (var wallet in index.Nested.Wallets)
        for (var row = 0; row < wallet.ResourceCount; row++)
        {
            var offset = wallet.ResourcesOffset + row * RagnarokFormat.WalletRowSize;
            var id = RagnarokFormat.UInt64(bytes, offset);
            if (XpIds.Contains(id))
                text.Append(CultureInfo.InvariantCulture, $"xp/{wallet.Id:X16}/{id:X16}={RagnarokFormat.UInt32(bytes, offset + sizeof(ulong))}\n");
        }
        foreach (var group in index.Nested.FlagGroups)
        for (var row = 0; row < group.Count; row++)
        {
            var offset = group.RowsOffset + row * RagnarokFlagGroup.RowSize;
            var id = RagnarokFormat.UInt64(bytes, offset);
            text.Append(CultureInfo.InvariantCulture, $"flags/{group.Id:X16}/{id:X16}={Convert.ToHexString(bytes.AsSpan(offset + sizeof(ulong), sizeof(uint)))}\n");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
