using System.Buffers.Binary;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasProbeTests
{
    [Theory]
    [InlineData(1, 4, 11, false)]
    [InlineData(2, 4, 27, true)]
    [InlineData(3, 5, 3, false)]
    [InlineData(3, 5, 4, true)]
    public void OpenableHeaderVariants_AreClaimedInPlainAndWrappedFiles(int version, ushort major, ushort minor, bool wide)
    {
        var bytes = Header(version, major, minor, wide: wide);
        foreach (var original in new[] { bytes, GvasEnvelopeTests.Wrap(bytes), GvasEnvelopeTests.Wrap(bytes, true) })
        {
            var file = MemorySaveSet.SingleFile("save", original);
            var plugin = new UnrealGvasPlugin();
            using var document = plugin.Open(file);
            Assert.Equal(ProbeScore.Engine, plugin.Probe(file).Score);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaximumSupportedHeaderLengths_DoNotIntroduceFalseNegatives(bool wide)
    {
        var bytes = Header(wide: wide, branch: new string('B', GvasFile.MaxStringLength - 1),
            saveClass: new string('C', GvasFile.MaxStringLength - 1), count: GvasFile.MaxNodes);
        var file = MemorySaveSet.SingleFile("save", bytes);
        var plugin = new UnrealGvasPlugin();
        using var document = plugin.Open(file);
        Assert.True(plugin.Probe(file).IsMatch);
    }

    [Theory]
    [InlineData(4, 27)]
    [InlineData(5, 4)]
    public void TruncatedHeadersAndFirstNames_AreNotClaimedInAnyEnvelope(ushort major, ushort minor)
    {
        var bytes = Header(major >= 5 ? 3 : 2, major, minor);
        var plugin = new UnrealGvasPlugin();
        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = bytes[..length];
            foreach (var original in new[] { truncated, GvasEnvelopeTests.Wrap(truncated), GvasEnvelopeTests.Wrap(truncated, true) })
                Assert.False(plugin.Probe(MemorySaveSet.SingleFile("save", original)).IsMatch, $"Header length {length}");
        }
    }

    public static IEnumerable<object[]> InvalidHeaders()
    {
        yield return [Header(version: 0)];
        yield return [Header(version: 4)];
        yield return [Header(major: 3)];
        yield return [Header(major: 6)];
        yield return [Header(format: 2)];
        yield return [Header(count: -1)];
        yield return [Header(count: GvasFile.MaxNodes + 1)];
        yield return [Header(version: 3, major: 5, minor: 4, flags: 1)];
        yield return [Header(firstName: "")];
        // FString branch starts after magic, save/package versions and the UE4 engine version.
        const int branchLengthOffset = 4 + 2 * sizeof(int) + 3 * sizeof(ushort) + sizeof(uint);
        foreach (var length in new[] { int.MinValue, GvasFile.MaxStringLength + 1, -GvasFile.MaxStringLength - 1 })
        {
            var bytes = Header();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(branchLengthOffset), length);
            yield return [bytes];
        }
        var badTerminator = Header(branch: "B");
        badTerminator[branchLengthOffset + sizeof(int) + 1] = 1;
        yield return [badTerminator];
        var badUtf8 = Header(branch: "B");
        badUtf8[branchLengthOffset + sizeof(int)] = 0xFF;
        yield return [badUtf8];
        var badUtf16 = Header(wide: true, branch: "B");
        BinaryPrimitives.WriteUInt16LittleEndian(badUtf16.AsSpan(branchLengthOffset + sizeof(int)), 0xD800);
        yield return [badUtf16];
    }

    [Theory]
    [MemberData(nameof(InvalidHeaders))]
    public void UnsupportedOrMalformedHeaders_AreRejectedByProbeAndOpen(byte[] bytes)
    {
        var plugin = new UnrealGvasPlugin();
        foreach (var original in new[] { bytes, GvasEnvelopeTests.Wrap(bytes), GvasEnvelopeTests.Wrap(bytes, true) })
        {
            var file = MemorySaveSet.SingleFile("save", original);
            Assert.Throws<SaveFormatException>(() => plugin.Open(file));
            Assert.False(plugin.Probe(file).IsMatch);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Probe_StopsBeforePropertyTagsAndPayloads_OnShortNonSeekableReads(int wrapper)
    {
        var bytes = Header(firstName: "FirstProperty"); // Deliberately has no tag or payload.
        if (wrapper != 0) bytes = GvasEnvelopeTests.Wrap(bytes, wrapper == 2);
        using var stream = new ShortReadStream(bytes);
        Assert.NotNull(GvasEnvelope.Probe(stream));
        Assert.InRange(stream.BytesRead, 1, bytes.Length);
    }

    [Fact]
    public void ValidPlainHeader_KeepsTheTitleSpecificLabel()
    {
        var file = MemorySaveSet.SingleFile("save", Header(), "PPSA01288");
        Assert.Equal("Sackboy save (Unreal GVAS)", new UnrealGvasPlugin().Probe(file).Label);
    }

    private static byte[] Header(int version = 2, ushort major = 4, ushort minor = 27,
        bool wide = false, string branch = "branch", string saveClass = "Save", int format = 3,
        int count = 1, byte flags = 0, string firstName = "None")
        => GvasFixture.Body(writer =>
        {
            writer.Write("GVAS"u8);
            writer.Write(version);
            writer.Write(524);
            if (version >= 3) writer.Write(1009);
            writer.Write(major);
            writer.Write(minor);
            writer.Write((ushort)0);
            writer.Write(0U);
            GvasFixture.String(writer, branch, wide);
            if (major >= 5 || minor >= 12)
            {
                writer.Write(format);
                writer.Write(count);
                if (count is >= 0 and <= GvasFile.MaxNodes) writer.Write(new byte[count * (16 + sizeof(int))]);
            }
            GvasFixture.String(writer, saveClass, wide);
            if (major > 5 || (major == 5 && minor >= 4)) writer.Write(flags);
            GvasFixture.String(writer, firstName, wide);
        });

    private sealed class ShortReadStream(byte[] bytes) : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            // A probe that scans beyond the first name is a regression, even if it catches this exception.
            if (BytesRead == bytes.Length) throw new InvalidOperationException("Probe read past the header and first name.");
            if (buffer.IsEmpty) return 0;
            buffer[0] = bytes[BytesRead++];
            return 1;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
