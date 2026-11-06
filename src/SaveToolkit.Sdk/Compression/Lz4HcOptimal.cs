namespace SaveToolkit.Sdk.Compression;

internal sealed partial class Lz4HcContext
{
    private readonly ref partial struct Run
    {
        private const int TrailingLiterals = 3;

        private static int LiteralsPrice(int litlen) =>
            litlen >= (int)RunMask ? litlen + 1 + (litlen - (int)RunMask) / 255 : litlen;

        private static int SequencePrice(int litlen, int mlen)
        {
            var price = 1 + 2 + LiteralsPrice(litlen); // token + 16-bit offset
            if (mlen >= (int)(MlMask + MinMatch)) price += 1 + (mlen - (int)(MlMask + MinMatch)) / 255;
            return price;
        }

        /// <summary>Levels 10 to 12 (upstream <c>LZ4HC_compress_optimal</c>); returns the output length or 0.</summary>
        internal int CompressOptimal(int source, int inputSize, int nbSearches, uint sufficientLen, bool fullUpdate)
        {
            // Upstream keeps this ~64 KiB price table on the stack.
            var opt = _c.OptimalBuffer();
            var ip = source;
            var anchor = ip;
            var iend = ip + inputSize;
            var mflimit = iend - MfLimit;
            var matchlimit = iend - LastLiterals;
            var op = 0;
            if (sufficientLen >= OptNum) sufficientLen = OptNum - 1;

            while (ip <= mflimit)
            {
                var llen = ip - anchor;
                int bestMlen, bestOff, cur, lastMatchPos;
                var firstMatch = FindLongerMatch(ip, matchlimit, MinMatch - 1, nbSearches);
                if (firstMatch.Len == 0)
                {
                    ip++;
                    continue;
                }

                if ((uint)firstMatch.Len > sufficientLen)
                {
                    // Good enough solution: immediate encoding.
                    if (EncodeSequence(ref ip, ref op, ref anchor, firstMatch.Len, ip - firstMatch.Off)) return 0;
                    continue;
                }

                // Set prices for the first positions (literals).
                for (var rPos = 0; rPos < MinMatch; rPos++) opt[rPos] = Literal(llen + rPos, LiteralsPrice(llen + rPos));
                // Set prices using the initial match.
                for (var mlen = MinMatch; mlen <= firstMatch.Len; mlen++)
                    opt[mlen] = new Optimal { Mlen = mlen, Off = firstMatch.Off, Litlen = llen, Price = SequencePrice(llen, mlen) };
                lastMatchPos = firstMatch.Len;
                AddTrailingLiterals(opt, lastMatchPos);

                // Check further positions.
                for (cur = 1; cur < lastMatchPos; cur++)
                {
                    var curPtr = ip + cur;
                    if (curPtr > mflimit) break;
                    // Not useful to search here if the next position has the same (or lower) cost.
                    if (fullUpdate
                            ? opt[cur + 1].Price <= opt[cur].Price && opt[cur + MinMatch].Price < opt[cur].Price + 3
                            : opt[cur + 1].Price <= opt[cur].Price)
                        continue;

                    var newMatch = FindLongerMatch(curPtr, matchlimit, fullUpdate ? MinMatch - 1 : lastMatchPos - cur, nbSearches);
                    if (newMatch.Len == 0) continue;
                    if ((uint)newMatch.Len > sufficientLen || newMatch.Len + cur >= OptNum)
                    {
                        // Immediate encoding.
                        bestMlen = newMatch.Len;
                        bestOff = newMatch.Off;
                        lastMatchPos = cur + 1;
                        goto Encode;
                    }

                    // Before the match: set prices with literals at the beginning.
                    var baseLitlen = opt[cur].Litlen;
                    for (var litlen = 1; litlen < MinMatch; litlen++)
                    {
                        var price = opt[cur].Price - LiteralsPrice(baseLitlen) + LiteralsPrice(baseLitlen + litlen);
                        var pos = cur + litlen;
                        if (price < opt[pos].Price) opt[pos] = Literal(baseLitlen + litlen, price);
                    }

                    // Set prices using the match at position cur.
                    for (var ml = MinMatch; ml <= newMatch.Len; ml++)
                    {
                        var pos = cur + ml;
                        int price, ll;
                        if (opt[cur].Mlen == 1)
                        {
                            ll = opt[cur].Litlen;
                            price = (cur > ll ? opt[cur - ll].Price : 0) + SequencePrice(ll, ml);
                        }
                        else
                        {
                            ll = 0;
                            price = opt[cur].Price + SequencePrice(0, ml);
                        }

                        if (pos > lastMatchPos + TrailingLiterals || price <= opt[pos].Price)
                        {
                            if (ml == newMatch.Len && lastMatchPos < pos) lastMatchPos = pos;
                            opt[pos] = new Optimal { Mlen = ml, Off = newMatch.Off, Litlen = ll, Price = price };
                        }
                    }

                    AddTrailingLiterals(opt, lastMatchPos);
                }

                bestMlen = opt[lastMatchPos].Mlen;
                bestOff = opt[lastMatchPos].Off;
                cur = lastMatchPos - bestMlen;

            Encode: // cur, lastMatchPos, bestMlen and bestOff are set
                var candidatePos = cur;
                var selectedMatchLength = bestMlen;
                var selectedOffset = bestOff;
                while (true)
                {
                    // From end to beginning.
                    var nextMatchLength = opt[candidatePos].Mlen; // 1 means a literal
                    var nextOffset = opt[candidatePos].Off;
                    opt[candidatePos].Mlen = selectedMatchLength;
                    opt[candidatePos].Off = selectedOffset;
                    selectedMatchLength = nextMatchLength;
                    selectedOffset = nextOffset;
                    if (nextMatchLength > candidatePos) break; // first match to encode
                    candidatePos -= nextMatchLength;
                }

                // Encode all recorded sequences in order.
                for (var rPos = 0; rPos < lastMatchPos;)
                {
                    var ml = opt[rPos].Mlen;
                    var offset = opt[rPos].Off;
                    if (ml == 1)
                    {
                        ip++;
                        rPos++;
                        continue;
                    }

                    rPos += ml;
                    if (EncodeSequence(ref ip, ref op, ref anchor, ml, ip - offset)) return 0;
                }
            }

            return WriteLastLiterals(anchor, iend, op);
        }

        private static Optimal Literal(int litlen, int price) => new() { Mlen = 1, Off = 0, Litlen = litlen, Price = price };

        private static void AddTrailingLiterals(Optimal[] opt, int lastMatchPos)
        {
            for (var addLit = 1; addLit <= TrailingLiterals; addLit++)
                opt[lastMatchPos + addLit] = Literal(addLit, opt[lastMatchPos].Price + LiteralsPrice(addLit));
        }
    }
}
