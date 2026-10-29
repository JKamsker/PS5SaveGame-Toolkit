namespace SaveToolkit.Codecs.Tests;

public sealed class OodleWorkspaceTests
{
    [Theory]
    [MemberData(nameof(OodleEntropyTests.Vectors), MemberType = typeof(OodleEntropyTests))]
    public void Reused_workspace_decodes_vectors_into_only_the_requested_slice(byte[] stream, byte[] expected)
    {
        using var workspace = new OodleWorkspace();
        Prime(workspace);
        byte[] output = Enumerable.Repeat((byte)0xCD, expected.Length + 2).ToArray();
        byte[] before = stream.ToArray();
        for (int iteration = 0; iteration < 3; iteration++)
        {
            OodleCodec.Decompress(stream, output.AsSpan(1, expected.Length), workspace);
            Assert.Equal(expected, output.AsSpan(1, expected.Length).ToArray());
            Assert.Equal(0xCD, output[0]);
            Assert.Equal(0xCD, output[^1]);
            Assert.Equal(before, stream);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(131073)]
    [InlineData(262145)]
    [InlineData(1000000)]
    public void Workspace_grows_and_reuses_buffers_across_quantum_boundaries(int length)
    {
        using var workspace = new OodleWorkspace();
        byte[] expected = new byte[length];
        new Random(40).NextBytes(expected);
        byte[] output = new byte[length];
        byte[] input = OodleCodec.EncodeStored(expected);
        for (int iteration = 0; iteration < 2; iteration++)
        {
            OodleCodec.Decompress(input, output, workspace, Math.Max(1, length));
            Assert.Equal(expected, output);
            Prime(workspace);
        }
    }

    [Fact]
    public void Input_and_destination_may_overlap()
    {
        byte[] expected = "overlapping input and output"u8.ToArray();
        byte[] stream = OodleCodec.EncodeStored(expected);
        using var workspace = new OodleWorkspace();
        OodleCodec.Decompress(stream, stream.AsSpan(0, expected.Length), workspace);
        Assert.Equal(expected, stream.AsSpan(0, expected.Length).ToArray());
    }

    [Fact]
    public void Failure_keeps_destination_unchanged_and_workspace_recovers()
    {
        using var workspace = new OodleWorkspace();
        Prime(workspace);
        byte[] output = Enumerable.Repeat((byte)0xCD, 64).ToArray();
        byte[] before = output.ToArray();
        byte[] valid = OodleTestStreams.Quantum(OodleTestStreams.Entropy(3, [42], output.Length));
        byte[] nested = OodleTestStreams.Entropy(3, [42], output.Length);
        for (int depth = 0; depth < 20; depth++)
            nested = OodleTestStreams.Entropy(5, [2, .. nested, 0, 0, 0], output.Length);
        byte[][] invalid = [[.. valid, 0], valid[..^1], OodleTestStreams.Quantum(nested)];
        foreach (byte[] input in invalid)
        {
            Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress(input, output, workspace));
            Assert.Equal(before, output);
        }
        OodleCodec.Decompress(valid, output, workspace);
        Assert.All(output, value => Assert.Equal(42, value));
    }

    [Fact]
    public void Limits_null_and_disposed_workspaces_are_rejected()
    {
        using var workspace = new OodleWorkspace();
        byte[] output = new byte[2];
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress([], output, workspace, 1));
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress(new byte[4], output, workspace, 1));
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress(new byte[4], new byte[1], workspace, 1));
        Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress(new byte[1], Span<byte>.Empty, workspace));
        Assert.Throws<ArgumentOutOfRangeException>(() => OodleCodec.Decompress([], output, workspace, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OodleCodec.Decompress([], output, workspace, OodleCodec.MaximumBytes + 1));
        Assert.Throws<ArgumentNullException>(() => OodleCodec.Decompress([], output, null!));
        workspace.Dispose();
        workspace.Dispose();
        Assert.Throws<ObjectDisposedException>(() => OodleCodec.Decompress([], Span<byte>.Empty, workspace));
    }

    private static void Prime(OodleWorkspace workspace)
    {
        const int length = 8192;
        byte[] stream = OodleTestStreams.Quantum(OodleTestStreams.Entropy(3, [0xA5], length));
        OodleCodec.Decompress(stream, new byte[length], workspace);
    }
}
