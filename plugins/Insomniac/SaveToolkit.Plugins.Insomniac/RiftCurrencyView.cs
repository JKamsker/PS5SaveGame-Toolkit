using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>Read-only config-anchored counts, independent of campaign career statistics.
/// Path facts: Tkachov / Overstrike MT/v1.4.1 hashes.txt, accessed 2026-10-29.
/// See Resources/CurrencyFields.source.md for pins, attribution and PS5 evidence.</summary>
internal sealed class RiftCurrencyView(IReadOnlyList<InventoryBlock> blocks, bool supportedTitle)
{
    internal const string BoltConfig = "947dc517d343ad64";
    internal const string RaritaniumConfig = "95ffa4c1254a6f29";
    internal const string BoltId = "currency/bolts";
    internal const string RaritaniumId = "currency/raritanium";
    internal const string HashSource = "https://github.com/Tkachov/Overstrike/releases/tag/MT/v1.4.1";
    private const uint GenericItemType = 0x4E5C_E3CB;
    private const int MaxDiagnosticRecords = 5;
    private const string Attribution = "Tkachov / Overstrike MT/v1.4.1 hashes.txt, accessed 2026-10-29; " + HashSource + ". " +
        "No licence published; reused with attribution per owner decision 2026-10-28. " + DdlSections.Attribution;
    private const string Warning = "Read-only inferred inventory balance. Not calibrated against the HUD. " +
        "Game caps, accounting dependencies and save-body integrity are unknown.";

    internal bool HasInventory => supportedTitle && blocks.Count > 0;

    internal IReadOnlyList<ViewNode> Build(Func<string, string, FieldSpec, FieldValue, FieldNode> bind) =>
    [
        new TextNode("Stored currency counts are separate from career statistics. Missing or ambiguous records remain unresolved; they are not reported as zero."),
        Currency(BoltId, "Bolts (inventory candidate)", BoltConfig, "configs/pickup/pup_bolt_item.config", bind),
        Currency(RaritaniumId, "Raritanium (inventory candidate)", RaritaniumConfig, "configs/pickup/pup_raritanium_item.config", bind),
    ];

    private ViewNode Currency(string id, string label, string config, string path,
        Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        // Require a unique config identity across containers. Never choose an actor,
        // occurrence or ItemId by position, or manufacture a zero for an absent item.
        var matches = blocks.SelectMany(block => block.Records.Select(record => (Block: block, Record: record)))
            .Where(pair => pair.Record.Type == GenericItemType && pair.Record.ConfigBytes == config).ToArray();
        if (matches.Length == 0)
        {
            var opaque = blocks.SelectMany(block => block.Records.Select(record => (Block: block, Record: record)))
                .Where(pair => pair.Record.Type == GenericItemType && pair.Record.ConfigBytes is null).ToArray();
            var reason = "Unresolved: no supported generic-item record matches this config token. " +
                "This does not establish that the currency is absent or zero.";
            if (opaque.Length > 0)
                reason += FormattableString.Invariant($" Generic-item records with an undecoded payload layout: {opaque.Length}. ") +
                    "Their currency identity is unknown.";
            return Unresolved(label, path, config, reason, opaque);
        }
        if (matches.Length > 1)
            return Unresolved(label, path, config,
                FormattableString.Invariant($"Unresolved: {matches.Length} supported generic-item records match this config token; ") +
                "a unique record is required. No record is selected and counts are not summed. Actor/ownership mapping is unknown.", matches);
        if (matches[0].Record.Count is not { } count)
            return Unresolved(label, path, config, "Unresolved: the matching record has no decoded ItemCount.", matches);
        var match = matches[0];
        return bind(id, label, IntegerSpec.UInt32 with { ReadOnly = true }, FieldValue.Of(count)) with
        {
            Confidence = Confidence.Inferred,
            Description = $"Stored ItemCount for {path}; config token {config}, container {match.Block.Key}. " +
                Attribution,
            Warning = Warning,
        };
    }

    private static SectionNode Unresolved(string label, string path, string config, string reason,
        IReadOnlyList<(InventoryBlock Block, InventoryRecord Record)> records)
    {
        var details = new List<ViewNode> { new TextNode(reason) };
        foreach (var (block, record) in records.Take(MaxDiagnosticRecords))
            details.Add(new TextNode(FormattableString.Invariant(
                $"Container {block.Key}; record {record.Key}; type 0x{record.Type:X8}; payload at 0x{record.Offset:X}, {record.Size} bytes."))
                { Monospace = true });
        if (records.Count > MaxDiagnosticRecords)
            details.Add(new TextNode(FormattableString.Invariant(
                $"Showing the first {MaxDiagnosticRecords} of {records.Count} records; further identities are omitted from this summary.")));
        details.Add(new TextNode(Warning) { Tone = Tone.Warning });
        return new SectionNode(label, details)
        {
            Description = $"Inferred currency mapping: {path}; config token {config}. " + Attribution,
        };
    }
}
