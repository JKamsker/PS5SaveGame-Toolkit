using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroSampleCorruptionTests
{
    private static ReadOnlySpan<byte> ClosingRoot => "</PreInstall>"u8;
    private const string OpeningRoot = "\r\n<PreInstall>";
    private const int SampledBoundaryCount = 16;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TruncatedSampleGrammarReachesTokenizerAndReportsOffset(int generation)
    {
        var original = Sample(generation);
        var closing = original.AsSpan().IndexOf(ClosingRoot);
        Assert.True(closing > AstroTestData.TextOffset + OpeningRoot.Length);
        var tagOffsets = Enumerable.Range(AstroTestData.TextOffset + OpeningRoot.Length,
                closing - AstroTestData.TextOffset - OpeningRoot.Length)
            .Where(offset => original[offset] is (byte)'<' or (byte)'>').ToArray();
        Assert.True(tagOffsets.Length >= SampledBoundaryCount);
        var cuts = Enumerable.Range(0, SampledBoundaryCount)
            .Select(i => tagOffsets[i * (tagOffsets.Length - 1) / (SampledBoundaryCount - 1)])
            .Concat(Enumerable.Range(closing, ClosingRoot.Length)).Distinct().ToArray();
        foreach (var cut in cuts)
        {
            var truncated = original.ToArray();
            truncated.AsSpan(cut).Clear();
            var error = Assert.Throws<SaveFormatException>(() => AstroTestData.Open(truncated));
            Assert.Contains("(byte 0x", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CorruptRootClosingTagIsRejectedWithoutChangingImportedSample(int generation)
    {
        var original = Sample(generation);
        var mutated = original.ToArray();
        var closing = mutated.AsSpan().IndexOf(ClosingRoot);
        Assert.True(closing > AstroTestData.TextOffset);
        mutated[closing + 2] = (byte)'X';
        var error = Assert.Throws<SaveFormatException>(() => AstroTestData.Open(mutated));
        Assert.StartsWith("Mismatched closing tag for PreInstall.", error.Message, StringComparison.Ordinal);
        using var document = AstroTestData.Open(original);
        Assert.Equal(original, AstroTestData.Output(document));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BytesAfterCompleteRootAreOpaqueAndPreserved(int generation)
    {
        var bytes = Sample(generation).ToArray();
        var rootEnd = bytes.AsSpan().IndexOf(ClosingRoot) + ClosingRoot.Length;
        Assert.True(rootEnd > AstroTestData.TextOffset + ClosingRoot.Length);
        bytes.AsSpan(rootEnd).Clear();
        // Deliberately invalid markup, but valid UTF-8: the tokenizer must stop at the root.
        "<broken a='<!--"u8.CopyTo(bytes.AsSpan(rootEnd));
        using var document = AstroTestData.Open(bytes);
        _ = AstroTestData.Fields(document);
        Assert.Equal(bytes, AstroTestData.Output(document));
    }

    private static byte[] Sample(int generation) => Assert.Single(AstroTestData.Samples(),
        file => file.Directory.Generation == generation && !file.Directory.IsBackup).ReadAllBytes();
}
