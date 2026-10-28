using System.Collections.Frozen;
using System.Globalization;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed record ProtoSpec(string Name, string Type, bool Repeated)
{
    public int WireType => Type switch
    {
        "double" or "fixed64" or "sfixed64" => 1,
        "float" or "fixed32" or "sfixed32" => 5,
        "int32" or "int64" or "uint32" or "uint64" or "sint32" or "sint64" or "bool" or "enum" => 0,
        _ => 2,
    };
}

internal sealed class GearboxSchema
{
    private readonly FrozenDictionary<string, FrozenDictionary<int, ProtoSpec>> _messages;
    public IReadOnlyList<int> Experience { get; }
    public bool Wonderlands { get; }
    public static GearboxSchema Bl3 { get; } = new("Bl3", false);
    public static GearboxSchema Tina { get; } = new("Wonderlands", true);

    private GearboxSchema(string resource, bool wonderlands)
    {
        Wonderlands = wonderlands;
        var rows = ReadResource(resource).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r').Split('\t'));
        _messages = rows.GroupBy(r => r[0]).ToFrozenDictionary(g => g.Key,
            g => g.ToFrozenDictionary(r => int.Parse(r[1], CultureInfo.InvariantCulture), r => new ProtoSpec(r[2], r[3], r[4] == "repeated")));
        Experience = Array.AsReadOnly(ReadResource(resource + "Xp").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray());
    }

    public bool IsMessage(string type) => _messages.ContainsKey(type);
    public ProtoSpec? Find(string message, int tag) => _messages[message].GetValueOrDefault(tag);
    public int Tag(string message, string name) => _messages[message].Single(p => p.Value.Name == name).Key;
    public ProtoSpec Spec(string message, string name) => _messages[message][Tag(message, name)];
    public int Level(int experience) => Math.Max(1, Experience.TakeWhile(v => v <= experience).Count());

    private static string ReadResource(string name)
    {
        using var stream = typeof(GearboxSchema).Assembly.GetManifestResourceStream($"SaveToolkit.Plugins.Gearbox.Data.{name}.tsv")
            ?? throw new InvalidOperationException("Missing Gearbox reference data.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
