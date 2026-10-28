using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

/// <summary>F1 hook for decompressed evidence; independent of visible/loaded UI pages.</summary>
public sealed class CyberpunkEvidenceDecoder : ISaveDecoder
{
    public string Id => "cyberpunk2077-evidence";

    public bool CanDecode(ISaveFile file) => new Cyberpunk2077Plugin().Probe(file).IsMatch;

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes)
        {
            throw new InvalidDataException("Save exceeds the evidence decoder input bound.");
        }

        var container = CsavContainer.Parse(file.ReadAllBytes());
        var tree = CsavTree.Parse(container);
        var builder = new SemanticTreeBuilder(Id, limits);
        if (!builder.Add("header/saveVersion", "integer", Number(container.SaveVersion)) ||
            !builder.Add("header/gameVersion", "integer", Number(container.GameVersion)) ||
            !builder.Add("header/archiveVersion", "integer", Number(container.ArchiveVersion)))
        {
            return builder.Build();
        }

        foreach (var item in InventoryReader.Read(container, tree))
        {
            var node = tree.Nodes[item.NodeId];
            var identity = Convert.ToHexString(container.Payload.AsSpan(node.Offset + 4, 16));
            var path = $"inventory[container={item.ContainerId};identity={identity}]";
            if (item.QuantityOffset is { } offset && !builder.Add(path + "/quantity", "integer", Number(BinaryData.UInt32(container.Payload, offset))))
            {
                return builder.Build();
            }

            // Include the body fingerprint to retain changes to currently opaque instance data.
            if (!builder.Add(path + "/bodySha256", "sha256", Convert.ToHexStringLower(SHA256.HashData(container.Payload.AsSpan(node.Offset + 20, node.Size - 20)))))
            {
                return builder.Build();
            }
        }

        foreach (var node in tree.Nodes.Where(node => node.Name == "FactsTable"))
        {
            foreach (var fact in FactsReader.Read(BinaryData.Slice(container.Payload, node.Offset, node.Size)))
            {
                // Hashes repeat across tables in the sample; the table node is an explicit scope,
                // not a claimed persistent cross-save identity for future structural changes.
                if (!builder.Add($"facts[table={node.Id};hash={fact.Hash:X8}]/value", "integer", Number(fact.Value)))
                {
                    return builder.Build();
                }
            }
        }

        return builder.Build();
    }

    private static string Number<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
}
