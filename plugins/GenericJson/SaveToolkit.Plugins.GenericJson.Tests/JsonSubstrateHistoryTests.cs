using System.Text;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSubstrateHistoryTests
{
    [Fact]
    public void InsertedScalarAndImportedDuplicateKeepSeparateSourcesAcrossEveryHistoryTransition()
    {
        const string source = "\uFEFF \r\n{ \"\\u0061\" : -0,\t\"a\":1.2300E+02,\t\"huge\":1e999999999999999999999,\t\"nil\":null }\n";
        const string edited = "\uFEFF \r\n{ \"\\u0061\" : -0,\t\"a\":0.12345678901234567890123456789,\t\"huge\":1e999999999999999999999,\t\"nil\":null }\n";
        const string inserted = "\uFEFF \r\n{ \"\\u0061\" : -0,\t\"added\":null,\t\"a\":0.12345678901234567890123456789,\t\"huge\":1e999999999999999999999,\t\"nil\":null }\n";
        const string insertedEdited = "\uFEFF \r\n{ \"\\u0061\" : -0,\t\"added\":\"longer\",\t\"a\":0.12345678901234567890123456789,\t\"huge\":1e999999999999999999999,\t\"nil\":null }\n";
        const string removed = "\uFEFF \r\n{ \"added\":\"longer\",\t\"a\":0.12345678901234567890123456789,\t\"huge\":1e999999999999999999999,\t\"nil\":null }\n";
        using var fixture = new JsonSessionFixture(source);
        var originalFields = fixture.Fields();
        var duplicateId = originalFields[1].ValueId;
        Assert.True(originalFields[2].Spec.ReadOnly);
        AssertBytes(source);

        Assert.True(fixture.Edit(duplicateId, JsonEditingTests.Number("0.12345678901234567890123456789")).Ok);
        AssertBytes(edited);
        JsonArrayEditingTests.Apply(fixture, "object/0/insert", ("at", FieldValue.Of(1L)),
            ("name", FieldValue.Of("added")), ("json", FieldValue.Of("null")));
        AssertBytes(inserted);
        var addedId = fixture.Fields().Single(field => field.Label == "added").ValueId;
        Assert.DoesNotContain(addedId, originalFields.Select(field => field.ValueId));
        Assert.True(fixture.Edit(addedId, JsonEditingTests.Text("longer")).Ok);
        AssertBytes(insertedEdited);
        JsonArrayEditingTests.Apply(fixture, "object/0/remove", ("at", FieldValue.Of(0L)));
        AssertBytes(removed);
        Assert.Equal(duplicateId, fixture.Fields().Single(field => field.Label == "a").ValueId);

        foreach (var expected in new[] { insertedEdited, inserted, edited, source })
        {
            Assert.True(fixture.Session.Undo().Ok);
            AssertBytes(expected);
        }
        Assert.Equal(originalFields.Select(field => field.ValueId), fixture.Fields().Select(field => field.ValueId));
        foreach (var expected in new[] { edited, inserted, insertedEdited, removed })
        {
            Assert.True(fixture.Session.Redo().Ok);
            AssertBytes(expected);
        }
        Assert.Equal(addedId, fixture.Fields().Single(field => field.Label == "added").ValueId);

        void AssertBytes(string expected) => Assert.Equal(Encoding.UTF8.GetBytes(expected), fixture.Bytes);
    }
}
