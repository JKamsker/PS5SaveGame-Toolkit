using System.Buffers.Binary;
using System.Diagnostics;

namespace SaveToolkit.Codecs.Tests;

public sealed class CodecSampleMutationTests(ITestOutputHelper output)
{
    private const int HeaderBytes = 48;
    private const int MutationsPerFrame = 8;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySampleFrameRejectsTruncationAndSurvivesBoundedMutations(bool hogwarts)
    {
        byte[] file = hogwarts
            ? SampleChunks.Read("PPSA01603", "ue4savegame.ps5.sav", "sdimg_HL0010")
            : SampleChunks.Read("PPSA10695", "ue4savegame.dpx.sav");
        int start = hogwarts ? SampleChunks.FindFrames(file, 0) : 2302;
        int[] groups = hogwarts ? [35, 33] : [14];
        var random = new Random(40);
        var timer = Stopwatch.StartNew();
        using var workspace = new OodleWorkspace();
        int mutations = 0;
        foreach (int frames in groups)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                Assert.True(timer.Elapsed < TimeSpan.FromMinutes(2), "Sample mutation work exceeded two minutes.");
                int compressed = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(start + 16)));
                int plain = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(start + 24)));
                byte[] seed = file.AsSpan(start + HeaderBytes, compressed).ToArray();
                byte[] decoded = OodleCodec.Decompress(seed, plain);
                Assert.Equal(decoded, OodleCodec.Decompress(OodleCodec.EncodeStored(decoded), plain));
                foreach (int cut in new[] { 0, 1, compressed / 2, compressed - 1 })
                    Assert.Throws<InvalidDataException>(() => OodleCodec.Decompress(seed.AsSpan(0, cut), plain));
                byte[] destination = new byte[plain];
                for (int mutation = 0; mutation < MutationsPerFrame; mutation++)
                {
                    byte[] changed = seed.ToArray();
                    changed[random.Next(changed.Length)] ^= (byte)random.Next(1, 256);
                    destination.AsSpan().Fill(0xA5);
                    try { OodleCodec.Decompress(changed, destination, workspace); }
                    catch (InvalidDataException) { Assert.All(destination, value => Assert.Equal(0xA5, value)); }
                    mutations++;
                }
                start += HeaderBytes + compressed;
            }
            if (hogwarts && frames == groups[0]) start = SampleChunks.FindFrames(file, start);
        }
        output.WriteLine($"{groups.Sum()} frames, {mutations} mutations, {timer.ElapsedMilliseconds} ms; seed 40.");
    }
}
