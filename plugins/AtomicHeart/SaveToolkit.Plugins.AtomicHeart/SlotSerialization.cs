using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Retains one validated edit result. Returned buffers never alias the cache or input.</summary>
internal sealed class SlotSerialization(byte[] original, SaveExtensionFile file, RecordIndex index)
{
    private Dictionary<TaggedProperty, FieldValue>? cachedEdits;
    private byte[]? cachedOutput;

    internal byte[] Serialize(IReadOnlyDictionary<TaggedProperty, FieldValue> edits)
    {
        byte[] output;
        if (edits.Count == 0) output = original; // Its Kraken streams and inner grammar were validated at Open.
        else if (cachedOutput is not null && cachedEdits is not null && cachedEdits.Count == edits.Count &&
            edits.All(e => cachedEdits.TryGetValue(e.Key, out var value) && value == e.Value)) output = cachedOutput;
        else
        {
            var plain = InventoryPatcher.Apply(index, edits);
            output = SlotDataWriter.Write(original, file, index.Plain, plain);
            var validated = SaveExtensionFile.Parse(output);
            var reopened = SlotDataDecoder.Decode(output, validated);
            if (!reopened.Plain.AsSpan().SequenceEqual(plain)) throw new SaveFormatException("Re-decoded SlotData differs from the intended edited bytes.");
            cachedEdits = new Dictionary<TaggedProperty, FieldValue>(edits);
            cachedOutput = output;
        }
        // Reparse every output. Identical private buffers reuse the previously validated decode,
        // avoiding another Kraken pass for no-op exports, repeated downloads and complete undo.
        _ = SaveExtensionFile.Parse(output);
        return output.ToArray();
    }
}
