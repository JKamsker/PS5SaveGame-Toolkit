using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Research;

internal static class RecordIdentities
{
    // Deliberately excludes mutable quantities, flags and _Hash (often a record checksum/version key).
    private static readonly string[] Keys = ["_ItemId", "_ItemID", "_ItemHash", "ItemId", "ID", "Id", "_ID", "_Id", "_Guid", "_GUID", "Guid", "_WeaponHash", "_ChapterHash", "_CheckPointHash", "_ObjectiveHash", "_EventType"];

    public static string[] ForArray(SaveNode array, byte[] bytes, out bool indexed)
    {
        var rows = array.Children;
        indexed = false;
        if (rows.Count == 0) return [];
        if (rows.All(r => r.Type == FieldTags.Class))
        {
            if (rows.Select(r => r.ClassHash).Distinct().Count() == rows.Count && rows.Count > 1)
                return rows.Select(r => $"[class={r.ClassHash:x8}]").ToArray();
            foreach (var name in Keys)
            {
                var fields = rows.Select(r => r.Children.FirstOrDefault(f => HashNames.All.GetValueOrDefault(f.Hash) == name)).ToArray();
                if (fields.Any(f => f is null || !(f.IsPrimitive || f.Type == FieldTags.Struct && f.Size == 16))) continue;
                var values = fields.Select(f => Key(f!, bytes)).ToArray();
                if (values.Any(v => v is null) || values.Distinct(StringComparer.Ordinal).Count() != rows.Count) continue;
                return fields.Select((f, i) => $"[key={f!.Hash:x8}:{values[i]}]").ToArray();
            }
        }
        // Primitive sets can be keyed by value; duplicates explicitly fall back to position.
        if (rows.All(r => r.IsPrimitive))
        {
            var values = rows.Select(r => PrimitiveCodec.Read(r, bytes)).ToArray();
            if (values.All(v => v.Kind == FieldValueKind.Integer) && values.Distinct().Count() == rows.Count)
                return values.Select(v => $"[value={v}]").ToArray();
        }
        indexed = true;
        return Enumerable.Range(0, rows.Count).Select(i => $"[index={i.ToString(CultureInfo.InvariantCulture)}]").ToArray();
    }

    private static string? Key(SaveNode node, byte[] bytes)
    {
        if (node.Type == FieldTags.Struct)
            return "opaque-sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(node.Offset, node.Size)));
        var value = PrimitiveCodec.Read(node, bytes);
        return value.Kind == FieldValueKind.Integer ? value.ToString() : null;
    }
}
