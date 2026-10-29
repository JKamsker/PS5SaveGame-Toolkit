using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseTraversalCharacterizationTests
{
    [Theory]
    [InlineData(AriseTestContainer.SectionVersionOffset)]
    [InlineData(AriseTestContainer.SectionIdOffset)]
    [InlineData(AriseTestContainer.SectionSizeOffset)]
    public void InvalidSectionLayout_PrecedesInvalidFollowingPointer(int fieldOffset)
    {
        var fixture = new AriseTestContainer();
        fixture.Write(fixture.Header(0) + fieldOffset, uint.MaxValue);
        fixture.Write(fixture.Header(0) + AriseTestContainer.NextSectionOffset, uint.MaxValue);
        var bytes = fixture.Encrypt();
        var original = bytes.ToArray();
        var file = MemorySaveSet.SingleFile(AriseTestContainer.FileName, bytes, AriseTestContainer.TitleId);
        var error = Assert.Throws<SaveFormatException>(() => new TalesOfArisePlugin().Open(file));
        Assert.Equal($"Unsupported Tales of Arise section {fixture.Sections[0].Name} layout.", error.Message);
        Assert.Equal(original, bytes);
    }

    [Theory]
    [InlineData("offset", null, false, 1, 4, 8)]
    [InlineData("offset", "0x0000000a", true, 0, 12, 4)]
    [InlineData("value", "0x0000000a", true, 0, 4, 12)]
    [InlineData("hex", "0x0000000a", false, 0, 4, 12)]
    public void EvidencePages_RenderOnlyRequestedOffsetsWithStableFieldMetadata(
        string sort, string? text, bool descending, int offset, int first, int second)
    {
        var fixture = new AriseTestContainer();
        var section = fixture.Sections.Single(section => section.Name == "PartyProfile");
        fixture.Payload.AsSpan(section.Offset, section.Size).Clear();
        fixture.Write(section.Offset + 4, 10);
        fixture.Write(section.Offset + 8, 2);
        fixture.Write(section.Offset + 12, 10);
        var collection = new AriseWordCollection(new AriseSave([], fixture.Payload, fixture.Sections), section);
        var rendered = new List<int>();
        var spec = IntegerSpec.UInt32 with { ReadOnly = true };
        var page = collection.Query(new() { SortColumn = sort, Text = text, Descending = descending, Offset = offset, Limit = 2 }, Field);
        Assert.Equal(new[] { first, second }, rendered);
        Assert.Equal(section.Size / sizeof(uint), page.TotalCount);
        Assert.Equal(text is null ? page.TotalCount : 2, page.FilteredCount);
        Assert.Equal(offset, page.Offset);
        Assert.Equal(2, page.Limit);
        foreach (var (row, relative) in page.Rows.Zip(rendered))
        {
            Assert.Equal($"{section.Id:X8}/{relative:X6}", row.Key);
            Assert.Equal($"+0x{relative:X6}", Assert.IsType<TextNode>(row.Cells["offset"]).Text);
            Assert.Equal($"0x{AriseFormat.U32(fixture.Payload, section.Offset + relative):X8}", Assert.IsType<TextNode>(row.Cells["hex"]).Text);
            var field = Assert.IsType<FieldNode>(row.Cells["value"]);
            Assert.Equal($"evidence/{section.Id:X8}/{relative:X6}", field.ValueId);
            Assert.Equal($"PartyProfile +0x{relative:X}", field.Label);
            Assert.Same(spec, field.Spec);
            Assert.Equal(Confidence.Unknown, field.Confidence);
            Assert.Null(field.Warning);
            Assert.Equal(collection.Metadata.Description, field.Description);
        }

        FieldNode Field(string id, string label, int absolute)
        {
            rendered.Add(absolute - section.Offset);
            return new(id, label, spec, FieldValue.Of((long)AriseFormat.U32(fixture.Payload, absolute)))
                { Confidence = Confidence.Verified, Description = "Callback description", Warning = "Callback warning" };
        }
    }
}
