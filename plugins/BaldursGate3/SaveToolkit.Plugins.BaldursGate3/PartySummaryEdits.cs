using System.Globalization;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Patches existing summary number tokens, retaining every other original JSON byte.</summary>
internal sealed class PartySummaryEdits
{
    internal const int MaxSummaryBytes = 1024 * 1024;
    internal const int MaxCharacters = 128;
    internal const string CurrentXp = "Experience Points (Current level)";
    internal const string TotalXp = "Experience Points (Total)";
    // Factual cap: bg3.wiki contributors, https://bg3.wiki/wiki/Experience, retrieved 2026-10-28.
    // CC BY-SA 4.0: https://creativecommons.org/licenses/by-sa/4.0/; extracted only the number 12.
    private const long BaseGameMaxLevel = 12;
    internal const string Warning = "Not validated on console. Changes SaveInfo.json metadata only; live NewAge character values are unchanged. " +
        "When nested in an LSPK v18 package, its outer digest is repaired; standalone resources have no outer package digest. Other game consistency checks are unknown; the game may reject or overwrite the edited summary.";
    private readonly byte[] _original;
    private readonly Dictionary<string, Number> _numbers;
    private readonly Dictionary<string, long> _edits = new(StringComparer.Ordinal);

    private PartySummaryEdits(string entryName, byte[] original, IReadOnlyList<Number> numbers)
    {
        EntryName = entryName;
        _original = original;
        _numbers = numbers.ToDictionary(number => number.Id, StringComparer.Ordinal);
        foreach (var group in numbers.GroupBy(number => number.Name))
        {
            // XP meaning/limits are unproven: restrict to the nonnegative range observed in this summary.
            var max = group.Max(number => number.Original);
            if (group.Key == "Level") max = Math.Max(BaseGameMaxLevel, max);
            foreach (var number in group) number.Max = max;
        }
    }

    internal string EntryName { get; }
    internal bool HasFields => _numbers.Count > 0;
    internal bool HasEdits => _edits.Count > 0;
    internal Number? Find(string id) => _numbers.GetValueOrDefault(id);
    internal FieldValue Get(Number number) => FieldValue.Of(_edits.GetValueOrDefault(number.Id, number.Original));

    internal void Set(Number number, FieldValue value)
    {
        var next = value.AsInt64();
        BinaryData.Require(next >= number.Min && next <= number.Max, $"Summary value must be between {number.Min} and {number.Max}.");
        if (next == number.Original) _edits.Remove(number.Id);
        else _edits[number.Id] = next;
    }

    internal byte[] Write()
    {
        using var output = new MemoryStream();
        var position = 0;
        foreach (var number in _numbers.Values.Where(number => _edits.ContainsKey(number.Id)).OrderBy(number => number.Offset))
        {
            output.Write(_original.AsSpan(position, number.Offset - position));
            output.Write(Encoding.UTF8.GetBytes(_edits[number.Id].ToString(CultureInfo.InvariantCulture)));
            position = number.Offset + number.Length;
        }
        output.Write(_original.AsSpan(position));
        var bytes = output.ToArray();
        var reopened = Create([new DocumentEntry(EntryName, bytes, null)]);
        BinaryData.Require(reopened is not null && reopened._numbers.Count == _numbers.Count &&
            _numbers.Values.All(number => reopened.Find(number.Id)?.Original == Get(number).AsInt64()),
            "Edited SaveInfo.json failed summary validation.");
        return bytes;
    }

    internal static PartySummaryEdits? Create(IReadOnlyList<DocumentEntry> entries)
    {
        var matches = entries.Where(entry => entry.Name.Equals("SaveInfo.json", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches is not [var summary] || summary.Bytes.Length > MaxSummaryBytes) return null;
        try
        {
            var numbers = new List<Number>();
            var reader = new Utf8JsonReader(summary.Bytes, new JsonReaderOptions { MaxDepth = 32 });
            if (!reader.Read()) return null;
            ReadValue(ref reader, [], numbers);
            if (reader.Read()) return null;
            return new PartySummaryEdits(summary.Name, summary.Bytes, numbers);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // Optional unsupported/ambiguous JSON remains inspectable and is always preserved.
            return null;
        }
    }

    private static void ReadValue(ref Utf8JsonReader reader, List<string> path, List<Number> numbers)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString()!;
                if (!names.Add(name)) throw new InvalidOperationException("Ambiguous duplicate JSON property.");
                if (!reader.Read()) throw new JsonException();
                path.Add(name);
                ReadValue(ref reader, path, numbers);
                path.RemoveAt(path.Count - 1);
            }
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            var index = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                path.Add((index++).ToString(CultureInfo.InvariantCulture));
                ReadValue(ref reader, path, numbers);
                path.RemoveAt(path.Count - 1);
            }
        }
        else if (reader.TokenType == JsonTokenType.Number &&
            path is ["Active Party", "Characters", var index, "Level" or CurrentXp or TotalXp] &&
            int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var character) && character < MaxCharacters &&
            reader.TryGetInt64(out var value) && value >= (path[^1] == "Level" ? 1 : 0))
        {
            numbers.Add(new Number($"party/{index}/{path[^1]}", path[^1], checked((int)reader.TokenStartIndex),
                reader.ValueSpan.Length, value));
        }
    }

    internal sealed class Number(string id, string name, int offset, int length, long original)
    {
        internal string Id { get; } = id;
        internal string Name { get; } = name;
        internal int Offset { get; } = offset;
        internal int Length { get; } = length;
        internal long Original { get; } = original;
        internal long Min => Name == "Level" ? 1 : 0;
        internal long Max { get; set; }
    }
}
