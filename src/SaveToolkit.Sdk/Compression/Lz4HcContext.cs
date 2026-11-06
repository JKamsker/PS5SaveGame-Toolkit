using System.Buffers.Binary;

namespace SaveToolkit.Sdk.Compression;

/// <summary>
/// The LZ4 HC match-finder state (upstream <c>LZ4_streamHC_t</c>) with every pointer replaced by a
/// signed position in one caller-owned window buffer and the two tables held as separate arrays.
/// Ported from K4os.Compression.LZ4 1.3.8 (MIT, Milosz Krajewski), itself a port of lz4 (BSD-2-Clause,
/// Yann Collet); see THIRD-PARTY-NOTICES. Algorithm names and arithmetic are kept to ease comparison.
/// </summary>
/// <remarks>
/// K4os keeps the 256 KiB tables and the scalar fields after them in one unmanaged struct. The Mono
/// WebAssembly interpreter encodes field offsets in 16 bits (dotnet/runtime#46622), so every field past
/// 64 KiB (the chain table, <c>end</c>, <c>base</c>, ...) aliases the hash table there and HC compression
/// reads and writes out of bounds. Nothing here has a field offset above a few dozen bytes.
/// </remarks>
internal sealed partial class Lz4HcContext
{
    internal const int MinMatch = 4;
    internal const int LastLiterals = 5;
    internal const int MfLimit = 12;
    internal const int MinLength = MfLimit + 1;
    internal const int MlBits = 4;
    internal const uint MlMask = (1U << MlBits) - 1;
    internal const uint RunMask = (1U << (8 - MlBits)) - 1;
    internal const int OptimalMl = (int)(MlMask - 1 + MinMatch);
    internal const int OptNum = 1 << 12;
    internal const int DistanceMax = 65535;
    internal const int MaxInputSize = 0x7E000000;
    internal const int MinLevel = 3;
    internal const int MaxLevel = 12;
    private const int HashLog = 15;
    private const int HashTableSize = 1 << HashLog;
    private const int ChainTableSize = 1 << 16;
    private const int Kb64 = 64 * 1024;
    // Positions are ints; streams stop well before upstream's 1 GiB table reset and 2 GiB reload.
    private const uint MaxStreamIndex = 1U << 30;

    private static readonly (bool Optimal, int NbSearches, int TargetLength)[] Levels =
    [
        (false, 2, 16), (false, 2, 16), (false, 2, 16), (false, 4, 16), (false, 8, 16), (false, 16, 16),
        (false, 32, 16), (false, 64, 16), (false, 128, 16), (false, 256, 16),
        (true, 96, 64), (true, 512, 128), (true, 16384, OptNum),
    ];

    private readonly uint[] _hashTable = new uint[HashTableSize];
    private readonly ushort[] _chainTable = new ushort[ChainTableSize];
    private readonly int _level;
    private Optimal[]? _optimal;
    private bool _initialized;
    private int _end;
    private int _base;
    private int _dictBase;
    private uint _dictLimit;
    private uint _lowLimit;
    private uint _nextToUpdate;

    internal Lz4HcContext(int level)
    {
        if (level is < MinLevel or > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level, "LZ4 HC levels are 3 to 12.");
        _level = level;
    }

    internal static int CompressBound(int length) => length is < 0 or > MaxInputSize ? 0 : length + length / 255 + 16;

    /// <summary>One-shot block compression (upstream <c>LZ4_compress_HC</c> on a fresh state).</summary>
    internal int CompressBlock(ReadOnlySpan<byte> source, Span<byte> target)
    {
        InitInternal(0);
        var limited = target.Length < CompressBound(source.Length);
        return CompressGeneric(source, 0, source.Length, target, limited);
    }

    /// <summary>Linked-block compression (upstream <c>LZ4_compress_HC_continue</c>).</summary>
    internal int Continue(ReadOnlySpan<byte> window, int source, int length, Span<byte> target)
    {
        if (!_initialized) InitInternal(source);
        if ((uint)(_end - _base) + (uint)length > MaxStreamIndex)
            throw new NotSupportedException("LZ4 HC streams longer than 1 GiB are not supported.");
        if (source != _end) SetExternalDict(window, source);
        // Check overlapping input/dictionary space.
        var sourceEnd = source + length;
        var dictBegin = _dictBase + (long)_lowLimit;
        var dictEnd = _dictBase + (long)_dictLimit;
        if (sourceEnd > dictBegin && source < dictEnd)
        {
            if (sourceEnd > dictEnd) sourceEnd = (int)dictEnd;
            _lowLimit = (uint)(sourceEnd - _dictBase);
            if (_dictLimit - _lowLimit < 4) _lowLimit = _dictLimit;
        }

        var limited = target.Length < CompressBound(length);
        return CompressGeneric(window, source, length, target, limited);
    }

    /// <summary>Moves up to 64 KiB of history to the start of the window (upstream <c>LZ4_saveDictHC</c>).</summary>
    internal int SaveDict(Span<byte> window, int dictSize)
    {
        var prefixSize = (int)(_end - (_base + (long)_dictLimit));
        if (dictSize > Kb64) dictSize = Kb64;
        if (dictSize < 4) dictSize = 0;
        if (dictSize > prefixSize) dictSize = prefixSize;
        window.Slice(_end - dictSize, dictSize).CopyTo(window);
        var endIndex = (uint)(_end - _base);
        _end = dictSize;
        _base = _end - (int)endIndex;
        _dictLimit = endIndex - (uint)dictSize;
        _lowLimit = endIndex - (uint)dictSize;
        if (_nextToUpdate < _dictLimit) _nextToUpdate = _dictLimit;
        return dictSize;
    }

    private int CompressGeneric(ReadOnlySpan<byte> window, int source, int length, Span<byte> target, bool limited)
    {
        if ((uint)length > MaxInputSize) return 0;
        _end += length;
        var level = Levels[_level];
        var run = new Run(this, window, target, limited);
        return level.Optimal
            ? run.CompressOptimal(source, length, level.NbSearches, (uint)level.TargetLength, _level == MaxLevel)
            : run.CompressHashChain(source, length, level.NbSearches);
    }

    // A fresh upstream state has end = (byte*)-1 and base = null, so the first init always clears the tables.
    private void InitInternal(int start)
    {
        Array.Clear(_hashTable);
        _chainTable.AsSpan().Fill(0xFFFF);
        const int startingOffset = Kb64;
        _nextToUpdate = startingOffset;
        _base = start - startingOffset;
        _end = start;
        _dictBase = start - startingOffset;
        _dictLimit = startingOffset;
        _lowLimit = startingOffset;
        _initialized = true;
    }

    private void SetExternalDict(ReadOnlySpan<byte> window, int newBlock)
    {
        if (_end >= _base + (long)_dictLimit + 4) Insert(window, _end - 3);
        _lowLimit = _dictLimit;
        _dictLimit = (uint)(_end - _base);
        _dictBase = _base;
        _base = newBlock - (int)_dictLimit;
        _end = newBlock;
        _nextToUpdate = _dictLimit;
    }

    internal Optimal[] OptimalBuffer()
    {
        _optimal ??= new Optimal[OptNum + 3];
        Array.Clear(_optimal);
        return _optimal;
    }

    private static uint HashPtr(ReadOnlySpan<byte> window, int position) =>
        unchecked(BinaryPrimitives.ReadUInt32LittleEndian(window[position..]) * 2654435761U) >> (MinMatch * 8 - HashLog);

    private void Insert(ReadOnlySpan<byte> window, int ip)
    {
        var target = (uint)(ip - _base);
        for (var idx = _nextToUpdate; idx < target; idx++)
        {
            var h = HashPtr(window, _base + (int)idx);
            var delta = unchecked(idx - _hashTable[h]);
            if (delta > DistanceMax) delta = DistanceMax;
            _chainTable[(ushort)idx] = (ushort)delta;
            _hashTable[h] = idx;
        }

        _nextToUpdate = target;
    }

    internal struct Optimal
    {
        public int Price;
        public int Off;
        public int Mlen;
        public int Litlen;
    }
}
