using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>Reports changes to published read-only uint32 views over editable bytes.</summary>
internal sealed class AriseReadOnlyAliases(byte[] payload)
{
    private readonly Dictionary<string, Alias> _aliases = new(StringComparer.Ordinal);

    public void Register(string id, string label, int offset) => _aliases[id] = new(label, offset);

    public IReadOnlyDictionary<string, FieldValue> Capture() =>
        _aliases.ToDictionary(pair => pair.Key, pair => Read(pair.Value.Offset), StringComparer.Ordinal);

    public IReadOnlyList<ValueChange> Changes(IReadOnlyDictionary<string, FieldValue> before) =>
        before.Select(pair => new ValueChange(pair.Key, pair.Value, Read(_aliases[pair.Key].Offset)))
            .Where(change => change.OldValue != change.NewValue).ToArray();

    public IReadOnlyList<PlannedChange> Plan(IReadOnlyDictionary<int, byte> byteWrites)
    {
        var changes = new List<PlannedChange>();
        Span<byte> word = stackalloc byte[sizeof(uint)];
        foreach (var (id, alias) in _aliases)
        {
            var oldWord = AriseFormat.U32(payload, alias.Offset);
            payload.AsSpan(alias.Offset, word.Length).CopyTo(word);
            for (var index = 0; index < sizeof(uint); index++)
            {
                if (byteWrites.TryGetValue(alias.Offset + index, out var value))
                {
                    word[index] = value;
                }
            }

            var newWord = BinaryPrimitives.ReadUInt32LittleEndian(word);
            if (newWord != oldWord)
            {
                changes.Add(new PlannedChange(id, alias.Label, FieldValue.Of((long)oldWord), FieldValue.Of((long)newWord)) { Derived = true });
            }
        }

        return changes;
    }

    private FieldValue Read(int offset) => FieldValue.Of((long)AriseFormat.U32(payload, offset));

    private sealed record Alias(string Label, int Offset);
}
