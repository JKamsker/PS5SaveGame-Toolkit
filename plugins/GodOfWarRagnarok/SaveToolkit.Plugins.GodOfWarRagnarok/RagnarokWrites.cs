using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed record RagnarokCounter(string RowKey, string ValueId, int Offset, uint Imported,
    string Label, string Source, bool Resource = true);

/// <summary>Only decoded fixed-width counters are writable; import values are conservative ceilings.</summary>
internal sealed class RagnarokWrites
{
    public const string IntegrityWarning = "Integrity unknown — the game may reject this save.";
    public const string EvidenceWarning = "Not validated on console. Legal limits and gameplay dependencies are unknown; " +
        "the conservative range is 0..the imported value, not a proven game maximum. No derived values are guessed.";
    public const string RestoreActionId = "resources/restore-imported";
    private readonly byte[] _bytes;
    private readonly Dictionary<int, RagnarokCounter> _counters = [];
    public IReadOnlyCollection<RagnarokCounter> Counters => _counters.Values;

    public RagnarokWrites(byte[] bytes, RagnarokRecordIndex index, RagnarokReferences references)
    {
        _bytes = bytes;
        if (index.Preferences) return;

        Add(new("metadata/play-seconds", "metadata/play-seconds", RagnarokFormat.PlaySecondsOffset,
            RagnarokFormat.UInt32(bytes, RagnarokFormat.PlaySecondsOffset), "Play time (inferred)",
            "Continue? PC reader (Alexsander720, GPL-3.0), revision 51e19b2a: " +
            "https://github.com/Alexsander720/continue/blob/51e19b2a292c60f9acdf3c13c561809657104ea2/plugin/py_modules/ddsaves/god_of_war_ragnarok.py; " +
            "PS5 word and units inferred from the supplied corpus.", Resource: false));
        var wallets = index.Nested.Wallets.Count > 0 ? index.Nested.Wallets :
            [new RagnarokWallet(RagnarokFormat.HeroWalletId, index.WalletOffset, index.WalletCount, 0, 0)];
        foreach (var wallet in wallets)
        {
            var occurrences = new Dictionary<ulong, int>();
            for (var row = 0; row < wallet.ResourceCount; row++)
            {
                var offset = wallet.ResourcesOffset + row * RagnarokFormat.WalletRowSize;
                var id = RagnarokFormat.UInt64(bytes, offset);
                var occurrence = occurrences.GetValueOrDefault(id);
                occurrences[id] = occurrence + 1;
                var key = $"wallet/{wallet.Id:X16}/resource/{id:X16}/{occurrence}";
                Add(new(key, key + "/quantity", offset + sizeof(ulong),
                    RagnarokFormat.UInt32(bytes, offset + sizeof(ulong)),
                    $"{references.Container(wallet.Id)} · {references.Name(id)} · counter (inferred)", references.Attribution(id)));
            }
        }
    }

    public RagnarokCounter Counter(int offset) => _counters.TryGetValue(offset, out var counter)
        ? counter : throw new KeyNotFoundException("This byte range is not a decoded counter.");

    public uint Read(RagnarokCounter counter) => RagnarokFormat.UInt32(_bytes, counter.Offset);

    public string? Validate(RagnarokCounter counter, FieldValue value) =>
        value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > counter.Imported
            ? $"Enter an integer in the conservative imported range 0..{counter.Imported}." : null;

    public void Write(RagnarokCounter counter, FieldValue value)
    {
        if (Validate(counter, value) is { } error) throw new ArgumentException(error, nameof(value));
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(counter.Offset), value.AsUInt32());
    }

    public static ActionNode RestoreAction() => new(RestoreActionId, "Restore counters to imported values")
    {
        SupportsPlanning = true,
        Description = "Restore existing resource/XP/raven counters after reductions in this session. " +
            EvidenceWarning + " " + IntegrityWarning,
        Confirm = "Review the raw counters and skipped rows. " + IntegrityWarning,
    };

    public ActionPlan Plan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        ActionPlan Reject(string error) => new() { Errors = [new(actionId, error)], Warnings = [IntegrityWarning, EvidenceWarning] };
        if (actionId != RestoreActionId) return Reject("Unknown action.");
        if (parameters.Count != 0) return Reject("This action accepts no parameters.");
        if (scope.QueryScope is { } query &&
            (scope.RowKeys is null || query.CollectionId is not ("resources" or "xp" or "collectibles")))
            return Reject("Use resolved resource, XP or collectible row keys for this query scope.");

        var resources = Counters.Where(counter => counter.Resource).ToArray();
        var selected = scope.RowKeys?.ToHashSet(StringComparer.Ordinal);
        if (selected is not null && (selected.Count != scope.RowKeys!.Count ||
            selected.Except(resources.Select(counter => counter.RowKey), StringComparer.Ordinal).Any()))
            return Reject("Selection contains duplicate or unknown resource rows.");

        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var counter in resources.Where(counter => selected is null || selected.Contains(counter.RowKey)))
        {
            var current = Read(counter);
            if (current == counter.Imported)
                skipped.Add(new(counter.RowKey, "Already at the imported value (the conservative ceiling)."));
            else
                changes.Add(new(counter.ValueId, counter.Label, FieldValue.Of((long)current), FieldValue.Of((long)counter.Imported)));
        }

        return new() { Changes = changes, SkippedRows = skipped, Warnings = [IntegrityWarning, EvidenceWarning] };
    }

    private void Add(RagnarokCounter counter) => _counters.Add(counter.Offset, counter);
}
