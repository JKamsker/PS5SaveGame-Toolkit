using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SaveToolkit.Plugins.Pragmata.Hashing;

namespace SaveToolkit.Plugins.Pragmata.Format;

internal static class HashNames
{
    public static FrozenDictionary<uint, string> All { get; } = Load();
    public static string Label(uint hash) => All.TryGetValue(hash, out var name) ? $"{name} ({hash:x8})" : $"0x{hash:x8}";

    private static FrozenDictionary<uint, string> Load()
    {
        using var resource = typeof(PragmataPlugin).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.Pragmata.Resources.HashNames.json") ?? throw new InvalidOperationException("Missing hash names resource.");
        var names = JsonSerializer.Deserialize<Dictionary<string, string>>(resource) ?? throw new InvalidOperationException("Invalid hash names resource.");
        var result = new Dictionary<uint, string>();
        foreach (var (key, name) in names)
        {
            var hash = uint.Parse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (Murmur3.Hash(Encoding.UTF8.GetBytes(name)) != hash)
                throw new InvalidOperationException($"Unconfirmed name in hash resource: {key}.");
            result.Add(hash, name);
        }
        return result.ToFrozenDictionary();
    }
}
