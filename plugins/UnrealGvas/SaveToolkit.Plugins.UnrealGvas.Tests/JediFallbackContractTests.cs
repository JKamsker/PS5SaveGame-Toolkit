using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class JediFallbackContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackTagsConsumeMetadataAndKeepExactRowsAndOpaqueBytes(bool invalidGuid)
    {
        byte[] array = GvasFixture.Body(writer =>
        {
            writer.Write(1);
            GvasFixture.Property(writer, "Health", "IntProperty", GvasFixture.Body(body => body.Write(42)));
            GvasFixture.String(writer, "None");
        });
        byte[] plain = GvasFixture.Body(writer =>
        {
            GvasFixture.Property(writer, "Enabled", "BoolProperty", [], boolean: true, guid: true);
            GvasFixture.Property(writer, "Choice", "EnumProperty", GvasFixture.Body(body => GvasFixture.String(body, "Mode::A")), "Mode");
            GvasFixture.Property(writer, "UnsupportedByte", "ByteProperty", [42], "None");
            // Deliberately lacks the general parser's struct-array inner tag, exercising the scanner.
            GvasFixture.Property(writer, "ActorRecords", "ArrayProperty", array, "StructProperty");
            byte[] tail = GvasFixture.Body(body => GvasFixture.Property(body, "Tail", "IntProperty", [1, 0, 0, 0]));
            if (invalidGuid) tail[^5] = 2;
            writer.Write(tail);
            GvasFixture.String(writer, "None");
        });
        byte[] bytes = TitleFixture.Jedi(TitleFixture.Compress(plain), (uint)plain.Length);
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save", bytes, "PPSA02199"));
        Assert.True(document.CanEdit);
        _ = document.BuildView();
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var rows = query.Query("jedi/actor-tags", new() { Limit = 200 }).Rows;
        string[] expected = [
            "actors/0|/Enabled|BoolProperty|True",
            "actors/1|/Choice|EnumProperty|Mode::A",
            "actors/2|/UnsupportedByte|ByteProperty|Actor payload is preserved; no game-specific schema is asserted.",
            $"actors/3|/ActorRecords|ArrayProperty|1 children; {array.Length} bytes",
            "actors/3/0|/ActorRecords/Health|IntProperty|42",
        ];
        if (!invalidGuid) expected = [.. expected, "actors/4|/Tail|IntProperty|1"];
        Assert.Equal(expected, rows.Select(row => string.Join("|", row.Key, Text(row, "path"), Text(row, "type"), Text(row, "value"))));
        Assert.All(rows, row => Assert.Equal("Inferred tag scan; incomplete schema; game meaning Unknown", Text(row, "evidence")));
        var observed = query.Query("jedi/actor-names", new() { Text = "Tail" });
        Assert.NotEmpty(observed.Rows); // Observed strings remain independent of accepted tag boundaries.
        var summary = Assert.Single(document.ExpandNode("jedi/actors").OfType<TableNode>());
        Assert.Equal(rows.Select(row => row.Key), summary.Rows.Select(row => row.Key));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static string Text(TableRow row, string column) => Assert.IsType<TextNode>(row.Cells[column]).Text;
}
