using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>
/// Atomic Heart's observed SaveExtension fork. Stock 1.4 is a structural reference, not its schema.
/// Both class strings, native transforms, velocities, tags and unknown payloads stay in Plain.
/// </summary>
internal sealed class RecordReader(byte[] plain)
{
    private const int TransformBytes = 10 * sizeof(float);
    private const int VelocityBytes = 6 * sizeof(float);
    private readonly PropertyReader properties = new(plain);
    private readonly List<ObjectRecord> objects = [];
    private readonly List<LevelRecord> levels = [];
    private readonly Dictionary<string, int> identities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> recordPositions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> levelOccurrences = new(StringComparer.Ordinal);
    private int identityUnits;

    internal RecordIndex Read()
    {
        var reader = SaveExtensionBinary.Cursor(plain);
        var arrayLength = reader.Count(SaveExtensionFile.MaxBytes);
        if (arrayLength != reader.Remaining) throw new SaveFormatException("SlotData byte-array prefix does not match the decoded aggregate.");
        var root = properties.ReadSequence(ref reader);
        ReadObjectTerminator(ref reader);
        var subsystems = reader.Count(RecordIndex.MaxRecords);
        for (var index = 0; index < subsystems; index++) ReadRecord(ref reader, RecordKinds.Subsystem, "SlotData");
        ReadLevel(ref reader);
        var sublevels = reader.Count(RecordIndex.MaxLevels - 1);
        for (var index = 0; index < sublevels; index++) ReadLevel(ref reader);
        if (reader.Remaining != 0) throw new SaveFormatException("Unexpected bytes after Atomic Heart level records.");
        return new RecordIndex(plain, root, objects, levels);
    }

    private void ReadLevel(ref BoundedBinaryCursor reader)
    {
        var start = reader.Position;
        var name = reader.String(RecordIndex.MaxNameUnits);
        if (name.Length == 0) throw new SaveFormatException("Empty Atomic Heart level name.");
        var count = reader.Count(RecordIndex.MaxRecords);
        for (var index = 0; index < count; index++) ReadActor(ref reader, name);
        var occurrence = levelOccurrences.GetValueOrDefault(name);
        levelOccurrences[name] = occurrence + 1;
        // Hash the name/occurrence pair, not a suffixed name that could collide with a literal '#2'.
        var id = RecordIdentity.Compact("level", name + "\0" + occurrence);
        levels.Add(new LevelRecord(id, name, new(start, reader.Position - start), count));
    }

    private void ReadActor(ref BoundedBinaryCursor reader, string owner)
    {
        var actor = ReadRecord(ref reader, RecordKinds.Actor, owner);
        if (actor is null) return;
        ReadBit(ref reader); // bHiddenInGame
        ReadBit(ref reader); // bIsProcedural
        reader.TakeBytes(TransformBytes);
        var moving = reader.Int32();
        if (moving is not (0 or 1)) throw new SaveFormatException("Invalid actor velocity flag.");
        if (moving != 0) reader.TakeBytes(VelocityBytes);
        var count = reader.Count(RecordIndex.MaxRecords);
        for (var index = 0; index < count; index++)
        {
            var component = ReadRecord(ref reader, RecordKinds.Component, actor.Id);
            if (component is not null)
            {
                reader.TakeBytes(TransformBytes);
                Complete(component, reader.Position);
            }
        }
        Complete(actor, reader.Position);
    }

    private ObjectRecord? ReadRecord(ref BoundedBinaryCursor reader, string kind, string owner)
    {
        if (objects.Count >= RecordIndex.MaxRecords) throw new SaveFormatException("Too many Atomic Heart object records.");
        var start = reader.Position;
        var name = reader.String(RecordIndex.MaxNameUnits);
        if (name == "None") return null;
        if (name.Length == 0) throw new SaveFormatException("Empty Atomic Heart record name.");
        // The fork stores two FStrings, not a path followed by an assumed zero int32.
        // The second is sometimes populated even when the first is None.
        var classPath = reader.String(RecordIndex.MaxNameUnits);
        var classSubPath = reader.String(RecordIndex.MaxNameUnits);
        var length = reader.Count(reader.Remaining);
        var payload = new ByteSpan(reader.Position, length);
        reader.TakeBytes(length);
        var tagCount = reader.Count(RecordIndex.MaxCollection);
        var tags = new List<string>();
        for (var index = 0; index < tagCount; index++) tags.Add(reader.String(RecordIndex.MaxNameUnits));
        var classIdentity = $"{Uri.EscapeDataString(classPath)}|{Uri.EscapeDataString(classSubPath)}";
        var id = $"{owner}/{kind}/{classIdentity}/{Uri.EscapeDataString(name)}";
        var occurrence = identities.GetValueOrDefault(id);
        identities[id] = occurrence + 1;
        if (occurrence != 0) id += $"#{occurrence + 1}";
        identityUnits = checked(identityUnits + id.Length);
        if (identityUnits > RecordIndex.MaxIndexPathUnits) throw new SaveFormatException("Record identity text exceeds the index budget.");
        var cursor = SaveExtensionBinary.Cursor(plain, payload.Offset, payload.End);
        IReadOnlyList<string> prefixIds = [];
        if (classPath.EndsWith(InventoryClasses.Player, StringComparison.Ordinal))
        {
            var count = cursor.Count(RecordIndex.MaxCollection);
            var ids = new List<string>();
            for (var index = 0; index < count; index++) ids.Add(cursor.String(RecordIndex.MaxNameUnits));
            prefixIds = ids;
        }
        IReadOnlyList<TaggedProperty> members = [];
        // A payload may start with native state. Do not search arbitrary bytes for a plausible tag.
        var tagged = IsTaggedStart(cursor);
        if (tagged) members = properties.ReadSequence(ref cursor);
        if (cursor.Remaining >= sizeof(int) && tagged)
        {
            var end = cursor.Position;
            if (cursor.Int32() != 0) cursor.Position = end; // Native tail, retained without interpretation.
        }
        var record = new ObjectRecord(id, kind, owner, name, classPath, classSubPath,
            new(start, reader.Position - start), payload, tags, members,
            new(cursor.Position, cursor.Remaining), prefixIds);
        recordPositions.Add(id, objects.Count);
        objects.Add(record);
        return record;
    }

    private static bool IsTaggedStart(BoundedBinaryCursor probe)
    {
        try
        {
            var name = probe.String(RecordIndex.MaxNameUnits);
            if (name == "None") return true;
            var type = probe.String(RecordIndex.MaxNameUnits);
            return name.Length != 0 && type.EndsWith("Property", StringComparison.Ordinal);
        }
        catch (SaveFormatException) { return false; }
    }

    private void Complete(ObjectRecord record, int end)
    {
        var index = recordPositions[record.Id];
        objects[index] = record with { Span = new(record.Span.Offset, end - record.Span.Offset) };
    }

    private static void ReadBit(ref BoundedBinaryCursor reader)
    {
        if (reader.Byte() > 1) throw new SaveFormatException("Invalid actor flag.");
    }

    private static void ReadObjectTerminator(ref BoundedBinaryCursor cursor)
    {
        if (cursor.Int32() != 0) throw new SaveFormatException("Invalid SlotData UObject terminator.");
    }
}
