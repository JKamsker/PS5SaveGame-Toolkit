using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal static class RagnarokInspection
{
    internal const string Prefix = "inspect/resource/";

    public static ActionNode Action(int offset) => new(Prefix + offset.ToString("X", CultureInfo.InvariantCulture), "Inspect resource")
    {
        Mutates = false,
        Description = "Read the decoded row, attribution and unknown dependencies without editing the save or history.",
    };

    public static ActionOutcome Inspect(RagnarokDocument document, RagnarokRecordIndex index, RagnarokReferences references,
        string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (parameters.Count != 0 || !int.TryParse(actionId.AsSpan(Prefix.Length), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out var offset))
            throw new ArgumentException("Resource inspection takes a decoded row offset and no parameters.");
        var wallet = index.GetResourceWallets().FirstOrDefault(wallet => offset >= wallet.ResourcesOffset &&
            offset < wallet.IdsOffset && (offset - wallet.ResourcesOffset) % RagnarokFormat.WalletRowSize == 0);
        if (index.Preferences || wallet is null) throw new KeyNotFoundException("Resource inspection requires a counted wallet row.");
        var id = document.ReadId(offset);
        return new()
        {
            Message = $"{references.Name(id)}; raw ID {RagnarokReferences.Key(id)}; {references.Container(wallet.Id)}; " +
                $"quantity word {document.ReadWord(offset + sizeof(ulong))}; metadata word {document.ReadWord(offset + RagnarokFormat.WalletRowMetadataOffset)}. " +
                $"{references.Attribution(id)} Legal maximum and gameplay dependencies are unknown. No completion or character ownership is inferred.",
            Warnings = [RagnarokWrites.IntegrityWarning, RagnarokWrites.EvidenceWarning],
        };
    }
}
