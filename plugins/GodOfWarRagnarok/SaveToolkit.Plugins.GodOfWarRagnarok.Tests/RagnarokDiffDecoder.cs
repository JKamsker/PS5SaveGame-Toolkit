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
        foreach (var span in index.Evidence.Spans)
        {
            builder.Add($"grammar/spans/{span.Kind}", "span", $"0x{span.Offset:X}+{span.Length} ({span.Count})", span.Confidence);
        }

        foreach (var candidate in index.Evidence.LengthCandidates)
        {
            if (!builder.Add($"grammar/length-candidate/{candidate.Offset:X}", "integer", candidate.Length.ToString(), candidate.Basis))
            {
                break;
            }
        }

        foreach (var hash in index.Evidence.HashCandidates)
        {
            if (!builder.Add($"hash/{hash.Role}[id={hash.Value:X16}]", "hash", $"0x{hash.Value:X16}", hash.Role))
            {
                break;
            }
        }

        foreach (var compression in index.Evidence.CompressionCandidates)
        {
            builder.Add($"compression/{compression.Offset:X}", "candidate", compression.Signature, compression.Note);
        }

        return builder.Build();
    }
}
