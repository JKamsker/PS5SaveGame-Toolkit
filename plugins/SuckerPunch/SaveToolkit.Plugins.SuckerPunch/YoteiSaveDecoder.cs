using System.Globalization;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.SuckerPunch;

internal static class YoteiSaveDecoder
{
    private const int RequiredDepth = 4;

    public static void Decode(byte[] bytes, SaveContainer container, ISaveDecodeSink sink)
    {
        sink.Warn("Yōtei named-record meanings are inferred; ownership, play time and checksum tail coverage remain unknown.");
        if (container.Kind == ContainerKind.YoteiEmpty) return;
        var state = YoteiState.Parse(bytes, container);
        if (sink.MaxDepth < RequiredDepth)
        {
            sink.MarkTruncated("Yōtei named record paths require a decode depth of at least 4.");
            return;
        }
        foreach (var row in state.Inventory.OrderBy(row => row.Name, StringComparer.Ordinal))
        {
            if (!Add(row.QuantityId, row.Quantity(bytes))) return;
            if (!Add($"yotei/inventory/{row.Name}/word0", YoteiFormat.Word(bytes, row.DataOffset))) return;
            if (!Add($"yotei/inventory/{row.Name}/byte4", bytes[row.DataOffset + sizeof(uint)])) return;
            if (row.Ammo && !Add(row.CopyId, YoteiFormat.Word(bytes, row.QuantityOffset + sizeof(uint)))) return;
        }
        foreach (var row in state.Statistics.OrderBy(row => row.Name, StringComparer.Ordinal))
            if (!Add("yotei/statistics/" + row.Name, YoteiFormat.Word(bytes, row.DataOffset))) return;
        foreach (var row in state.Gear.OrderBy(row => row.Name, StringComparer.Ordinal))
            for (var index = 0; index < 2; index++)
                if (!Add($"yotei/gear/{row.Name}/byte{index}", bytes[row.DataOffset + index])) return;
        foreach (var row in state.Registry.OrderBy(row => row.Name, StringComparer.Ordinal))
            for (var index = 0; index < YoteiFormat.RegistryTrailer / sizeof(uint); index++)
                if (!Add($"yotei/registry/{row.Name}/word{index}", YoteiFormat.Word(bytes, row.DataOffset + index * sizeof(uint)))) return;

        bool Add(string path, uint value) => sink.Add(path, "integer", value.ToString(CultureInfo.InvariantCulture));
    }
}
