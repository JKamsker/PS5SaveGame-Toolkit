using System.Text.Json;
using System.Security.Cryptography;
using SaveToolkit.Plugins.Pragmata.Format;

namespace SaveToolkit.Plugins.Pragmata.Research;

internal sealed class SchemaCatalog : IDisposable
{
    public const string Source = "https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/rszpragmata.json";
    public const string LicenseUrl = "https://github.com/alphazolam/RE_RSZ/blob/871a1d5c4c7b81c60c966d73ee63f4c4413ab56e/LICENSE";
    public const string Sha256 = "67e5ed72e0e9efff0287214ad36737a8959d799aa1510b6b76591731a21f4df2";
    private readonly JsonDocument _schema;
    private readonly Dictionary<string, JsonElement> _classes;
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    public SchemaCatalog(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != Sha256)
            throw new ArgumentException("Schema does not match the pinned MIT RE_RSZ revision.");
        _schema = JsonDocument.Parse(bytes);
        _classes = new(StringComparer.Ordinal);
        foreach (var entry in _schema.RootElement.EnumerateObject())
        {
            var name = entry.Value.GetProperty("name").GetString()!;
            _classes.TryAdd(name, entry.Value);
            _names.Add(name);
            foreach (var field in entry.Value.GetProperty("fields").EnumerateArray())
                _names.Add(field.GetProperty("name").GetString()!);
        }
    }

    public IReadOnlyDictionary<string, string> ConfirmedNames() => HashNames.All
        .Where(p => _names.Contains(p.Value)).OrderBy(p => p.Key)
        .ToDictionary(p => $"{p.Key:x8}", p => p.Value);

    public string? Name(uint hash) => HashNames.All.TryGetValue(hash, out var name) && _names.Contains(name) ? name : null;

    public string? DeclaredType(uint classHash, uint fieldHash)
    {
        var className = Name(classHash);
        var fieldName = Name(fieldHash);
        if (className is null || fieldName is null || !_classes.TryGetValue(className, out var entry)) return null;
        foreach (var field in entry.GetProperty("fields").EnumerateArray())
            if (field.GetProperty("name").GetString() == fieldName)
                return field.TryGetProperty("original_type", out var original) ? original.GetString() : field.GetProperty("type").GetString();
        return null;
    }

    public void Dispose() => _schema.Dispose();
}
