using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>F12 hook for decompressed evidence; independent of visible/loaded UI pages.</summary>
internal sealed class CyberpunkEvidenceDecoder : ISaveDecoderHook
{
    private const int NodeIdSize = sizeof(int);
    private const int ItemIdentitySize = 16;
    private const int ItemBodyOffset = NodeIdSize + ItemIdentitySize;

    public string Id => "cyberpunk2077-evidence";

    public bool CanDecode(ISaveFile file) => new Cyberpunk2077Plugin().Probe(file).IsMatch;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes)
        {
            throw new InvalidDataException("Save exceeds the evidence decoder input bound.");
        }

        // All published evidence paths have two components; do not emit beyond the host's depth cap.
        const int evidencePathDepth = 2;
        if (sink.MaxDepth < evidencePathDepth)
        {
            sink.MarkTruncated("Cyberpunk evidence paths exceed the decoder depth bound.");
            return;
        }

        var container = CsavContainer.Parse(file.ReadAllBytes());
        var tree = CsavTree.Parse(container);
        if (!sink.Add("header/saveVersion", "integer", Number(container.SaveVersion)) ||
            !sink.Add("header/gameVersion", "integer", Number(container.GameVersion)) ||
            !sink.Add("header/archiveVersion", "integer", Number(container.ArchiveVersion)))
        {
            return;
        }

        if (!DecodeInventory(container, tree, sink)) return;
        DecodeFacts(container, tree, sink);
    }

    private static bool DecodeInventory(CsavContainer container, CsavTree tree, ISaveDecodeSink sink)
    {
        foreach (var item in InventoryReader.Read(container, tree))
        {
            var node = tree.Nodes[item.NodeId];
            var identity = Convert.ToHexString(container.Payload.AsSpan(node.Offset + NodeIdSize, ItemIdentitySize));
            var path = $"inventory[container={item.ContainerId};identity={identity}]";
            if (item.QuantityOffset is { } offset && !sink.Add(path + "/quantity", "integer", Number(BinaryData.UInt32(container.Payload, offset))))
            {
                return false;
            }

            // Include the body fingerprint to retain changes to currently opaque instance data.
            if (!sink.Add(path + "/bodySha256", "sha256", Convert.ToHexStringLower(SHA256.HashData(container.Payload.AsSpan(node.Offset + ItemBodyOffset, node.Size - ItemBodyOffset)))))
            {
                return false;
            }
        }
        return true;
    }

    private static void DecodeFacts(CsavContainer container, CsavTree tree, ISaveDecodeSink sink)
    {
        foreach (var node in tree.Nodes.Where(node => node.Name == "FactsTable"))
        {
            foreach (var fact in FactsReader.Read(BinaryData.Slice(container.Payload, node.Offset, node.Size)))
            {
                // Hashes repeat across tables in the sample; the table node is an explicit scope,
                // not a claimed persistent cross-save identity for future structural changes.
                if (!sink.Add($"facts[table={node.Id};hash={fact.Hash:X8}]/value", "integer", Number(fact.Value)))
                {
                    return;
                }
            }
        }
    }

    private static string Number<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
}
