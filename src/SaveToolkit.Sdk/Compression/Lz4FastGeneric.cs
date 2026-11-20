using System.Buffers.Binary;

namespace SaveToolkit.Sdk.Compression;

internal sealed partial class Lz4FastContext
{
    private const int MinMatch = Lz4HcContext.MinMatch;
    private const int LastLiterals = Lz4HcContext.LastLiterals;
    private const int MfLimit = Lz4HcContext.MfLimit;
    private const int MinLength = Lz4HcContext.MinLength;
    private const int MlBits = Lz4HcContext.MlBits;
    private const uint MlMask = Lz4HcContext.MlMask;
    private const uint RunMask = Lz4HcContext.RunMask;
    private const int DistanceMax = Lz4HcContext.DistanceMax;

    /// <summary>
    /// Upstream <c>LZ4_compress_generic</c> for the paths K4os' fast encoders reach: acceleration 1, no
    /// <c>dictCtx</c> and no <c>fillOutput</c>. Positions are window offsets; every read and write is
    /// bounds-checked, so a logic error throws instead of touching foreign memory. Returns the encoded
    /// length, or 0 when a limited output cannot hold the block.
    /// </summary>
    private int CompressGeneric(ReadOnlySpan<byte> w, int source, int inputSize, Span<byte> t, bool limited,
        TableType tableType, DictDirective dictDirective, bool dictSmall)
    {
        var ip = source;
        var startIndex = _currentOffset;
        var @base = source - (int)startIndex;
        var dictionary = _dictionary;
        var dictSize = _dictSize;
        var maybeExtMem = dictDirective == DictDirective.UsingExtDict;
        var prefixIdxLimit = unchecked(startIndex - dictSize);
        var dictEnd = dictionary + (int)dictSize;
        var anchor = source;
        var iend = ip + inputSize;
        var mflimitPlusOne = iend - MfLimit + 1;
        var matchlimit = iend - LastLiterals;
        // A dictionary in the current context precedes the currentOffset.
        var dictBase = dictionary + (int)dictSize - (int)startIndex;
        var op = 0;
        long olimit = t.Length;
        uint offset = 0;
        uint forwardH;
        int match;
        int token;

        if ((uint)inputSize > Lz4HcContext.MaxInputSize) return 0;
        if (tableType == TableType.ByU16 && inputSize >= Limit64K) return 0;
        var lowLimit = source - (dictDirective == DictDirective.WithPrefix64K ? (int)dictSize : 0);

        // Update context state.
        _dictSize += (uint)inputSize;
        _currentOffset += (uint)inputSize;

        if (inputSize < MinLength) goto LastLiteralsLabel;

        // First byte.
        PutPosition(w, ip, tableType, @base);
        ip++;
        forwardH = HashPosition(w, ip, tableType);

        // Main loop.
        for (;;)
        {
            // Find a match (byU32, byU16).
            {
                var forwardIp = ip;
                var step = 1;
                var searchMatchNb = 1 << SkipTrigger;
                do
                {
                    var h = forwardH;
                    var current = (uint)(forwardIp - @base);
                    var matchIndex = _hashTable[h];
                    ip = forwardIp;
                    forwardIp += step;
                    step = searchMatchNb++ >> SkipTrigger;

                    if (forwardIp > mflimitPlusOne) goto LastLiteralsLabel;

                    if (dictDirective == DictDirective.UsingExtDict)
                    {
                        if (matchIndex < startIndex)
                        {
                            match = dictBase + (int)matchIndex;
                            lowLimit = dictionary;
                        }
                        else
                        {
                            match = @base + (int)matchIndex;
                            lowLimit = source;
                        }
                    }
                    else
                    {
                        match = @base + (int)matchIndex;
                    }

                    forwardH = HashPosition(w, forwardIp, tableType);
                    PutIndexOnHash(current, h, tableType);

                    if (dictSmall && matchIndex < prefixIdxLimit) continue;
                    if (tableType != TableType.ByU16 && matchIndex + DistanceMax < current) continue;

                    if (Peek4(w, match) == Peek4(w, ip))
                    {
                        if (maybeExtMem) offset = current - matchIndex;
                        break;
                    }
                }
                while (true);
            }

            // Catch up.
            while (ip > anchor && match > lowLimit && w[ip - 1] == w[match - 1])
            {
                ip--;
                match--;
            }

            // Encode literals.
            {
                var litLength = (uint)(ip - anchor);
                token = op++;
                if (limited && op + (long)litLength + (2 + 1 + LastLiterals) + litLength / 255 > olimit) return 0;
                if (litLength >= RunMask)
                {
                    var len = (int)(litLength - RunMask);
                    t[token] = (byte)(RunMask << MlBits);
                    for (; len >= 255; len -= 255) t[op++] = 255;
                    t[op++] = (byte)len;
                }
                else
                {
                    t[token] = (byte)(litLength << MlBits);
                }

                w.Slice(anchor, (int)litLength).CopyTo(t[op..]);
                op += (int)litLength;
            }

        NextMatch:
            // Encode offset.
            BinaryPrimitives.WriteUInt16LittleEndian(t[op..], maybeExtMem ? (ushort)offset : (ushort)(ip - match));
            op += 2;

            // Encode match length.
            {
                uint matchCode;
                if (dictDirective == DictDirective.UsingExtDict && lowLimit == dictionary)
                {
                    // Match within the external dictionary; it may continue into the block.
                    var limit = ip + (dictEnd - match);
                    if (limit > matchlimit) limit = matchlimit;
                    matchCode = Count(w, ip + MinMatch, match + MinMatch, limit);
                    ip += (int)matchCode + MinMatch;
                    if (ip == limit)
                    {
                        var more = Count(w, limit, source, matchlimit);
                        matchCode += more;
                        ip += (int)more;
                    }
                }
                else
                {
                    matchCode = Count(w, ip + MinMatch, match + MinMatch, matchlimit);
                    ip += (int)matchCode + MinMatch;
                }

                if (limited && op + (long)(1 + LastLiterals) + (matchCode + 240) / 255 > olimit) return 0;

                if (matchCode >= MlMask)
                {
                    t[token] += (byte)MlMask;
                    matchCode -= MlMask;
                    // Upstream writes 0xFF in 4-byte strides; the bytes it keeps are these.
                    for (var run = matchCode / 255; run > 0; run--) t[op++] = 255;
                    t[op++] = (byte)(matchCode % 255);
                }
                else
                {
                    t[token] += (byte)matchCode;
                }
            }

            anchor = ip;

            // Test end of chunk.
            if (ip >= mflimitPlusOne) break;

            // Fill table.
            PutPosition(w, ip - 2, tableType, @base);

            // Test next position.
            {
                var h = HashPosition(w, ip, tableType);
                var current = (uint)(ip - @base);
                var matchIndex = _hashTable[h];
                if (dictDirective == DictDirective.UsingExtDict)
                {
                    if (matchIndex < startIndex)
                    {
                        match = dictBase + (int)matchIndex;
                        lowLimit = dictionary;
                    }
                    else
                    {
                        match = @base + (int)matchIndex;
                        lowLimit = source;
                    }
                }
                else
                {
                    match = @base + (int)matchIndex;
                }

                PutIndexOnHash(current, h, tableType);
                if ((!dictSmall || matchIndex >= prefixIdxLimit)
                    && (tableType == TableType.ByU16 || matchIndex + DistanceMax >= current)
                    && Peek4(w, match) == Peek4(w, ip))
                {
                    token = op++;
                    t[token] = 0;
                    if (maybeExtMem) offset = current - matchIndex;
                    goto NextMatch;
                }
            }

            // Prepare next loop.
            forwardH = HashPosition(w, ++ip, tableType);
        }

    LastLiteralsLabel:
        {
            var lastRun = (uint)(iend - anchor);
            if (limited && op + (long)lastRun + 1 + (lastRun + 255 - RunMask) / 255 > olimit) return 0;
            if (lastRun >= RunMask)
            {
                var accumulator = lastRun - RunMask;
                t[op++] = (byte)(RunMask << MlBits);
                for (; accumulator >= 255; accumulator -= 255) t[op++] = 255;
                t[op++] = (byte)accumulator;
            }
            else
            {
                t[op++] = (byte)(lastRun << MlBits);
            }

            w.Slice(anchor, (int)lastRun).CopyTo(t[op..]);
            op += (int)lastRun;
        }

        return op;
    }

    private static uint Peek4(ReadOnlySpan<byte> w, int position) => BinaryPrimitives.ReadUInt32LittleEndian(w[position..]);

    /// <summary>Upstream <c>LZ4_count</c>: the common prefix of the two positions, up to <paramref name="inLimit"/>.</summary>
    private static uint Count(ReadOnlySpan<byte> w, int pIn, int pMatch, int inLimit)
    {
        var length = inLimit - pIn;
        return length <= 0 ? 0 : (uint)w.Slice(pIn, length).CommonPrefixLength(w.Slice(pMatch, length));
    }

    /// <summary>
    /// The 64-bit engine's <c>LZ4_hashPosition</c>: <c>LZ4_hash5</c> of 8 bytes for 32-bit index tables and
    /// <c>LZ4_hash4</c> with one more bit for the 16-bit table (the 32-bit engine always uses <c>LZ4_hash4</c>).
    /// </summary>
    private static uint HashPosition(ReadOnlySpan<byte> w, int position, TableType tableType)
    {
        if (tableType == TableType.ByU16)
            return unchecked(Peek4(w, position) * 2654435761U) >> (MinMatch * 8 - (HashLog + 1));
        var sequence = BinaryPrimitives.ReadUInt64LittleEndian(w[position..]);
        return (uint)(unchecked((sequence << 24) * 889523592379UL) >> (64 - HashLog));
    }

    private void PutIndexOnHash(uint index, uint h, TableType tableType) =>
        _hashTable[h] = tableType == TableType.ByU16 ? (ushort)index : index;

    private void PutPosition(ReadOnlySpan<byte> w, int position, TableType tableType, int @base) =>
        PutIndexOnHash((uint)(position - @base), HashPosition(w, position, tableType), tableType);
}
