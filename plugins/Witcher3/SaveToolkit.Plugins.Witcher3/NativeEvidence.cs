using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Emits each native record family in order, stopping immediately when the sink declines.</summary>
internal sealed class NativeEvidence(ISaveDecodeSink sink)
{
    public bool Add(string path, string kind, object value)
    {
        if (path.Count(c => c == '/') > sink.MaxDepth)
        {
            sink.MarkTruncated($"Native identity depth limit {sink.MaxDepth} reached at '{path}'.");
            return false;
        }

        return sink.Add(path, kind, Convert.ToString(value, CultureInfo.InvariantCulture)!);
    }

    public bool Inventory(string root, IReadOnlyList<InventoryItem> items)
    {
        foreach (var item in items)
        {
            var path = $"{root}/inventory/{item.Id}";
            if (!Add($"{path}/name", "name", item.Name)) { return false; }
            if (!Add($"{path}/quantity", "integer", item.Quantity)) { return false; }
            if (!Add($"{path}/flags", "uint32", item.Flags)) { return false; }
            if (!Add($"{path}/slot", "name", item.Slot)) { return false; }
            if (!Add($"{path}/durability", "float32", item.Durability.ToString("R", CultureInfo.InvariantCulture))) { return false; }
            if (!Add($"{path}/length", "length", item.Size)) { return false; }
            if (!Add($"{path}/opaquePrefix", "sha256", item.OpaqueHash)) { return false; }
            if (!Add($"{path}/prefixNames", "names", string.Join("|", item.PrefixNames))) { return false; }
            if (!Add($"{path}/tags", "names", string.Join("|", item.Tags))) { return false; }
            if (!Add($"{path}/attributes", "raw-attributes", string.Join("|", item.Attributes.Select(a => $"{a.Name}:{a.RawValue:X8}:{a.Type}")))) { return false; }
        }

        return true;
    }

    public bool Abilities(string root, IReadOnlyList<AbilityArray> arrays, byte[] data, IReadOnlyList<string> names)
    {
        foreach (var array in arrays)
        {
            if (!Add($"{root}/abilities/{array.Name}/count", "count", array.Entries.Count)) { return false; }
            foreach (var entry in array.Entries)
            {
                if (entry.Record.Properties.Count > 0 && entry.Id.StartsWith("unnamed-slot-", StringComparison.Ordinal))
                {
                    sink.MarkTruncated($"{array.Name}/{entry.Id} has only a positional identity; record alignment is unproven.");
                }
                foreach (var property in entry.Record.Properties)
                {
                    // Resolve name-index values before comparing; unknown binary properties retain length/hash evidence.
                    var node = new Sav3Node(0, property.Offset, property.Bytes.Length, "property", property.Name,
                        property.Type, property.Offset + 8, property.Value.Length);
                    var value = property.Type.StartsWith('E') || property.Type == "CName"
                        ? AbilityReader.NameValue(property, names)
                        : ScalarReader.Read(node, data, names)?.Value.ToString() ??
                            $"{property.Value.Length} bytes SHA-256 {Convert.ToHexString(SHA256.HashData(property.Value))}";
                    if (!Add($"{root}/abilities/{array.Name}/{entry.Id}/{property.Name}", property.Type, value)) { return false; }
                }
            }
        }
        return true;
    }
}
