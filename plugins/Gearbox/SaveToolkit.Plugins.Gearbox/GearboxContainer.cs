using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed record GearboxContainer(byte[] Original, int PayloadOffset, int PayloadLength, bool Profile, GearboxSchema Schema)
{
    public const int MaximumFileSize = 32 * 1024 * 1024;
    public string RootType => Profile ? "Profile" : "Character";

    public static GearboxContainer Parse(byte[] bytes, string titleId, bool profile)
    {
        if (bytes.Length > MaximumFileSize) throw new SaveFormatException("Gearbox save exceeds the 32 MiB safety limit.");
        var reader = new GvasReader(bytes);
        if (!reader.Take(4).SequenceEqual("GVAS"u8) || reader.Int32() != 2)
            throw new SaveFormatException("Expected a version 2 Gearbox GVAS save.");
        _ = reader.Int32(); // Package version is retained, not interpreted.
        if (reader.UInt16() != 4 || reader.UInt16() != 20 || reader.UInt16() != 3)
            throw new SaveFormatException("Unsupported Gearbox engine version (expected 4.20.3).");
        _ = reader.Int32();
        var branch = reader.String();
        var schema = titleId switch
        {
            "PPSA01463" when branch.StartsWith("OAK-", StringComparison.Ordinal) => GearboxSchema.Bl3,
            "PPSA01493" when branch.StartsWith("DAFFODIL-", StringComparison.Ordinal) => GearboxSchema.Tina,
            _ => throw new SaveFormatException("Title ID and Gearbox build branch do not match a supported game."),
        };
        if (reader.Int32() != 3) throw new SaveFormatException("Unsupported GVAS custom-version format.");
        var count = reader.Int32();
        const int maximumCustomVersions = 1024;
        const int customVersionSize = 20;
        if (count is < 0 or > maximumCustomVersions) throw new SaveFormatException("Invalid GVAS custom-version count.");
        _ = reader.Take(count * customVersionSize);
        var saveType = reader.String();
        var expectedType = (schema.Wonderlands, profile) switch
        {
            (false, false) => "OakSaveGame", (false, true) => "BP_DefaultOakProfile_C",
            (true, false) => "BPSaveGame_Default_C", (true, true) => "OakProfile",
        };
        if (saveType != expectedType) throw new SaveFormatException("Gearbox save class does not match its file role.");
        var length = reader.Int32();
        if (length <= 0 || length > bytes.Length - reader.Position) throw new SaveFormatException("Invalid GVAS payload length.");
        return new GearboxContainer(bytes, reader.Position, length, profile, schema);
    }

    public ProtoMessage Decode()
    {
        var root = ProtoMessage.Parse(BogoCrypt.Decode(Original.AsSpan(PayloadOffset, PayloadLength), Profile), Schema, RootType);
        if (Profile ? !root.Fields.Any(f => f.Spec is not null) : string.IsNullOrEmpty(root.Child("player_class_data")?.Text("player_class_path")))
            throw new SaveFormatException("Gearbox payload is missing the expected game records.");
        return root;
    }

    public byte[] Encode(ProtoMessage root)
    {
        var payload = BogoCrypt.Encode(root.Encode(), Profile);
        var tailOffset = PayloadOffset + PayloadLength;
        var result = new byte[checked(PayloadOffset + payload.Length + Original.Length - tailOffset)];
        Original.AsSpan(0, PayloadOffset).CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(PayloadOffset - sizeof(int)), payload.Length);
        payload.CopyTo(result, PayloadOffset);
        Original.AsSpan(tailOffset).CopyTo(result.AsSpan(PayloadOffset + payload.Length));
        return result;
    }
}
