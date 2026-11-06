using System.Buffers.Binary;

namespace SaveToolkit.Sdk.Compression;

internal sealed partial class Lz4HcContext
{
    /// <summary>
    /// One compression call: the window (source plus linked history), the destination and the state.
    /// Positions are window offsets; a "virtual" match position below the window only yields an offset.
    /// Every read is bounds-checked, so a logic error throws instead of reading foreign memory.
    /// </summary>
    private readonly ref partial struct Run
    {
        private readonly Lz4HcContext _c;
        private readonly ReadOnlySpan<byte> _w;
        private readonly Span<byte> _t;
        private readonly bool _limited;

        internal Run(Lz4HcContext context, ReadOnlySpan<byte> window, Span<byte> target, bool limited)
        {
            _c = context;
            _w = window;
            _t = target;
            _limited = limited;
        }

        private uint Peek4(int position) => BinaryPrimitives.ReadUInt32LittleEndian(_w[position..]);

        private ushort Peek2(int position) => BinaryPrimitives.ReadUInt16LittleEndian(_w[position..]);

        /// <summary>Common prefix of the two positions, at most up to <paramref name="inLimit"/> (upstream <c>LZ4_count</c>).</summary>
        private int Count(int pIn, int pMatch, int inLimit)
        {
            var length = inLimit - pIn;
            return length <= 0 ? 0 : _w.Slice(pIn, length).CommonPrefixLength(_w.Slice(pMatch, length));
        }

        private int CountBack(int ip, int match, int iMin, int mMin)
        {
            var back = 0;
            var min = Math.Max(iMin - ip, mMin - match);
            while (back > min && _w[ip + back - 1] == _w[match + back - 1]) back--;
            return back;
        }

        /// <summary>Bytes from <paramref name="ip"/> that repeat the little-endian 4-byte pattern.</summary>
        private uint CountPattern(int ip, int iEnd, uint pattern)
        {
            var start = ip;
            while (ip < iEnd && _w[ip] == (byte)(pattern >> ((ip - start) & 3) * 8)) ip++;
            return (uint)(ip - start);
        }

        /// <summary>Bytes before <paramref name="ip"/> that repeat the pattern, last byte first.</summary>
        private uint ReverseCountPattern(int ip, int iLow, uint pattern)
        {
            var back = 0;
            while (ip - back > iLow && _w[ip - back - 1] == (byte)(pattern >> (3 - (back & 3)) * 8)) back++;
            return (uint)back;
        }

        private static uint RotatePattern(uint rotate, uint pattern)
        {
            var bits = (int)((rotate & 3) << 3);
            return bits == 0 ? pattern : (pattern << bits) | (pattern >> (32 - bits));
        }

        private static bool ProtectDictEnd(uint dictLimit, uint matchIndex) => unchecked(dictLimit - 1 - matchIndex) >= 3;

        private int InsertAndFindBestMatch(int ip, int iLimit, ref int matchpos, int maxNbAttempts, bool patternAnalysis)
        {
            var useless = ip;
            return InsertAndGetWiderMatch(ip, ip, iLimit, MinMatch - 1, ref matchpos, ref useless, maxNbAttempts,
                patternAnalysis, chainSwap: false);
        }

        private (int Len, int Off) FindLongerMatch(int ip, int iHighLimit, int minLen, int nbSearches)
        {
            var matchPtr = 0;
            var length = InsertAndGetWiderMatch(ip, ip, iHighLimit, minLen, ref matchPtr, ref ip, nbSearches,
                patternAnalysis: true, chainSwap: true);
            return length <= minLen ? (0, 0) : (length, ip - matchPtr);
        }

        // Upstream LZ4HC_InsertAndGetWiderMatch without the dictCtx and favorDecSpeed modes, which K4os
        // never enables for LZ4Codec.Encode or LZ4Stream.Encode.
        private int InsertAndGetWiderMatch(int ip, int iLowLimit, int iHighLimit, int longest, ref int matchpos,
            ref int startpos, int maxNbAttempts, bool patternAnalysis, bool chainSwap)
        {
            unchecked
            {
                var chainTable = _c._chainTable;
                var @base = _c._base;
                var dictLimit = _c._dictLimit;
                var lowPrefixPtr = @base + (int)dictLimit;
                var ipIndex = (uint)(ip - @base);
                var lowestMatchIndex = _c._lowLimit + (DistanceMax + 1) > ipIndex ? _c._lowLimit : ipIndex - DistanceMax;
                var dictBase = _c._dictBase;
                var lookBackLength = ip - iLowLimit;
                var nbAttempts = maxNbAttempts;
                var matchChainPos = 0U;
                var pattern = Peek4(ip);
                var repeat = 0; // 0 untested, 1 not a repeat, 2 confirmed
                var srcPatternLength = 0U;

                _c.Insert(_w, ip);
                var matchIndex = _c._hashTable[HashPtr(_w, ip)];
                while (matchIndex >= lowestMatchIndex && nbAttempts != 0)
                {
                    var matchLength = 0;
                    nbAttempts--;
                    if (matchIndex >= dictLimit)
                    {
                        var matchPtr = @base + (int)matchIndex;
                        if (Peek2(iLowLimit + longest - 1) == Peek2(matchPtr - lookBackLength + longest - 1) &&
                            Peek4(matchPtr) == pattern)
                        {
                            var back = lookBackLength != 0 ? CountBack(ip, matchPtr, iLowLimit, lowPrefixPtr) : 0;
                            matchLength = MinMatch + Count(ip + MinMatch, matchPtr + MinMatch, iHighLimit) - back;
                            if (matchLength > longest)
                            {
                                longest = matchLength;
                                matchpos = matchPtr + back;
                                startpos = ip + back;
                            }
                        }
                    }
                    else
                    {
                        var matchPtr = dictBase + (int)matchIndex;
                        if (Peek4(matchPtr) == pattern)
                        {
                            var dictStart = dictBase + (int)_c._lowLimit;
                            var vLimit = ip + (int)(dictLimit - matchIndex);
                            if (vLimit > iHighLimit) vLimit = iHighLimit;
                            matchLength = Count(ip + MinMatch, matchPtr + MinMatch, vLimit) + MinMatch;
                            if (ip + matchLength == vLimit && vLimit < iHighLimit)
                                matchLength += Count(ip + matchLength, lowPrefixPtr, iHighLimit);
                            var back = lookBackLength != 0 ? CountBack(ip, matchPtr, iLowLimit, dictStart) : 0;
                            matchLength -= back;
                            if (matchLength > longest)
                            {
                                longest = matchLength;
                                matchpos = @base + (int)matchIndex + back; // virtual position, for the offset only
                                startpos = ip + back;
                            }
                        }
                    }

                    if (chainSwap && matchLength == longest && matchIndex + (uint)longest <= ipIndex)
                    {
                        // Better match: follow the chain that reaches furthest back.
                        const int kTrigger = 4;
                        var distanceToNextMatch = 1U;
                        var end = longest - MinMatch + 1;
                        var step = 1;
                        var accel = 1 << kTrigger;
                        for (var pos = 0; pos < end; pos += step)
                        {
                            uint candidateDist = chainTable[(ushort)(matchIndex + (uint)pos)];
                            step = accel++ >> kTrigger;
                            if (candidateDist > distanceToNextMatch)
                            {
                                distanceToNextMatch = candidateDist;
                                matchChainPos = (uint)pos;
                                accel = 1 << kTrigger;
                            }
                        }

                        if (distanceToNextMatch > 1)
                        {
                            if (distanceToNextMatch > matchIndex) break; // avoid overflow
                            matchIndex -= distanceToNextMatch;
                            continue;
                        }
                    }

                    uint distNextMatch = chainTable[(ushort)matchIndex];
                    if (patternAnalysis && distNextMatch == 1 && matchChainPos == 0)
                    {
                        var matchCandidateIdx = matchIndex - 1;
                        if (repeat == 0)
                        {
                            if (((pattern & 0xFFFF) == (pattern >> 16)) & ((pattern & 0xFF) == (pattern >> 24)))
                            {
                                repeat = 2;
                                srcPatternLength = CountPattern(ip + sizeof(uint), iHighLimit, pattern) + sizeof(uint);
                            }
                            else
                            {
                                repeat = 1;
                            }
                        }

                        if (repeat == 2 && matchCandidateIdx >= lowestMatchIndex && ProtectDictEnd(dictLimit, matchCandidateIdx))
                        {
                            var extDict = matchCandidateIdx < dictLimit;
                            var matchPtr = (extDict ? dictBase : @base) + (int)matchCandidateIdx;
                            if (Peek4(matchPtr) == pattern)
                            {
                                if (!FollowPattern(ip, iHighLimit, pattern, ref matchIndex, ref longest, ref matchpos, ref startpos,
                                        matchPtr, matchCandidateIdx, extDict, lowestMatchIndex, lookBackLength, srcPatternLength))
                                    break;
                                continue;
                            }
                        }
                    }

                    matchIndex -= chainTable[(ushort)(matchIndex + matchChainPos)];
                }

                return longest;
            }
        }

        /// <summary>The repeated-pattern shortcut; false means the search ends (upstream <c>break</c>).</summary>
        private bool FollowPattern(int ip, int iHighLimit, uint pattern, ref uint matchIndex, ref int longest,
            ref int matchpos, ref int startpos, int matchPtr, uint matchCandidateIdx, bool extDict, uint lowestMatchIndex, int lookBackLength, uint srcPatternLength)
        {
            unchecked
            {
                var @base = _c._base;
                var dictBase = _c._dictBase;
                var dictLimit = _c._dictLimit;
                var lowPrefixPtr = @base + (int)dictLimit;
                var dictStart = dictBase + (int)_c._lowLimit;
                var iLimit = extDict ? dictBase + (int)dictLimit : iHighLimit;
                var forwardPatternLength = CountPattern(matchPtr + sizeof(uint), iLimit, pattern) + sizeof(uint);
                if (extDict && matchPtr + (long)forwardPatternLength == iLimit)
                    forwardPatternLength += CountPattern(lowPrefixPtr, iHighLimit, RotatePattern(forwardPatternLength, pattern));

                var lowestMatchPtr = extDict ? dictStart : lowPrefixPtr;
                var backLength = ReverseCountPattern(matchPtr, lowestMatchPtr, pattern);
                if (!extDict && matchPtr - (long)backLength == lowPrefixPtr && _c._lowLimit < dictLimit)
                    backLength += ReverseCountPattern(dictBase + (int)dictLimit, dictStart, RotatePattern((uint)-(int)backLength, pattern));

                // Limit backLength not to go further than lowestMatchIndex.
                backLength = matchCandidateIdx - Math.Max(matchCandidateIdx - backLength, lowestMatchIndex);
                var currentSegmentLength = backLength + forwardPatternLength;
                if (currentSegmentLength >= srcPatternLength && forwardPatternLength <= srcPatternLength)
                {
                    var newMatchIndex = matchCandidateIdx + forwardPatternLength - srcPatternLength;
                    matchIndex = ProtectDictEnd(dictLimit, newMatchIndex) ? newMatchIndex : dictLimit;
                    return true;
                }

                var farthest = matchCandidateIdx - backLength;
                if (!ProtectDictEnd(dictLimit, farthest))
                {
                    matchIndex = dictLimit;
                    return true;
                }

                matchIndex = farthest;
                if (lookBackLength != 0) return true;
                var maxMl = Math.Min(currentSegmentLength, srcPatternLength);
                if ((uint)longest < maxMl)
                {
                    if ((uint)(ip - @base) - matchIndex > DistanceMax) return false;
                    longest = (int)maxMl;
                    matchpos = @base + (int)matchIndex; // virtual position, for the offset only
                    startpos = ip;
                }

                uint distToNextPattern = _c._chainTable[(ushort)matchIndex];
                if (distToNextPattern > matchIndex) return false; // avoid overflow
                matchIndex -= distToNextPattern;
                return true;
            }
        }
    }
}
