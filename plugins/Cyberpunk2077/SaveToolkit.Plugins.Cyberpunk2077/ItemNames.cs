using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record ItemName(string Id, string Name, string Kind);

internal sealed class ItemNames
{
    private readonly Dictionary<ulong, ItemName> _items;

    public ItemNames()
    {
        using var stream = typeof(ItemNames).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.Cyberpunk2077.Resources.ItemNames.json")
            ?? throw new InvalidOperationException("Embedded Cyberpunk item names are missing.");
        var rows = JsonSerializer.Deserialize<ItemName[]>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("Embedded Cyberpunk item names are invalid.");
        _items = rows.ToDictionary(row => ulong.Parse(row.Id, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        foreach (var pair in _items)
        {
            if (TweakHash(pair.Value.Name) != pair.Key)
            {
                throw new InvalidOperationException($"Embedded TweakDB name hash mismatch for {pair.Value.Name}.");
            }
        }
    }

    public ItemName? Find(ulong id) => _items.GetValueOrDefault(id);

    public static ulong TweakHash(string name)
    {
        const uint polynomial = 0xEDB88320;
        var bytes = Encoding.UTF8.GetBytes(name);
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? polynomial : 0);
            }
        }

        return ~crc | ((ulong)bytes.Length << 32);
    }
}
