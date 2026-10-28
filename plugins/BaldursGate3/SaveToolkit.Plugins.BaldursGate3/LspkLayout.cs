namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Absolute wire offsets from LSLib's PackageFormat.cs, not CLR structure offsets.</summary>
internal static class LspkLayout
{
    internal const int Version = 4;
    internal const int FileListOffset = 8;
    internal const int FileListSize = 16;
    internal const int Flags = 20;
    internal const int Digest = 22;
    internal const int DigestSize = 16;
    internal const int PartCount = 38;
    internal const int Header15Size = 38;
    internal const int Header16Size = 40;
    internal const int FileNameSize = 256;
    internal const int Entry18Size = 272;
    internal const int Entry15Size = 296;
    internal const int FileListPreambleSize = 8;
    internal const int FileListCompressedSize = 4;
    internal const int EntryOffset = FileNameSize;
    internal const int Entry18OffsetHigh = 260;
    internal const int Entry18Part = 262;
    internal const int Entry18Flags = 263;
    internal const int EntryStoredSize = 264;
    internal const int Entry18ExpandedSize = 268;
    internal const int Entry15ExpandedSize = 272;
    internal const int Entry15Part = 280;
    internal const int Entry15Flags = 284;
    internal const int OffsetHighShift = 32;
    internal const byte SolidFlag = 0x04;
}
