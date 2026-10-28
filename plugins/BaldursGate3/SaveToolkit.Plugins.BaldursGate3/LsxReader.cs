using System.Xml;
using System.Xml.Linq;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Retains all XML elements/attributes as a bounded, lazy explorer model.</summary>
internal static class LsxReader
{
    private const int MaxDepth = 256;

    internal static LarianResource Read(byte[] bytes, ResourceBudget? budget = null)
    {
        budget ??= new ResourceBudget();
        budget.Reserve(bytes.Length, 0, 0);
        using var stream = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = BinaryData.MaxBufferSize,
        });
        // Check depth before materializing XML, including otherwise unused wrapper elements.
        while (reader.Read())
        {
            BinaryData.Require(reader.Depth <= MaxDepth, "LSX nesting exceeds 256 levels.");
        }

        stream.Position = 0;
        using var secondReader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = BinaryData.MaxBufferSize,
        });
        var document = XDocument.Load(secondReader);
        BinaryData.Require(document.Root?.Name == "save", "LSX must have a <save> root element.");
        var nodes = new List<ResourceNode>();
        var attributes = new List<ResourceAttribute>();
        var pending = new Queue<(XElement Element, int Parent)>();
        pending.Enqueue((document.Root!, -1));
        while (pending.TryDequeue(out var item))
        {
            BinaryData.Require(nodes.Count < BinaryData.MaxRecords, "Too many LSX elements.");
            budget.Reserve(0, 1, 0);
            var (element, parent) = item;
            var label = element.Attribute("id") is { } id ? $"{element.Name} · {id.Value}" : element.Name.ToString();
            var index = nodes.Count;
            var node = new ResourceNode(label, parent);
            nodes.Add(node);
            if (parent >= 0)
            {
                nodes[parent].Children.Add(index);
            }

            foreach (var attribute in element.Attributes())
            {
                AddAttribute(attribute.Name.ToString(), attribute.Value);
            }

            var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value));
            if (!string.IsNullOrWhiteSpace(text))
            {
                AddAttribute("#text", text);
            }

            foreach (var child in element.Elements())
            {
                pending.Enqueue((child, index));
            }

            void AddAttribute(string name, string value)
            {
                BinaryData.Require(attributes.Count < BinaryData.MaxRecords, "Too many LSX attributes.");
                budget.Reserve(0, 0, 1);
                node.Attributes.Add(attributes.Count);
                attributes.Add(new ResourceAttribute(name, LarianAttributeType.String, ReadOnlyMemory<byte>.Empty) { XmlValue = value });
            }
        }

        return new LarianResource("LSX XML", nodes, attributes, [0]);
    }
}
