using System.Buffers.Binary;

namespace SaveToolkit.Codecs.Tests;

public sealed class OodleSampleAllocationTests(ITestOutputHelper output)
{
    private const int Warmups = 5;
    private const int BytesPerFrameBudget = 64 * 1024;
    private const int HeaderBytes = 48;

    [Theory]
    [InlineData(false, false, 14, 1751391)]
    [InlineData(true, false, 35, 4534280)]
    [InlineData(true, true, 33, 4231176)]
    public void Reused_workspace_decodes_sample_frames_with_bounded_allocations(
        bool hogwarts, bool secondary, int frameCount, int expectedLength)
    {
        byte[] file = hogwarts
            ? SampleChunks.Read("PPSA01603", "ue4savegame.ps5.sav", "sdimg_HL0010")
            : SampleChunks.Read("PPSA10695", "ue4savegame.dpx.sav");
        int start = hogwarts ? SampleChunks.FindFrames(file, 0) : 2302;
        if (secondary)
            start = SampleChunks.FindFrames(file, SampleChunks.DecodeFrames(file, start, 35).End);
        var expected = SampleChunks.DecodeFrames(file, start, frameCount);
        Assert.Equal(expectedLength, expected.Bytes.Length);
        Frame[] frames = ReadFrames(file, start, frameCount);
        byte[] destination = new byte[expectedLength];
        using var workspace = new OodleWorkspace();
        for (int i = 0; i < Warmups; i++)
            Decode(file, frames, destination, workspace);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Decode(file, frames, destination, workspace);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine($"{frameCount} frames / {expectedLength} output bytes: {allocated} allocated bytes.");
        Assert.InRange(allocated, 0, (long)frameCount * BytesPerFrameBudget);
        Assert.Equal(expected.Bytes, destination);
    }

    private static void Decode(byte[] file, Frame[] frames, byte[] destination, OodleWorkspace workspace)
    {
        int position = 0;
        foreach (Frame frame in frames)
        {
            OodleCodec.Decompress(file.AsSpan(frame.Offset, frame.CompressedBytes),
                destination.AsSpan(position, frame.OutputBytes), workspace);
            position += frame.OutputBytes;
        }
    }

    private static Frame[] ReadFrames(byte[] file, int start, int count)
    {
        var frames = new Frame[count];
        for (int i = 0; i < count; i++)
        {
            int compressed = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(start + 16)));
            int plain = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(start + 24)));
            frames[i] = new(start + HeaderBytes, compressed, plain);
            start += HeaderBytes + compressed;
        }
        return frames;
    }

    private readonly record struct Frame(int Offset, int CompressedBytes, int OutputBytes);
}
