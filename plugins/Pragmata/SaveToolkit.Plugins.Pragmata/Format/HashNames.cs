using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SaveToolkit.Plugins.Pragmata.Hashing;

namespace SaveToolkit.Plugins.Pragmata.Format;

internal static class HashNames
{
    internal const string Source = "https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json";
    internal const string Attribution = "alphazolam; praydog (RSZ dumps), MIT, retrieved 2026-10-28. " + Source;
    internal const string CommunityRoot = "https://github.com/kvasszn/ree-save-editor/blob/4125e497f1ae84cc746bb4a99da612c48bea161e/assets/pragmata/";
    internal const string CommunityLicense = "No licence published; reused with attribution per owner decision 2026-10-28";
    internal const string RestoredSource = CommunityRoot + "strings_pragmata.txt";
    internal const string RestoredAttribution = "kvasszn / REE Save Editor; praydog (dump tooling). " +
        CommunityLicense + ". Retrieved 2026-10-28. " + RestoredSource;
    private static readonly FrozenDictionary<uint, string> Restored = Load("RestoredHashNames");
    public static FrozenDictionary<uint, string> All { get; } = Load();
    public static string Label(uint hash) => All.TryGetValue(hash, out var name) ? $"{name} ({hash:x8})" : $"0x{hash:x8}";
    public static string Provenance(uint hash) => Restored.ContainsKey(hash)
        ? RestoredAttribution
        : All.ContainsKey(hash) ? Attribution : "Unmapped identifier; gameplay meaning unknown.";

    private static FrozenDictionary<uint, string> Load(string resourceName = "HashNames")
    {
        using var resource = typeof(PragmataPlugin).Assembly.GetManifestResourceStream(
            $"SaveToolkit.Plugins.Pragmata.Resources.{resourceName}.json") ?? throw new InvalidOperationException("Missing hash names resource.");
        var names = JsonSerializer.Deserialize<Dictionary<string, string>>(resource) ?? throw new InvalidOperationException("Invalid hash names resource.");
        var result = new Dictionary<uint, string>();
        foreach (var (key, name) in names)
        {
            var hash = uint.Parse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (Murmur3.Hash(Encoding.UTF8.GetBytes(name)) != hash)
                throw new InvalidOperationException($"Unconfirmed name in hash resource: {key}.");
            result.Add(hash, name);
        }
        if (resourceName == "HashNames")
            foreach (var (hash, name) in Restored) result.Add(hash, name);
        return result.ToFrozenDictionary();
    }
}
