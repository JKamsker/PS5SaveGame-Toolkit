namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Adjusts a validated byte-sum checksum for a fixed-width word replacement.</summary>
internal static class AdditiveChecksum
{
    private const int BitsPerByte = 8;

    public static uint ReplaceWord(uint checksum, uint before, uint after) =>
        unchecked(checksum - SumBytes(before) + SumBytes(after));

    private static uint SumBytes(uint word)
    {
        uint sum = 0;
        for (var index = 0; index < sizeof(uint); index++)
        {
            sum += word & byte.MaxValue;
            word >>= BitsPerByte;
        }
        return sum;
    }
}
