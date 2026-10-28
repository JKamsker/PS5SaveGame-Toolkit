namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>Synthetic syntax vectors, never a claim of legitimate game gear.</summary>
internal static class SyntheticItem
{
    public static byte[] Payload(bool wonderlands, int customizations = 0, bool invalidPart = false)
    {
        var db = SerialDatabase.For(wonderlands);
        var balance = db.Records("serial-indices").First(r => r.Category == "InventoryBalanceData"
            && db.Find("part-families", r.Name.ToLowerInvariant()) is not null);
        var partKey = db.Find("part-families", balance.Name.ToLowerInvariant())!.Name;
        List<(int Value, int Width)> fields = [(128, 8), (db.MaximumVersion, 7),
            (int.Parse(balance.Attributes["index"], System.Globalization.CultureInfo.InvariantCulture), db.Width("InventoryBalanceData", db.MaximumVersion)),
            (1, db.Width("InventoryData", db.MaximumVersion)), (1, db.Width("ManufacturerData", db.MaximumVersion)), (12, 7),
            (invalidPart ? 1 : 0, 6)];
        if (invalidPart) fields.Add((0, db.Width(partKey, db.MaximumVersion)));
        fields.AddRange([(0, 4), (0, 8), (customizations, 4), (3, 8)]);
        if (wonderlands) fields.Add((2, 7));
        var bytes = new byte[(fields.Sum(f => f.Width) + 7) / 8];
        var offset = 0;
        foreach (var (value, width) in fields)
            for (var bit = 0; bit < width; bit++, offset++)
                bytes[offset / 8] |= (byte)(((value >> bit) & 1) << (offset % 8));
        return bytes;
    }
}
