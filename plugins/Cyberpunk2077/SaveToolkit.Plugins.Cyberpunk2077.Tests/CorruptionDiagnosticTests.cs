using System.Globalization;
using SaveToolkit.Testing;
using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

/// <summary>Pin the pre-migration parser's complete diagnostics, not only message fragments.</summary>
public sealed class CorruptionDiagnosticTests
{
    [Theory]
    // Captured on fc55788 before either helper migration. Includes successful trees as well as errors.
    [InlineData(false, "12F34CA33D2573930BB5BB4730497CCF852EF8387CA780966C152499E7001BFF", "en-US")]
    [InlineData(true, "71D45478F30458F2AD34ADB2803AFB69A857EB76B0A7E67A33C370C888ACD067", "en-US")]
    [InlineData(false, "12F34CA33D2573930BB5BB4730497CCF852EF8387CA780966C152499E7001BFF", "sv-SE")]
    [InlineData(true, "71D45478F30458F2AD34ADB2803AFB69A857EB76B0A7E67A33C370C888ACD067", "sv-SE")]
    public void MutationAndTruncationOutcomesKeepExactExceptionTypesAndMessages(bool compressed, string expected,
        string cultureName)
    {
        using var ambientCulture = new TestCulture(CultureInfo.GetCultureInfo(cultureName));
        Assert.Equal(expected, TranscriptFingerprint(compressed));
        Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
        Assert.Equal(cultureName, CultureInfo.CurrentUICulture.Name);
    }

    private static string TranscriptFingerprint(bool compressed)
    {
        // This golden pins historical diagnostics and numbers, independent of the machine's locale.
        using var culture = new TestCulture(CultureInfo.InvariantCulture);
        var valid = CsavCorruptionFixture.Flat(compressed: compressed);
        var transcript = new StringBuilder();
        void Record(byte[] bytes)
        {
            try
            {
                var container = CsavContainer.Parse(bytes);
                var tree = CsavTree.Parse(container);
                Assert.Equal(bytes, container.Rebuild(container.Payload));
                transcript.Append("accepted:");
                foreach (var node in tree.Nodes)
                    transcript.Append($"{node.Id}/{node.Name}/{node.NextId}/{node.ChildId}/{node.Offset}/{node.Size};");
                transcript.Append('\n');
            }
            catch (SaveFormatException error)
            {
                Assert.IsType<SaveFormatException>(error);
                transcript.Append(error.GetType().FullName).Append(':').Append(error.Message).Append('\n');
            }
        }

        for (var offset = 0; offset < valid.Length; offset++)
        {
            foreach (var replacement in new byte[] { 0, 0x7F, 0x80, 0xFF })
            {
                var bytes = valid.ToArray();
                bytes[offset] = replacement;
                Record(bytes);
            }
            Record(valid[..offset]);
            if (offset > valid.Length - sizeof(int)) continue;
            foreach (var replacement in new[] { int.MinValue, -2, -1, 0, 1, 3, 4097, 100_001, int.MaxValue })
            {
                var bytes = valid.ToArray();
                CsavCorruptionFixture.SetWord(bytes, offset, replacement);
                Record(bytes);
            }
        }

        var directory = BinaryData.Int32(valid, valid.Length - 8);
        for (var end = directory + 4; end < valid.Length - 8; end++)
            Record([.. valid.AsSpan(0, end), .. valid.AsSpan(valid.Length - 8)]);
        var data = BinaryData.Int32(valid, CsavCorruptionFixture.TableOffset);
        for (var end = data; end < directory; end++)
        {
            byte[] bytes = [.. valid.AsSpan(0, end), .. valid.AsSpan(directory)];
            CsavCorruptionFixture.SetWord(bytes, bytes.Length - 8, end);
            Record(bytes);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript.ToString())));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(4, 1)]
    [InlineData(int.MinValue, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void RangeErrorsRetainTheOriginalAbsoluteOffset(int offset, int length)
    {
        var error = Assert.Throws<SaveFormatException>(() => BinaryData.Slice(new byte[4], offset, length));
        Assert.Equal($"CSAV range at 0x{offset:X} exceeds its containing data.", error.Message);
    }

    [Fact]
    public void EveryTruncatedPrimitiveKeepsTheRangeDiagnostic()
    {
        for (var length = 0; length < sizeof(ulong); length++)
        {
            var bytes = new byte[length + 2];
            const string expected = "CSAV range at 0x2 exceeds its containing data.";
            Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => BinaryData.UInt64(bytes, 2)).Message);
            if (length >= sizeof(int)) continue;
            Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => BinaryData.Int32(bytes, 2)).Message);
            Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => BinaryData.UInt32(bytes, 2)).Message);
        }
        Assert.Empty(BinaryData.Slice(new byte[4], 4, 0).ToArray());
    }
}
