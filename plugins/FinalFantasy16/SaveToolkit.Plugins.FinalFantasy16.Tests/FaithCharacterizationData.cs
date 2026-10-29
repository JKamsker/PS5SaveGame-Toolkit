using System.Buffers.Binary;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal static class FaithCharacterizationData
{
    public static byte[] Combined(bool extended = false)
    {
        var player = Words(extended ? 1536 : 272, 100, 40);
        var skills = new byte[96];
        for (var index = 0; index < 68; index++) skills[index] = (byte)(index % 4);
        // Physical order deliberately differs from the decoder's published semantic order.
        return FaithTestData.Container(
            (27, 20210422, skills), (13, 20220104, Words(72, 200, 18)),
            (2, 2021112401, Words(168, 300, 6)), (7, 2021061001, Words(104, 400, 1)),
            (6, 2021110401, FaithTestData.Items()), (1, 202211161, player));
    }

    private static byte[] Words(int length, int first, int count)
    {
        var bytes = new byte[length];
        for (var index = 0; index < count; index++)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), first + index);
        return bytes;
    }
}
