using System.Diagnostics;
using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseSampleTests
{
    private readonly TalesOfArisePlugin _plugin = new();

    [Fact]
    public void EverySlotAndBackup_DecryptsWithIndependentWholePlaintextDigest_AndReencryptsExactly()
    {
        var files = GameplayFiles().ToArray();
        Assert.Equal(6, files.Length);
        Assert.Equal(3, files.Count(f => f.Directory.IsBackup));
        var directoryOffsets = new HashSet<uint>();
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            Assert.True(_plugin.Probe(file).IsMatch);
            var timer = Stopwatch.StartNew();
            var save = AriseFormat.Decode(bytes);
            using var document = _plugin.Open(file);
            _ = document.BuildView();
            timer.Stop();
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"Opening {file.FileName} took {timer.Elapsed}.");
            Assert.Equal(30, save.Sections.Count);
            Assert.Equal(ExpectedDigest(file.FileName), Convert.ToHexString(SHA256.HashData(save.Payload)));
            Assert.Equal(bytes, AriseFormat.Encode(save));
            directoryOffsets.Add(AriseFormat.U32(save.Payload, 0x38));
            Assert.True(Hash(bytes.AsSpan(AriseFormat.CipherOffset)).AsSpan().SequenceEqual(bytes.AsSpan(AriseFormat.HashOffset, 20)));
            Assert.False(Hash(save.Payload.AsSpan(0x20)).AsSpan().SequenceEqual(bytes.AsSpan(AriseFormat.HashOffset, 20)));
        }

        Assert.Equal(3, directoryOffsets.Count); // Fixed physical section addresses would fail this corpus.
        foreach (var group in files.GroupBy(f => f.FileName))
        {
            Assert.Equal(group.First().ReadAllBytes(), group.Last().ReadAllBytes());
        }
    }

    [Theory]
    [InlineData("SaveData_000.sav", 1890, 113431, 0, 24)]
    [InlineData("SaveData_001.sav", 2770, 166253, 33, 24)]
    [InlineData("SaveData_002.sav", 3166, 189968, 33, 25)]
    public void KnownSampleValues_AgreeAcrossMovingSections(string name, uint seconds, uint ticks, uint experience, int itemCount)
    {
        foreach (var file in GameplayFiles().Where(f => f.FileName == name))
        {
            var bytes = file.ReadAllBytes();
            var save = AriseFormat.Decode(bytes);
            using var document = _plugin.Open(file);
            _ = document.BuildView();
            var inventory = document.LoadInventory();
            Assert.Equal(itemCount, inventory.Rows.Count);
            Assert.Equal(0, document.GetValue("party/gald").AsInt64());
            Assert.Equal(seconds, document.GetValue("party/play-seconds").AsUInt32());
            Assert.Equal(ticks, document.GetValue("party/play-ticks").AsUInt32());
            Assert.Equal(seconds, ticks / 60);
            Assert.Equal(seconds, AriseFormat.U32(bytes, 8));
            uint[] hitPoints = [321, 282, 255, 291, 339, 312];
            for (var i = 0; i < hitPoints.Length; i++)
            {
                Assert.Equal(5, document.GetValue($"character/{i}/level").AsInt64());
                Assert.Equal(hitPoints[i], document.GetValue($"character/{i}/hp").AsUInt32());
                Assert.Equal(hitPoints[i], document.GetValue($"character/{i}/base-hp").AsUInt32());
                Assert.Equal(experience, document.GetValue($"character/{i}/experience").AsUInt32());
            }

            var party = save.Sections.Single(s => s.Name == "PartyProfile");
            Assert.Equal(ticks, AriseFormat.U32(save.Payload, party.Offset + 0x2008));
            if (name == "SaveData_002.sav")
            {
                Assert.Equal(1, document.GetValue("inventory/24/quantity").AsInt64());
            }
        }
    }

    [Fact]
    public void CuratedEdits_RoundTrip_PreserveEveryOtherPlaintextByteAndEntireOuterHeader()
    {
        foreach (var file in GameplayFiles())
        {
            using var document = _plugin.Open(file);
            _ = document.BuildView();
            _ = document.LoadInventory();
            var before = AriseFormat.Decode(file.ReadAllBytes());
            List<ValueEdit> edits =
            [
                new("party/gald", FieldValue.Of(12345L)),
                new("character/0/sp", FieldValue.Of(100L)),
                new("character/0/hp", FieldValue.Of(300L)),
                new("character/0/level", FieldValue.Of(6L)),
            ];
            var allowed = new HashSet<int>();
            Allow(before.Sections.Single(s => s.Name == "PartyProfile").Offset + 0x18, 4);
            var character = before.Sections.Single(s => s.Name == "ArisePCStatus_000").Offset;
            Allow(character + 0x30, 4);
            Allow(character + 8, 4);
            Allow(character + 4, 4);
            if (file.FileName == "SaveData_002.sav")
            {
                edits.Add(new("inventory/24/quantity", FieldValue.Of(12L)));
                Allow(before.Sections.Single(s => s.Name == "SaveData_ItemManager").Offset + 24 * 16, 1);
            }

            var outcome = document.ApplyEdits(edits);
            Assert.True(outcome.Succeeded);
            Assert.Equal(edits.Count, outcome.Changes.Count(change => edits.Any(edit => edit.ValueId == change.ValueId)));
            Assert.All(outcome.Changes.Where(change => change.ValueId.StartsWith("sheet/", StringComparison.Ordinal)),
                change => Assert.Equal(change.NewValue, document.GetValue(change.ValueId)));
            var output = Assert.Single(document.Serialize()).Content.ToArray();
            Assert.True(output.AsSpan(0, AriseFormat.PayloadOffset).SequenceEqual(before.Original.AsSpan(0, AriseFormat.PayloadOffset)));
            var after = AriseFormat.Decode(output);
            for (var offset = 0x20; offset < after.Payload.Length; offset++)
            {
                if (!allowed.Contains(offset))
                {
                    Assert.Equal(before.Payload[offset], after.Payload[offset]);
                }
            }

            using var reopened = _plugin.Open(MemorySaveSet.SingleFile(file.FileName, output, "PPSA02745"));
            _ = reopened.BuildView();
            _ = reopened.LoadInventory();
            foreach (var edit in edits)
            {
                Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
            }

            // Undo through the same bindings restores even the original ciphertext and SHA1.
            Assert.True(document.ApplyEdits(outcome.Changes.Where(change => edits.Any(edit => edit.ValueId == change.ValueId))
                .Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
            Assert.Equal(before.Original, Assert.Single(document.Serialize()).Content.ToArray());

            void Allow(int offset, int count) => allowed.UnionWith(Enumerable.Range(offset, count));
        }
    }

    [Fact]
    public void InvalidBatch_IsAtomic_AndUnknownEquipmentCannotBeEdited()
    {
        using var document = _plugin.Open(GameplayFiles().First());
        _ = document.BuildView();
        _ = document.LoadInventory();
        var original = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(document.ApplyEdits([new("party/gald", FieldValue.Of(50L)), new("character/0/level", FieldValue.Of(101L))]).Succeeded);
        Assert.False(document.ApplyEdits([new("inventory/0/quantity", FieldValue.Of(2L))]).Succeeded);
        Assert.False(document.ApplyEdits([new("party/play-seconds", FieldValue.Of(100L))]).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    internal static IEnumerable<ISaveFile> GameplayFiles() => ((ISaveSet)SampleSaves.Load("PPSA02745")).AllFiles
        .Where(f => f.FileName.StartsWith("SaveData_", StringComparison.Ordinal));

    internal static byte[] Hash(ReadOnlySpan<byte> bytes)
    {
#pragma warning disable CA5350
        return SHA1.HashData(bytes);
#pragma warning restore CA5350
    }

    private static string ExpectedDigest(string fileName) => fileName switch
    {
        "SaveData_000.sav" => "E6EED74675DDD24083BE43D826FA5BD36F6B4CC2CB53EECD42E9D59908FBC5DD",
        "SaveData_001.sav" => "FBA12A15AB1543B7B1964D3A48F7A76CB1B968EC3584FC1E43C02FD048FF4BD9",
        "SaveData_002.sav" => "2A51715D94BFD83D164D046120D556870AA67E7231618A1AD368621ECF5799BE",
        _ => throw new ArgumentException("Unknown sample slot.", nameof(fileName)),
    };
}
