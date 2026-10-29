using System.Buffers.Binary;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal static class FaithSaveIdentity
{
    public static bool IsSupportedFile(ISaveFile file) =>
        file.Directory.TitleId is "PPSA10665" or "PPSA13803" && IsSaveName(file.FileName);

    public static bool HasMatchingHeader(ISaveFile file)
    {
        var header = file.ReadHeader(FaithContainer.HeaderSize);
        if (header.Length < FaithContainer.HeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(header) != FaithContainer.HeaderSize)
        {
            return false;
        }

        var version = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(FaithContainer.VersionOffset));
        var expected = file.FileName == "system-save-data" ? FaithContainer.SystemVersion : FaithContainer.GameVersion;
        return version == expected;
    }

    private static bool IsSaveName(string name) => name is "game-save-data" or "system-save-data" ||
        (name.Length == "game-save-data00".Length && name.StartsWith("game-save-data0", StringComparison.Ordinal) &&
         name[^1] is >= '0' and <= '9');
}
