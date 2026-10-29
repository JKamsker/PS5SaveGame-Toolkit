using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.FinalFantasy16;

/// <summary>Bounded F12 evidence paths for the fixed-width Faith save format.</summary>
internal sealed class FaithEvidenceDecoder : ISaveDecoderHook
{
    private const int MinimumPathDepth = 2;
    public string Id => "ffxvi-evidence";

    public bool CanDecode(ISaveFile file)
    {
        if (file.Directory.TitleId is not ("PPSA10665" or "PPSA13803") ||
            file.FileName is not ("game-save-data" or "system-save-data") &&
            (file.FileName.Length != "game-save-data00".Length ||
             !file.FileName.StartsWith("game-save-data0", StringComparison.Ordinal) || file.FileName[^1] is < '0' or > '9'))
        {
            return false;
        }

        var header = file.ReadHeader(FaithContainer.HeaderSize);
        if (header.Length < FaithContainer.HeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(header) != FaithContainer.HeaderSize)
        {
            return false;
        }

        var version = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(FaithContainer.VersionOffset));
        var expected = file.FileName == "system-save-data" ? FaithContainer.SystemVersion : FaithContainer.GameVersion;
        return version == expected;
    }

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > FaithContainer.MaximumFileSize)
        {
            throw new SaveFormatException("Faith save exceeds the supported 64 MiB limit.");
        }

        if (file.Length > sink.MaxFileBytes)
        {
            throw new InvalidDataException("FFXVI save exceeds the evidence decoder input bound.");
        }

        if (!CanDecode(file))
        {
            throw new SaveFormatException("This is not a recognised Final Fantasy XVI save file.");
        }

        if (sink.MaxDepth < MinimumPathDepth)
        {
            sink.MarkTruncated("FFXVI evidence paths require a decode depth of at least 2.");
            return;
        }

        var bytes = file.ReadAllBytes();
        var container = FaithContainer.Parse(bytes);
        var schema = FaithSchema.ForTitle(file.Directory.TitleId);
        var player = FaithSchema.Unique(container, FaithSchema.PlayerType, schema.SupportsPlayer);
        if (player is not null)
        {
            if (!AddInt(sink, "player/level", ReadInt(bytes, player.PayloadOffset)) ||
                !AddInt(sink, "player/xp", ReadInt(bytes, player.PayloadOffset + sizeof(int))) ||
                !AddInt(sink, "player/childLevel", ReadInt(bytes, player.PayloadOffset + FaithContextLayout.ChildLevelOffset)) ||
                !AddInt(sink, "player/childXp", ReadInt(bytes, player.PayloadOffset + FaithContextLayout.ChildXpOffset)))
            {
                return;
            }
        }

        var user = FaithSchema.Unique(container, FaithSchema.UserType, FaithSchema.SupportsUser);
        if (user is not null && !AddInt(sink, "user/playTime", ReadInt(bytes, user.PayloadOffset)))
        {
            return;
        }

        var inventory = FaithInventory.Parse(bytes, container);
        foreach (var item in inventory.Items)
        {
            var path = $"inventory/{item.Slot}";
            if (!AddInt(sink, path + "/item", ReadInt(bytes, item.Offset)) ||
                !AddInt(sink, path + "/quantity", ReadInt(bytes, item.Offset + sizeof(int))))
            {
                return;
            }
        }

        var party = FaithSchema.Unique(container, FaithSchema.PartyType, FaithSchema.SupportsParty);
        if (party is not null &&
            (!AddInt(sink, "party/scene", ReadInt(bytes, party.PayloadOffset)) ||
             !AddInt(sink, "party/map", ReadInt(bytes, party.PayloadOffset + FaithContextLayout.MapOffset)) ||
             !AddInt(sink, "party/id", ReadInt(bytes, party.PayloadOffset + FaithContextLayout.PartyIdOffset)) ||
             !AddInt(sink, "party/count", ReadInt(bytes, party.PayloadOffset + FaithContextLayout.PartyCountOffset))))
        {
            return;
        }

        var equipment = FaithSchema.Unique(container, FaithSchema.EquipmentType, FaithSchema.SupportsEquipment);
        if (equipment is not null)
        {
            for (var bank = 0; bank < FaithContextLayout.EquipmentBanks; bank++)
            for (var slot = 0; slot < FaithContextLayout.EquipmentSlots; slot++)
            {
                var offset = equipment.PayloadOffset + (bank * FaithContextLayout.EquipmentSlots + slot) * sizeof(int);
                if (!AddInt(sink, $"equipment/{bank}/{slot}/item", ReadInt(bytes, offset))) return;
            }
        }

        var skills = FaithSchema.Unique(container, FaithSchema.SkillType, FaithSchema.SupportsSkills);
        if (skills is not null)
        {
            for (var index = 0; index < FaithContextLayout.SkillRanks; index++)
            {
                if (!AddInt(sink, $"skills/ranks/{index}", bytes[skills.PayloadOffset + index])) return;
            }
        }
    }

    private static bool AddInt(ISaveDecodeSink sink, string path, int value) =>
        sink.Add(path, "integer", value.ToString(CultureInfo.InvariantCulture));

    private static int ReadInt(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
}
