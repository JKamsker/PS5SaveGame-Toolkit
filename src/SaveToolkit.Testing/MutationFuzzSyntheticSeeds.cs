using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing;

/// <summary>Authored fixtures only. Unlisted formats need valid seeds supplied by their conformance class.</summary>
internal static class MutationFuzzSyntheticSeeds
{
    internal static IEnumerable<ISaveFile> For(string pluginId)
    {
        switch (pluginId)
        {
            case "format.json":
                yield return MemorySaveSet.SingleFile("synthetic.json",
                    "{\"count\":5,\"enabled\":true,\"items\":[{\"name\":\"synthetic\",\"amount\":2}]}"u8.ToArray());
                break;
            case "format.json-xor":
                var settings = "{\"masterVolume\":49,\"enabled\":true,\"items\":[1,2,3]}"u8.ToArray();
                // The plugin supports XOR-1 and byte-minus-one UTF-8 JSON envelopes.
                yield return MemorySaveSet.SingleFile("synthetic-xor.dat",
                    settings.Select(value => (byte)(value ^ 1)).ToArray(), "PPSA16064");
                yield return MemorySaveSet.SingleFile("synthetic-minus-one.dat",
                    settings.Select(value => unchecked((byte)(value - 1))).ToArray(), "PPSA16064");
                break;
        }
    }
}
