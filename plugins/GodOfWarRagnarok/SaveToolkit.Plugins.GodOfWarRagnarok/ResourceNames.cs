using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Text.Json;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal static class ResourceNames
{
    private static readonly FrozenDictionary<ulong, string> Names = Load();

    public static IEnumerable<ulong> Ids => Names.Keys;

    public static string Get(ulong id) => Names.GetValueOrDefault(id) ?? $"Unknown ID 0x{id:X16}";

    public static bool TryGet(ulong id, out string name) => Names.TryGetValue(id, out name!);

    private static FrozenDictionary<ulong, string> Load()
    {
        using var stream = typeof(ResourceNames).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.GodOfWarRagnarok.Resources.ResourceNames.json")
            ?? throw new InvalidOperationException("Embedded Ragnarök resource names are missing.");
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("Embedded Ragnarök resource names are invalid.");
        return entries.ToFrozenDictionary(pair => BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(pair.Key)), pair => pair.Value);
    }
}
