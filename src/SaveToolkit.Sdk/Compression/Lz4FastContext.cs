namespace SaveToolkit.Sdk.Compression;

/// <summary>
/// The LZ4 fast-level state (upstream <c>LZ4_stream_t</c>) with every pointer replaced by a signed
/// position in one caller-owned window buffer. Ported from the 64-bit engine of K4os.Compression.LZ4
/// 1.3.8 (<c>LL64</c>, MIT, Milosz Krajewski), itself a port of lz4 (BSD-2-Clause, Yann Collet); see
/// THIRD-PARTY-NOTICES. Algorithm names and arithmetic are kept to ease comparison.
/// </summary>
/// <remarks>
/// K4os picks its engine by pointer size, so on 32-bit WebAssembly it runs <c>LL32</c>: 4-byte instead of
/// 5-byte hashes for 32-bit index tables and pointer tables for large blocks. That output is valid LZ4
/// but differs from the 64-bit bytes the native host writes for every linked block and for independent
/// blocks of 64 KiB + 11 bytes and more, and K4os offers no way to force the 64-bit engine. This port
/// always computes the 64-bit variant, on every platform.
/// </remarks>
internal sealed partial class Lz4FastContext
{
    private const int HashLog = 12; // LZ4_MEMORY_USAGE (14) - 2
    private const int Limit64K = 64 * 1024 + (Lz4HcContext.MfLimit - 1);
    private const int SkipTrigger = 6;
    private const int Kb64 = 64 * 1024;
    // Stands in for upstream's null dictionary pointer: below every window position.
    private const int NoDictionary = -1;
    // Positions are ints; streams stop well before upstream's 2 GiB index renormalisation.
    private const uint MaxStreamIndex = 1U << 30;

    // byU32 uses the first 4096 entries; byU16 (independent blocks under 64 KiB) uses 8192 entries,
    // the same 16 KiB that upstream reinterprets as 16-bit slots. Values stay below 65536 there.
    private readonly uint[] _hashTable = new uint[1 << (HashLog + 1)];
    private uint _currentOffset;
    private int _dictionary = NoDictionary;
    private uint _dictSize;

    private enum TableType { ByU32, ByU16 }

    private enum DictDirective { NoDict, WithPrefix64K, UsingExtDict }

    /// <summary>
    /// One-shot block compression on a fresh state (upstream <c>LZ4_compress_fast_extState</c> with
    /// acceleration 1, the only value <c>LZ4Codec.Encode</c> passes), as the 64-bit engine selects it.
    /// </summary>
    internal int CompressBlock(ReadOnlySpan<byte> source, Span<byte> target)
    {
        var limited = target.Length < Lz4HcContext.CompressBound(source.Length);
        var tableType = source.Length < Limit64K ? TableType.ByU16 : TableType.ByU32;
        return CompressGeneric(source, 0, source.Length, target, limited, tableType, DictDirective.NoDict, dictSmall: false);
    }

    /// <summary>Linked-block compression (upstream <c>LZ4_compress_fast_continue</c>, acceleration 1).</summary>
    internal int Continue(ReadOnlySpan<byte> window, int source, int inputSize, Span<byte> target)
    {
        if (_currentOffset + (ulong)(uint)inputSize > MaxStreamIndex)
            throw new NotSupportedException("LZ4 fast streams longer than 1 GiB are not supported.");
        var dictEnd = _dictionary + (int)_dictSize;

        // A dictionary of 1 to 3 bytes that does not precede the block is useless (intentional underflow).
        if (unchecked(_dictSize - 1U) < 4 - 1 && dictEnd != source)
        {
            _dictSize = 0;
            _dictionary = source;
            dictEnd = source;
        }

        // Check overlapping input/dictionary space.
        var sourceEnd = source + inputSize;
        if (sourceEnd > _dictionary && sourceEnd < dictEnd)
        {
            _dictSize = (uint)(dictEnd - sourceEnd);
            if (_dictSize > Kb64) _dictSize = Kb64;
            if (_dictSize < 4) _dictSize = 0;
            _dictionary = dictEnd - (int)_dictSize;
        }

        var dictSmall = _dictSize < Kb64 && _dictSize < _currentOffset;
        if (dictEnd == source)
        {
            return CompressGeneric(window, source, inputSize, target, limited: true, TableType.ByU32,
                DictDirective.WithPrefix64K, dictSmall);
        }

        var result = CompressGeneric(window, source, inputSize, target, limited: true, TableType.ByU32,
            DictDirective.UsingExtDict, dictSmall);
        _dictionary = source;
        _dictSize = (uint)inputSize;
        return result;
    }

    /// <summary>Moves up to 64 KiB of history to the start of the window (upstream <c>LZ4_saveDict</c>).</summary>
    internal int SaveDict(Span<byte> window, int dictSize)
    {
        var previousDictEnd = _dictionary + (int)_dictSize;
        if ((uint)dictSize > Kb64) dictSize = Kb64;
        if ((uint)dictSize > _dictSize) dictSize = (int)_dictSize;
        if (dictSize > 0) window.Slice(previousDictEnd - dictSize, dictSize).CopyTo(window);
        _dictionary = 0;
        _dictSize = (uint)dictSize;
        return dictSize;
    }
}
