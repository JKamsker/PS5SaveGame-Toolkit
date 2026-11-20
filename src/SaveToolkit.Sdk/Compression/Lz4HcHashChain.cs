using System.Buffers.Binary;

namespace SaveToolkit.Sdk.Compression;

internal sealed partial class Lz4HcContext
{
    private readonly ref partial struct Run
    {
        /// <summary>Levels 3 to 9 (upstream <c>LZ4HC_compress_hashChain</c>); returns the output length or 0.</summary>
        internal int CompressHashChain(int source, int inputSize, int maxNbAttempts)
        {
            var patternAnalysis = maxNbAttempts > 128; // levels 9+
            var ip = source;
            var anchor = ip;
            var iend = ip + inputSize;
            var mflimit = iend - MfLimit;
            var matchlimit = iend - LastLiterals;
            var op = 0;
            int ml, ml0, ml2, ml3;
            int start0, ref0;
            var @ref = 0;
            var start2 = 0;
            var ref2 = 0;
            var start3 = 0;
            var ref3 = 0;

            if (inputSize < MinLength) goto LastLiterals; // input too small, no compression (all literals)

            while (ip <= mflimit)
            {
                ml = InsertAndFindBestMatch(ip, matchlimit, ref @ref, maxNbAttempts, patternAnalysis);
                if (ml < MinMatch)
                {
                    ip++;
                    continue;
                }

                // Saved, in case we would skip too much.
                start0 = ip;
                ref0 = @ref;
                ml0 = ml;

            Search2:
                ml2 = ip + ml <= mflimit
                    ? InsertAndGetWiderMatch(ip + ml - 2, ip, matchlimit, ml, ref ref2, ref start2, maxNbAttempts,
                        patternAnalysis, chainSwap: false)
                    : ml;

                if (ml2 == ml)
                {
                    // No better match: encode ML1.
                    if (EncodeSequence(ref ip, ref op, ref anchor, ml, @ref)) return 0;
                    continue;
                }

                if (start0 < ip && start2 < ip + ml0)
                {
                    // Squeezing ML1 between ML0 (original ML1) and ML2.
                    ip = start0;
                    @ref = ref0;
                    ml = ml0;
                }

                if (start2 - ip < 3)
                {
                    // First match too small: removed.
                    ml = ml2;
                    ip = start2;
                    @ref = ref2;
                    goto Search2;
                }

            Search3:
                if (start2 - ip < OptimalMl)
                {
                    var newMl = ml;
                    if (newMl > OptimalMl) newMl = OptimalMl;
                    if (ip + newMl > start2 + ml2 - MinMatch) newMl = start2 - ip + ml2 - MinMatch;
                    var correction = newMl - (start2 - ip);
                    if (correction > 0)
                    {
                        start2 += correction;
                        ref2 += correction;
                        ml2 -= correction;
                    }
                }

                ml3 = start2 + ml2 <= mflimit
                    ? InsertAndGetWiderMatch(start2 + ml2 - 3, start2, matchlimit, ml2, ref ref3, ref start3,
                        maxNbAttempts, patternAnalysis, chainSwap: false)
                    : ml2;

                if (ml3 == ml2)
                {
                    // No better match: encode ML1 and ML2.
                    if (start2 < ip + ml) ml = start2 - ip;
                    if (EncodeSequence(ref ip, ref op, ref anchor, ml, @ref)) return 0;
                    ip = start2;
                    if (EncodeSequence(ref ip, ref op, ref anchor, ml2, ref2)) return 0;
                    continue;
                }

                if (start3 < ip + ml + 3)
                {
                    // Not enough space for match 2: remove it.
                    if (start3 >= ip + ml)
                    {
                        // Seq1 can be written now; Seq2 is removed, so Seq3 becomes Seq1.
                        if (start2 < ip + ml)
                        {
                            var correction = ip + ml - start2;
                            start2 += correction;
                            ref2 += correction;
                            ml2 -= correction;
                            if (ml2 < MinMatch)
                            {
                                start2 = start3;
                                ref2 = ref3;
                                ml2 = ml3;
                            }
                        }

                        if (EncodeSequence(ref ip, ref op, ref anchor, ml, @ref)) return 0;
                        ip = start3;
                        @ref = ref3;
                        ml = ml3;
                        start0 = start2;
                        ref0 = ref2;
                        ml0 = ml2;
                        goto Search2;
                    }

                    start2 = start3;
                    ref2 = ref3;
                    ml2 = ml3;
                    goto Search3;
                }

                // Three ascending matches: write the first one, ML1.
                if (start2 < ip + ml)
                {
                    if (start2 - ip < OptimalMl)
                    {
                        if (ml > OptimalMl) ml = OptimalMl;
                        if (ip + ml > start2 + ml2 - MinMatch) ml = start2 - ip + ml2 - MinMatch;
                        var correction = ml - (start2 - ip);
                        if (correction > 0)
                        {
                            start2 += correction;
                            ref2 += correction;
                            ml2 -= correction;
                        }
                    }
                    else
                    {
                        ml = start2 - ip;
                    }
                }

                if (EncodeSequence(ref ip, ref op, ref anchor, ml, @ref)) return 0;
                ip = start2; // ML2 becomes ML1
                @ref = ref2;
                ml = ml2;
                start2 = start3; // ML3 becomes ML2
                ref2 = ref3;
                ml2 = ml3;
                goto Search3;
            }

        LastLiterals:
            return WriteLastLiterals(anchor, iend, op);
        }

        /// <summary>Upstream <c>LZ4HC_encodeSequence</c>; true when the limited output would overflow.</summary>
        private bool EncodeSequence(ref int ip, ref int op, ref int anchor, int matchLength, int match)
        {
            var token = op++;
            var length = (uint)(ip - anchor);
            if (_limited && op + (long)(length / 255) + length + (2 + 1 + LastLiterals) > _t.Length) return true;
            if (length >= RunMask)
            {
                var len = length - RunMask;
                _t[token] = (byte)(RunMask << MlBits);
                for (; len >= 255; len -= 255) _t[op++] = 255;
                _t[op++] = (byte)len;
            }
            else
            {
                _t[token] = (byte)(length << MlBits);
            }

            _w.Slice(anchor, (int)length).CopyTo(_t[op..]);
            op += (int)length;
            BinaryPrimitives.WriteUInt16LittleEndian(_t[op..], (ushort)(ip - match));
            op += 2;

            length = (uint)matchLength - MinMatch;
            if (_limited && op + (long)(length / 255) + (1 + LastLiterals) > _t.Length) return true;
            if (length >= MlMask)
            {
                _t[token] += (byte)MlMask;
                length -= MlMask;
                for (; length >= 510; length -= 510)
                {
                    _t[op++] = 255;
                    _t[op++] = 255;
                }

                if (length >= 255)
                {
                    length -= 255;
                    _t[op++] = 255;
                }

                _t[op++] = (byte)length;
            }
            else
            {
                _t[token] += (byte)length;
            }

            ip += matchLength;
            anchor = ip;
            return false;
        }

        /// <summary>Writes the final literal run; 0 when a limited output cannot hold it.</summary>
        private int WriteLastLiterals(int anchor, int iend, int op)
        {
            var lastRunSize = (uint)(iend - anchor);
            var litLength = (lastRunSize + 255 - RunMask) / 255;
            var totalSize = 1 + litLength + lastRunSize;
            if (_limited && op + (long)totalSize > _t.Length) return 0;
            if (lastRunSize >= RunMask)
            {
                var accumulator = lastRunSize - RunMask;
                _t[op++] = (byte)(RunMask << MlBits);
                for (; accumulator >= 255; accumulator -= 255) _t[op++] = 255;
                _t[op++] = (byte)accumulator;
            }
            else
            {
                _t[op++] = (byte)(lastRunSize << MlBits);
            }

            _w.Slice(anchor, (int)lastRunSize).CopyTo(_t[op..]);
            return op + (int)lastRunSize;
        }
    }
}
