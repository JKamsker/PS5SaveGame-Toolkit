using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroCorruptionTests
{
    private const string Prefix = "\r\n<PreInstall>";
    private const string Suffix = "</PreInstall>";

    [Fact]
    public void EveryTruncatedGrammarPrefixRejectsWithAByteDiagnostic()
    {
        const string valid = Prefix + "<!--keep--><Common><NumCoin>001</NumCoin></Common>" +
            "<1073741839 code='a>b' n=\"literal&amp;\">before<2>001</2>after</1073741839><Empty a=''/>" + Suffix;
        var original = AstroTestData.Bytes(valid);
        using (var document = AstroTestData.Open(original))
        {
            Assert.Equal(original, AstroTestData.Output(document));
        }

        for (var length = Prefix.Length; length < valid.Length; length++)
        {
            var truncated = original.ToArray();
            // Retain the fixed allocation and move its NUL terminator, so the tokenizer is reached.
            truncated.AsSpan(AstroTestData.TextOffset + length).Clear();
            var error = Assert.Throws<SaveFormatException>(() => AstroTestData.Open(truncated));
            Assert.Contains("(byte 0x", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("<A></B>", "Mismatched closing tag for A.")]
    [InlineData("<A>", "Unclosed tag A.")]
    [InlineData("<A a='1' a='2'/>", "Duplicate attribute.")]
    [InlineData("<A a='1'b='2'/>", "Expected whitespace before attribute.")]
    [InlineData("<A a=1/>", "Expected quoted attribute value.")]
    [InlineData("<A a='<bad'/>", "Unescaped tag in attribute.")]
    [InlineData("<A a '1'/>", "Expected '='.")]
    [InlineData("<A/ >", "Expected '>'.")]
    [InlineData("<A a='unterminated", "Expected '\''.")]
    [InlineData("<!--unterminated", "Unterminated comment.")]
    [InlineData("<!DOCTYPE A>", "Expected a tag or attribute name.")]
    [InlineData("<?instruction?>", "Expected a tag or attribute name.")]
    [InlineData("<![CDATA[text]]>", "Expected a tag or attribute name.")]
    [InlineData("<", "Expected a tag or attribute name.")]
    [InlineData("<A   ", "Expected a tag or attribute name.")]
    public void MalformedGrammarReportsTheCauseAndOffset(string body, string diagnostic)
    {
        var error = Assert.Throws<SaveFormatException>(() => AstroTestData.Open(AstroTestData.Bytes(Prefix + body)));
        Assert.StartsWith(diagnostic, error.Message, StringComparison.Ordinal);
        Assert.Contains("(byte 0x", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("80")]
    [InlineData("C0AF")]
    [InlineData("E282")]
    [InlineData("EDA080")]
    [InlineData("F4908080")]
    public void InvalidUtf8IsRejectedInValuesAndInOpaqueSuffix(string hex)
    {
        foreach (var prefix in new[] { Prefix + "<A>", Prefix + Suffix })
        {
            var bytes = AstroTestData.Bytes(prefix);
            Convert.FromHexString(hex).CopyTo(bytes, AstroTestData.TextOffset + Encoding.UTF8.GetByteCount(prefix));
            var error = Assert.Throws<SaveFormatException>(() => AstroTestData.Open(bytes));
            Assert.Equal("ASTRO text is not valid UTF-8.", error.Message);
            Assert.IsType<DecoderFallbackException>(error.InnerException);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(AstroTestData.TextOffset - 1)]
    [InlineData(AstroTestData.TextOffset)]
    [InlineData(AstroTestData.TextOffset + 13)]
    [InlineData(AstroTestData.AllocationSize - 1)]
    [InlineData(AstroTestData.AllocationSize + 1)]
    public void PhysicallyTruncatedOrOversizedAllocationRejectsBeforeParsing(int length)
    {
        var bytes = new byte[length];
        var original = AstroTestData.Bytes();
        original.AsSpan(0, Math.Min(length, original.Length)).CopyTo(bytes);
        var file = AstroTestData.File(bytes);
        var plugin = new AstroPlayroomPlugin();
        Assert.False(plugin.Probe(file).IsMatch);
        var error = Assert.Throws<SaveFormatException>(() => plugin.Open(file));
        Assert.Equal("ASTRO's PLAYROOM memory.dat must be exactly 1 MiB.", error.Message);
    }

    [Fact]
    public void EarlyNulWithRemainingDataAndMissingNulHaveDistinctRejections()
    {
        var early = AstroTestData.Bytes();
        early[AstroTestData.TextOffset + Prefix.Length] = 0;
        Assert.Equal("Unsupported nonzero data after the ASTRO NUL terminator.",
            Assert.Throws<SaveFormatException>(() => AstroTestData.Open(early)).Message);

        var missing = AstroTestData.Bytes(Prefix + Suffix);
        missing.AsSpan(AstroTestData.TextOffset + Prefix.Length + Suffix.Length).Fill((byte)'x');
        Assert.Equal("ASTRO text has no NUL terminator within its allocation.",
            Assert.Throws<SaveFormatException>(() => AstroTestData.Open(missing)).Message);
    }

    [Fact]
    public void FinalByteNulAndNonXmlUtf8SuffixRemainSupported()
    {
        const string root = Prefix + "<A>中</A>" + Suffix;
        const string opaque = "残存<!--not markup<broken a='";
        var padding = AstroTestData.AllocationSize - AstroTestData.TextOffset -
            Encoding.UTF8.GetByteCount(root + opaque) - 1;
        var bytes = AstroTestData.Bytes(root + opaque + new string('x', padding));
        Assert.NotEqual(0, bytes[^2]);
        Assert.Equal(0, bytes[^1]);
        using var document = AstroTestData.Open(bytes);
        Assert.Equal("中", AstroTestData.Find(document, "PreInstall/A").Value.AsString());
        Assert.Equal(bytes, AstroTestData.Output(document));
    }
}
