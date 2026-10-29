using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

/// <summary>F1 decoder hook for evidence reports; it exposes only bounded, proven fields and candidates.</summary>
internal sealed class RagnarokDiffDecoder : ISaveDecoder
{
    private readonly GodOfWarRagnarokPlugin _plugin = new();

    public string Id => "gow-ragnarok-grammar";

    public bool CanDecode(ISaveFile file)
    {
        try
        {
            return _plugin.Probe(file).IsMatch && !file.FileName.Equals("userpreferences", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes)
        {
            throw new InvalidDataException($"File is {file.Length} bytes; the decode limit is {limits.MaxFileBytes}.");
        }

        var bytes = file.ReadAllBytes();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        var builder = new SemanticTreeBuilder(Id, limits);
        builder.Add("header/kind", "integer", RagnarokFormat.UInt32(bytes, RagnarokFormat.KindOffset).ToString());
        builder.Add("header/revision", "integer", $"0x{RagnarokFormat.UInt32(bytes, RagnarokFormat.RevisionOffset):X8}");
        builder.Add("metadata/location", "text", index.Description);
        builder.Add("directory/count", "integer", index.DirectoryCount.ToString());
        builder.Add("secondary/count", "integer", index.SecondaryCount.ToString());
        builder.Add("wallet/count", "integer", index.WalletCount.ToString());
        foreach (var probe in RagnarokAuthentication.Probe(bytes, index))
        {
            builder.Add($"integrity/{probe.Algorithm}/{probe.Coverage}/digest", "unkeyed hypothesis", probe.Digest);
            builder.Add($"integrity/{probe.Algorithm}/{probe.Coverage}/matches", "count", probe.MatchingOffsets.Count.ToString());
        }

        AddWallets(builder, bytes, index.Nested.Wallets);
        AddFlags(builder, bytes, index.Nested.FlagGroups);
        AddGrammar(builder, index.Evidence);
        return builder.Build();
    }

    private static void AddWallets(SemanticTreeBuilder builder, ReadOnlySpan<byte> bytes, IReadOnlyList<RagnarokWallet> wallets)
    {
        foreach (var wallet in wallets)
        {
            var root = $"wallets/{wallet.Id:X16}";
            builder.Add($"{root}/counts", "counts", $"{wallet.ResourceCount}/{wallet.IdCount}/{wallet.IndexCount}");
            var occurrences = new Dictionary<ulong, int>();
            for (var row = 0; row < wallet.ResourceCount; row++)
            {
                var offset = wallet.ResourcesOffset + row * RagnarokFormat.WalletRowSize;
                var id = RagnarokFormat.UInt64(bytes, offset);
                var occurrence = occurrences.GetValueOrDefault(id);
                occurrences[id] = occurrence + 1;
                builder.Add($"{root}/resources/{id:X16}/{occurrence}", "inferred quantity", RagnarokFormat.UInt32(bytes, offset + sizeof(ulong)).ToString());
            }

            occurrences.Clear();
            for (var row = 0; row < wallet.IdCount; row++)
            {
                var id = RagnarokFormat.UInt64(bytes, wallet.IdsOffset + row * sizeof(ulong));
                var occurrence = occurrences.GetValueOrDefault(id);
                occurrences[id] = occurrence + 1;
                builder.Add($"{root}/ids/{id:X16}/{occurrence}", "unknown ID", $"0x{id:X16}");
            }

            for (var row = 0; row < wallet.IndexCount; row++)
            {
                builder.Add($"{root}/index-like/{row}", "unknown word", RagnarokFormat.UInt32(bytes, wallet.IndicesOffset + row * sizeof(uint)).ToString());
            }
        }
    }

    private static void AddFlags(SemanticTreeBuilder builder, ReadOnlySpan<byte> bytes, IReadOnlyList<RagnarokFlagGroup> groups)
    {
        foreach (var group in groups)
        {
            for (var row = 0; row < group.Count; row++)
            {
                var offset = group.RowsOffset + row * RagnarokFlagGroup.RowSize;
                var id = RagnarokFormat.UInt64(bytes, offset);
                builder.Add($"flags/{group.Id:X16}/{id:X16}/{row}", "unknown flags", Convert.ToHexString(bytes.Slice(offset + sizeof(ulong), sizeof(uint))));
            }
        }
    }

    private static void AddGrammar(SemanticTreeBuilder builder, RagnarokGrammarEvidence evidence)
    {
        foreach (var span in evidence.Spans)
        {
            // A kind can now occur in several containers. Offset paths are diagnostic, not semantic identity.
            builder.Add($"grammar/spans/{span.Kind}/{span.Offset:X}", "span", $"0x{span.Offset:X}+{span.Length} ({span.Count})", span.Confidence);
        }

        foreach (var candidate in evidence.LengthCandidates)
        {
            if (!builder.Add($"grammar/length-candidate/{candidate.Offset:X}", "integer", candidate.Length.ToString(), candidate.Basis))
            {
                break;
            }
        }

        foreach (var hash in evidence.HashCandidates)
        {
            if (!builder.Add($"hash/{hash.Role}[id={hash.Value:X16}]", "hash", $"0x{hash.Value:X16}", hash.Role))
            {
                break;
            }
        }

        foreach (var compression in evidence.CompressionCandidates)
        {
            builder.Add($"compression/{compression.Offset:X}", "candidate", compression.Signature, compression.Note);
        }
    }
}
