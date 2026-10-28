using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroTokenizerTests
{
    [Fact]
    public void NumericTags_Attributes_Comments_MixedContent_AndRepeatedNamesArePreserved()
    {
        const string text = "\r\n<PreInstall><!--keep--><1073741839 code='a>b' n=\"中&amp;\">" +
            " before <2>001</2> after </1073741839><Empty a=''/><A>true</A><A>false</A></PreInstall>opaque";
        var bytes = AstroTestData.Bytes(text);
        using var document = AstroTestData.Open(bytes);
        var fields = AstroTestData.Fields(document);
        Assert.Contains(fields, f => f.Description == "PreInstall/1073741839/@code" && f.Value.AsString() == "a>b");
        Assert.Contains(fields, f => f.Description == "PreInstall/1073741839/@n" && f.Value.AsString() == "中&amp;");
        Assert.Contains(fields, f => f.Description == "PreInstall/Empty/@a" && f.Value.AsString() == "");
        Assert.Equal(2, fields.Count(f => f.Description == "PreInstall/A"));
        Assert.Equal(2, fields.Count(f => f.Description == "PreInstall/1073741839"));
        Assert.All(fields, f => Assert.True(f.Spec.ReadOnly));
        Assert.Equal(bytes, AstroTestData.Output(document));
    }

    [Fact]
    public void WideTree_PagesEveryValue_AndRejectsUnknownLazyIds()
    {
        var text = "\r\n<PreInstall><Many>" + string.Concat(Enumerable.Range(0, 180).Select(i => $"<{i}>{i}</{i}>")) + "</Many></PreInstall>";
        using var document = AstroTestData.Open(AstroTestData.Bytes(text));
        Assert.Equal(180, AstroTestData.Fields(document).Count);
        var root = Assert.IsType<TabsNode>(document.BuildView());
        Assert.IsType<SectionNode>(root.Tabs[^1].Children[^1]);
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("missing"));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("/PreInstall[0]|-1"));
    }

    [Theory]
    [InlineData("\r\n<PreInstall><A>1</B></PreInstall>")]
    [InlineData("\r\n<PreInstall><A>1</A>")]
    [InlineData("\r\n<PreInstall><A x='unterminated></A></PreInstall>")]
    [InlineData("\r\n<PreInstall><A x='1' x='2'/></PreInstall>")]
    [InlineData("\r\n<PreInstall><A x='1'y='2'/></PreInstall>")]
    [InlineData("\r\n<PreInstall><!--unclosed</PreInstall>")]
    [InlineData("\r\n<PreInstall><!DOCTYPE x></PreInstall>")]
    [InlineData("\r\n<PreInstall><A")]
    [InlineData("\r\n<PreInstall><A   ")]
    public void MalformedStructure_IsRejected(string text) =>
        Assert.Throws<SaveFormatException>(() => AstroTestData.Open(AstroTestData.Bytes(text)));

    [Fact]
    public void UnsupportedAllocation_InvalidEncoding_AndMissingTerminatorAreRejected()
    {
        Assert.Throws<SaveFormatException>(() => AstroTestData.Open(new byte[AstroTestData.AllocationSize - 1]));
        Assert.Throws<SaveFormatException>(() => AstroTestData.Open(new byte[AstroTestData.AllocationSize + 1]));
        var preamble = AstroTestData.Bytes();
        preamble[0] = 1;
        Assert.Throws<SaveFormatException>(() => AstroTestData.Open(preamble));
        var trailing = AstroTestData.Bytes();
        trailing[^1] = 1;
        Assert.Throws<SaveFormatException>(() => AstroTestData.Open(trailing));
        var unterminated = AstroTestData.Bytes(AstroTestData.MinimalRoot + new string('x',
            AstroTestData.AllocationSize - AstroTestData.TextOffset - Encoding.UTF8.GetByteCount(AstroTestData.MinimalRoot)));
        Assert.Throws<SaveFormatException>(() => AstroTestData.Open(unterminated));
        var invalidEncoding = AstroTestData.Bytes();
        invalidEncoding[AstroTestData.TextOffset + 14] = 0xFF;
        Assert.Throws<SaveFormatException>(() => AstroTestData.Open(invalidEncoding));
    }

    [Fact]
    public void ExcessiveDepthOrNodes_AreRejected()
    {
        var deep = "\r\n<PreInstall>" + string.Concat(Enumerable.Repeat("<A>", 70)) +
            string.Concat(Enumerable.Repeat("</A>", 70)) + "</PreInstall>";
        Assert.Throws<SaveFormatException>(() => AstroTestData.Open(AstroTestData.Bytes(deep)));
        var wide = "\r\n<PreInstall>" + string.Concat(Enumerable.Repeat("<A>0</A>", 4100)) + "</PreInstall>";
        Assert.Throws<SaveFormatException>(() => AstroTestData.Open(AstroTestData.Bytes(wide)));
    }

    [Fact]
    public void Probe_RequiresTitle_FileRole_Size_AndSignature()
    {
        var plugin = new AstroPlayroomPlugin();
        var bytes = AstroTestData.Bytes();
        Assert.Equal(ProbeScore.Title, plugin.Probe(AstroTestData.File(bytes)).Score);
        Assert.False(plugin.Probe(SaveToolkit.Testing.MemorySaveSet.SingleFile("memory.dat", bytes)).IsMatch);
        Assert.False(plugin.Probe(SaveToolkit.Testing.MemorySaveSet.SingleFile("other.dat", bytes, "PPSA01325")).IsMatch);
        Assert.False(plugin.Probe(AstroTestData.File(bytes[..100])).IsMatch);
        bytes[AstroTestData.TextOffset] = 1;
        Assert.False(plugin.Probe(AstroTestData.File(bytes)).IsMatch);
    }
}
