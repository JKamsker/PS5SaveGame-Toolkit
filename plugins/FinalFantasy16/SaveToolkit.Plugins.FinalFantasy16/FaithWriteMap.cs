using System.Buffers.Binary;
using static SaveToolkit.Plugins.FinalFantasy16.FaithContextLayout;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16;

// Offsets adapted from the attributed MIT Nenkai template used by the context readers.
// Ranges were measured over all 70 gameplay occurrences; see research/write-ranges.json.
internal sealed class FaithWriteMap
{
    public const string Warning = "Not validated on console. Fixed-width layout is based on Nenkai's MIT FaithSaveFile_Sub.bt " +
        "and PS5 sample bytes. CRC32 is repaired, but the game may reject or reset edited state. " +
        "No quest, ownership, growth/stat, AP/refund, loadout or other dependent state is reconciled. ";
    private readonly Dictionary<string, FaithWritableField> _fields = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<int, int> _originalMainCounts;
    private readonly IReadOnlyDictionary<int, int> _originalSecondaryCounts;

    public FaithWriteMap(byte[] bytes, FaithContainer container, FaithSchema schema, FaithInventory inventory)
    {
        _originalMainCounts = inventory.Items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.Count());
        _originalSecondaryCounts = inventory.SecondaryItems.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.Count());
        if (container.IsSystem) return;
        var player = FaithSchema.Unique(container, FaithSchema.PlayerType, schema.SupportsPlayer);
        if (player is not null)
        {
            var p = player.PayloadOffset;
            Add("player/level", p, 0, schema.IsDemo ? FaithWriteLimits.DemoLevel : FaithWriteLimits.RetailLevel);
            Add("player/xp", p + sizeof(int), 0, schema.IsDemo ? FaithWriteLimits.DemoXp : FaithWriteLimits.RetailXp);
            Add("player/childLevel", p + ChildLevelOffset, 0, FaithWriteLimits.ChildLevel);
            Add("player/childXp", p + ChildXpOffset, 0, schema.IsDemo ? FaithWriteLimits.DemoChildXp : FaithWriteLimits.RetailChildXp);
            Add("context/hp", p + HpOffset, -1, schema.IsDemo ? FaithWriteLimits.DemoHp : FaithWriteLimits.RetailHp);
            Add("context/recovery", p + RecoveryOffset, -1, schema.IsDemo ? FaithWriteLimits.DemoRecovery : 0);
            Add("context/lb", p + LimitBreakOffset, schema.IsDemo ? 0 : -1, schema.IsDemo ? 0 : FaithWriteLimits.RetailLimitBreak);
            Add("context/currentSummon", p + CurrentSummonOffset, 0, SummonCount - 1); // Three fixed summon slots.
            Add("context/learnedSummons", p + LearnedSummonsOffset, 0, FaithWriteLimits.LearnedSummons);
            Add("context/revealedSummons", p + RevealedSummonsOffset, 0, FaithWriteLimits.RevealedSummons);
            Array("context/summons", p + SummonModesOffset, SummonCount, FaithWriteLimits.SummonId);
            Array("context/commands", p + CommandsOffset, CommandCount, schema.IsDemo ? FaithWriteLimits.DemoCommandId : FaithWriteLimits.RetailCommandId);
            Array("context/shortcuts", p + ShortcutOffset, ShortcutCount, FaithWriteLimits.ShortcutId, categories: ["consumable"]);
        }
        var party = FaithSchema.Unique(container, FaithSchema.PartyType, FaithSchema.SupportsParty);
        if (party is not null)
        {
            var p = party.PayloadOffset;
            Add("party/scene", p, 0, schema.IsDemo ? FaithWriteLimits.DemoScene : FaithWriteLimits.RetailScene);
            Add("party/map", p + MapOffset, 0, schema.IsDemo ? FaithWriteLimits.DemoMap : FaithWriteLimits.RetailMap);
            Add("party/id", p + PartyIdOffset, 0, schema.IsDemo ? FaithWriteLimits.DemoParty : FaithWriteLimits.RetailParty);
            Add("party/count", p + PartyCountOffset, 0, schema.IsDemo ? FaithWriteLimits.DemoMemberCount : FaithWriteLimits.RetailMemberCount);
        }
        var user = FaithSchema.Unique(container, FaithSchema.UserType, FaithSchema.SupportsUser);
        if (user is not null) Add("user/playTime", user.PayloadOffset, 0, schema.IsDemo ? FaithWriteLimits.DemoPlayTime : FaithWriteLimits.RetailPlayTime);
        var gear = FaithSchema.Unique(container, FaithSchema.EquipmentType, FaithSchema.SupportsEquipment);
        if (gear is not null)
            for (var bank = 0; bank < EquipmentBanks; bank++)
                Array($"equipment/{bank}", gear.PayloadOffset + bank * EquipmentBankSize, EquipmentSlots, FaithWriteLimits.EquipmentId,
                    "/item", ["weapon", "belt", "vambrace", "accessory"]);
        var skills = FaithSchema.Unique(container, FaithSchema.SkillType, FaithSchema.SupportsSkills);
        if (skills is not null)
        {
            ByteArray("skills/ranks", skills.PayloadOffset, SkillRanks, schema.IsDemo ? FaithWriteLimits.DemoRank : FaithWriteLimits.RetailRank);
            ByteArray("skills/new", skills.PayloadOffset + SkillRanks, FlagBytes, byte.MaxValue);
            ByteArray("skills/protect", skills.PayloadOffset + ProtectOffset, FlagBytes, byte.MaxValue);
        }
        foreach (var item in inventory.Items) Add($"inventory/{item.Slot}/item", item.Offset, 1, FaithWriteLimits.MainItemId);
        foreach (var item in inventory.SecondaryItems)
        {
            Add($"secondary/{item.Slot}/quantity", item.Offset + sizeof(int), 0, FaithWriteLimits.SecondaryQuantity);
            // ID zero remains opaque; changing it might change a sentinel into an allocated item.
            if (item.Id == 0) continue;
            Add($"secondary/{item.Slot}/item", item.Offset, 1, FaithWriteLimits.SecondaryItemId);
        }

        void Add(string id, int offset, int min, int max, IReadOnlyList<string>? categories = null) =>
            _fields.Add(id, new(bytes, offset, sizeof(int), min, max, categories));
        void Array(string prefix, int offset, int count, int max, string suffix = "", IReadOnlyList<string>? categories = null)
        {
            for (var i = 0; i < count; i++) Add($"{prefix}/{i}{suffix}", offset + i * sizeof(int), 0, max, categories);
        }
        void ByteArray(string prefix, int offset, int count, int max)
        {
            for (var i = 0; i < count; i++) _fields.Add($"{prefix}/{i}", new(bytes, offset + i, sizeof(byte), 0, max));
        }
    }

    public bool CanEdit => _fields.Count > 0;
    public FaithWritableField? Find(string id) => _fields.GetValueOrDefault(id);

    public IReadOnlyList<FieldError> ValidateIdentityBatch(byte[] bytes, FaithInventory inventory, IReadOnlyList<ValueEdit> edits)
    {
        var identityEdits = edits.Where(edit => edit.ValueId.EndsWith("/item", StringComparison.Ordinal) &&
            (edit.ValueId.StartsWith("inventory/", StringComparison.Ordinal) || edit.ValueId.StartsWith("secondary/", StringComparison.Ordinal))).ToArray();
        if (identityEdits.Length == 0) return [];
        var candidate = bytes.ToArray();
        foreach (var edit in identityEdits)
        {
            if (Find(edit.ValueId) is not { } field || edit.Value.Kind != FieldValueKind.Text) continue;
            // Normal field validation runs first; conversion here is safe.
            field.Write(candidate, edit.Value, reference: true);
        }
        foreach (var (bank, originalCounts) in new[] { (inventory.Items, _originalMainCounts), (inventory.SecondaryItems, _originalSecondaryCounts) })
        {
            var oldCounts = bank.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.Count());
            var duplicates = bank.GroupBy(item => BinaryPrimitives.ReadInt32LittleEndian(candidate.AsSpan(item.Offset)))
                .Any(group => group.Count() > 1 && group.Count() > Math.Max(oldCounts.GetValueOrDefault(group.Key), originalCounts.GetValueOrDefault(group.Key)));
            if (duplicates) return [new(identityEdits[0].ValueId, "Replacement would introduce duplicate item IDs in this bank.")];
        }
        return [];
    }
}
