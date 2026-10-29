using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroParsingCharacterizationTests
{
    [Fact]
    public void MixedContentKeepsTokenOrderOccurrencesAndExactUntouchedBytesAcrossAnEdit()
    {
        const string text = "\r\n<PreInstall><Common><NumCoin note='中'> 0007 </NumCoin></Common>" +
            "<Mixed a='true'> \t<!--empty--> first <!--split--> \r\n<2>001</2> tail " +
            "<!--split--> last <2>-02</2></Mixed><Empty x=\"\"/></PreInstall>opaque-残存<broken";
        var original = AstroTestData.Bytes(text);
        using var document = AstroTestData.Open(original);
        const string mixedId = "/PreInstall[0]/Mixed[0]";
        var nodes = document.ExpandNode(mixedId);
        Assert.Equal(new[] { mixedId + "/@a", mixedId + "/text[0]", mixedId + "/text[1]", mixedId + "/text[2]" },
            nodes.OfType<FieldNode>().Select(field => field.ValueId));
        Assert.Equal(new[] { FieldValue.Of(true), FieldValue.Of("first"), FieldValue.Of("tail"), FieldValue.Of("last") },
            nodes.OfType<FieldNode>().Select(field => field.Value));
        Assert.All(nodes.OfType<FieldNode>(), field =>
        {
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Unknown, field.Confidence);
        });
        Assert.Equal(new[] { mixedId + "/2[0]", mixedId + "/2[1]" }, nodes.OfType<SectionNode>().Select(node => node.LazyId));
        Assert.Equal(FieldValue.Of(1L), Assert.Single(document.ExpandNode(mixedId + "/2[0]").OfType<FieldNode>()).Value);
        Assert.Equal(FieldValue.Of(-2L), Assert.Single(document.ExpandNode(mixedId + "/2[1]").OfType<FieldNode>()).Value);
        Assert.Equal(original, AstroTestData.Output(document));

        var coins = AstroTestData.Find(document, AstroTestData.CoinsPath);
        Assert.True(document.ApplyEdits([new ValueEdit(coins.ValueId, FieldValue.Of(8L))]).Succeeded);
        var expected = AstroTestData.Bytes(text.Replace("> 0007 </NumCoin>", "> 8 </NumCoin>", StringComparison.Ordinal));
        Assert.Equal(expected, AstroTestData.Output(document));
        using var reopened = AstroTestData.Open(expected);
        Assert.Equal(FieldValue.Of(8L), reopened.GetValue(coins.ValueId));
    }

    [Theory]
    [InlineData("<A>中</B>", "<A>中</B", "Mismatched closing tag for A.")]
    [InlineData("<A>中", "<A>中", "Unclosed tag A.")]
    [InlineData("<A>中<!--unfinished", "<A>中<!--", "Unterminated comment.")]
    [InlineData("<A x='中", "<A x='中", "Expected '\''.")]
    [InlineData("<A x='1' x='2'/>", "<A x='1' x", "Duplicate attribute.")]
    [InlineData("<A x='中'y='2'/>", "<A x='中'", "Expected whitespace before attribute.")]
    public void TokenizerDiagnosticsKeepTheirExactBytePosition(string body, string consumed, string diagnostic)
    {
        const string prefix = "\r\n<PreInstall>";
        var error = Assert.Throws<SaveFormatException>(() => AstroTestData.Open(AstroTestData.Bytes(prefix + body)));
        var offset = AstroTestData.TextOffset + Encoding.UTF8.GetByteCount(prefix + consumed);
        Assert.Equal($"{diagnostic} (byte 0x{offset:X})", error.Message);
    }
}
