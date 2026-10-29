using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class ArisePartyCpTests
{
    private const string CurrentId = "party/current-cp";
    private const string MaximumId = "party/max-cp";
    private const string Source = "https://fearlessrevolution.com/download/file.php?id=41559";
    private const int CurrentOffset = 0x0C;
    private const int MaximumOffset = 0x0E;

    [Theory]
    [InlineData("SaveData_000.sav", 0, 0)]
    [InlineData("SaveData_001.sav", 33, 0)]
    [InlineData("SaveData_002.sav", 33, 1)]
    public void EverySampleAndTwin_Has155Of155BesideTheKnownPartyLayout(string name, uint experience, int appleGels)
    {
        var files = AriseSampleTests.GameplayFiles().Where(file => file.FileName == name).ToArray();
        Assert.Equal(2, files.Length);
        Assert.Single(files, file => file.Directory.IsBackup);
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            var save = AriseFormat.Decode(original);
            var party = save.Sections.Single(section => section.Name == "PartyProfile");
            Assert.Equal(0x009B009Bu, AriseFormat.U32(save.Payload, party.Offset + CurrentOffset));
            using var document = new TalesOfArisePlugin().Open(file);
            AssertFields(document, 155, 155);
            Assert.Equal(0, document.GetValue("party/gald").AsInt64());
            uint[] hp = [321, 282, 255, 291, 339, 312];
            for (var index = 0; index < hp.Length; index++)
            {
                Assert.Equal(5, document.GetValue($"character/{index}/level").AsInt64());
                Assert.Equal(hp[index], document.GetValue($"character/{index}/hp").AsUInt32());
                Assert.Equal(experience, document.GetValue($"character/{index}/experience").AsUInt32());
            }

            var inventory = save.Sections.Single(section => section.Name == "SaveData_ItemManager");
            Assert.Equal(appleGels, save.Payload[inventory.Offset + 24 * AriseTestContainer.ItemRecordSize]);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(17, 155, false)]
    [InlineData(155, 155, true)]
    [InlineData(0, ushort.MaxValue, true)]
    [InlineData(ushort.MaxValue, ushort.MaxValue, false)]
    [InlineData(156, 155, true)]
    public void SyntheticHalfwords_AreIndependentReadOnlyAndNeverClamped(int current, int maximum, bool reverseOrder)
    {
        var fixture = new AriseTestContainer(reverseOrder);
        var party = fixture.Body("PartyProfile");
        BinaryPrimitives.WriteUInt16LittleEndian(fixture.Payload.AsSpan(party + CurrentOffset), checked((ushort)current));
        BinaryPrimitives.WriteUInt16LittleEndian(fixture.Payload.AsSpan(party + MaximumOffset), checked((ushort)maximum));
        var original = fixture.Encrypt();
        using var document = new TalesOfArisePlugin().Open(
            MemorySaveSet.SingleFile(AriseTestContainer.FileName, original, AriseTestContainer.TitleId));
        AssertFields(document, current, maximum);

        // Even an unchanged target must be rejected: these are observations, never setters.
        Assert.False(document.ApplyEdits([new(CurrentId, FieldValue.Of((long)current))]).Succeeded);
        Assert.False(document.ApplyEdits([new(MaximumId, FieldValue.Of((long)maximum))]).Succeeded);
        Assert.False(document.ApplyEdits([new("party/gald", FieldValue.Of(999L)), new(CurrentId, FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(123, document.GetValue("party/gald").AsInt64());
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());

        // Existing unrelated writes keep both halfwords intact through integrity repair and undo.
        var edit = document.ApplyEdits([new("party/gald", FieldValue.Of(999L))]);
        Assert.True(edit.Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        using var reopened = new TalesOfArisePlugin().Open(
            MemorySaveSet.SingleFile(AriseTestContainer.FileName, output, AriseTestContainer.TitleId));
        AssertFields(reopened, current, maximum);
        Assert.True(document.ApplyEdits([new("party/gald", FieldValue.Of(123L))]).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static void AssertFields(ISaveDocument document, int current, int maximum)
    {
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var party = view.Tabs.Single(tab => tab.Title == "Party");
        foreach (var (id, expected, offset) in new[] { (CurrentId, current, CurrentOffset), (MaximumId, maximum, MaximumOffset) })
        {
            var field = Assert.Single(party.Children.OfType<FieldNode>(), field => field.ValueId == id);
            Assert.Equal(expected, field.Value.AsInt64());
            Assert.Equal(field.Value, document.GetValue(id));
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Contains("inferred", field.Label, StringComparison.OrdinalIgnoreCase);
            Assert.True(field.Spec.ReadOnly);
            Assert.Contains(Source, field.Description!);
            Assert.Contains($"+0x{offset:X2}", field.Description!);
            Assert.Contains("2026-10-29", field.Description!);
            Assert.Contains("No licence published; reused with attribution per owner decision 2026-10-28", field.Description!);
            Assert.Contains("spend/refill", field.Warning!);
        }

        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var evidence = queryable.Query("evidence/8CC3AF4C", new() { Offset = CurrentOffset / sizeof(uint), Limit = 1 });
        var word = Assert.IsType<FieldNode>(Assert.Single(evidence.Rows).Cells["value"]);
        Assert.Equal((uint)current | ((uint)maximum << 16), word.Value.AsUInt32());
        Assert.Equal(Confidence.Unknown, word.Confidence); // Raw explorer semantics remain unchanged.
        Assert.True(word.Spec.ReadOnly);
        Assert.Contains(Source, new TalesOfArisePlugin().Info.References);
    }
}
