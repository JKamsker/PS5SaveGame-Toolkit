using Google.Protobuf;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Replace one packed/unpacked integer without normalizing unrelated wire encodings.</summary>
internal static class ProtoArrayEdits
{
    public static void Set(ProtoMessage message, string name, int index, int value)
    {
        if (index < 0 || value < 0) throw new SaveFormatException("Unsupported indexed allocation.");
        var position = 0;
        foreach (var record in message.All(name))
        {
            if (record.Value is ulong)
            {
                if (position++ == index) { record.Set((ulong)value); return; }
                continue;
            }
            if (record.Value is not byte[] bytes) throw new SaveFormatException("Unsupported array wire type.");
            using var reader = new CodedInputStream(bytes);
            while (!reader.IsAtEnd)
            {
                var start = checked((int)reader.Position);
                var current = reader.ReadInt32();
                var end = checked((int)reader.Position);
                if (position++ != index) continue;
                if (current == value) return; // Retain the original varint and enclosing length.
                using var output = new MemoryStream();
                output.Write(bytes.AsSpan(0, start));
                using (var writer = new CodedOutputStream(output, leaveOpen: true)) writer.WriteInt32(value);
                output.Write(bytes.AsSpan(end));
                record.Set(output.ToArray());
                return;
            }
        }
        throw new SaveFormatException("The indexed allocation is absent; no entries were created.");
    }
}
