using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Bounded F12 decoder for the parsed scalar tags in Atomic Heart's SaveExtension payload.</summary>
internal sealed class AtomicHeartSaveDecoder : ISaveDecoderHook
{
    public string Id => "atomic-heart.saveextension-v1";

    public bool CanDecode(ISaveFile file)
    {
        try
        {
            return new AtomicHeartPlugin().Probe(file).IsMatch;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes)
        {
            throw new InvalidDataException("Atomic Heart save exceeds the decoder file limit.");
        }

        if (!CanDecode(file))
        {
            throw new SaveFormatException("This is not a recognised Atomic Heart SaveExtension file.");
        }

        if (sink.MaxDepth < 2)
        {
            sink.MarkTruncated("Atomic Heart scalar paths require a decode depth of at least 2.");
            return;
        }

        var bytes = file.ReadAllBytes();
        var outer = SaveExtensionFile.Parse(bytes);
        var index = SlotDataDecoder.Decode(bytes, outer);
        if (!EmitProperties("metadata", index.Properties, sink, 1)) return;

        foreach (var record in index.Objects)
        {
            var recordPath = $"record[{Uri.EscapeDataString(record.Id)}]";
            if (!EmitProperties(recordPath, record.Properties, sink, 1)) return;
        }

        sink.Warn("Native transforms, opaque property payloads and unknown tails remain byte-level evidence.");
    }

    private static bool EmitProperties(string parent, IReadOnlyList<TaggedProperty> properties,
        ISaveDecodeSink sink, int depth)
    {
        foreach (var property in properties)
        {
            var path = FormattableString.Invariant($"{parent}/{Uri.EscapeDataString(property.Name)}[{property.ArrayIndex}]");
            if (property.Value is { } value)
            {
                if (depth + 1 > sink.MaxDepth)
                {
                    sink.MarkTruncated("Atomic Heart property paths exceed the decoder depth bound.");
                    return false;
                }

                if (!Add(sink, path, property.Name, value)) return false;
            }

            if (property.Children.Count != 0 && !EmitProperties(path, property.Children, sink, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Add(ISaveDecodeSink sink, string path, string label, FieldValue value)
    {
        var kind = value.Kind switch
        {
            FieldValueKind.Bool => "bool",
            FieldValueKind.Integer => "integer",
            FieldValueKind.Float => "float",
            FieldValueKind.Text => "text",
            _ => null,
        };
        if (kind is null) return true;

        var text = value.Kind switch
        {
            FieldValueKind.Bool => value.AsBool() ? "true" : "false",
            FieldValueKind.Integer => value.AsInteger().ToString(CultureInfo.InvariantCulture),
            FieldValueKind.Float => value.AsDouble().ToString("R", CultureInfo.InvariantCulture),
            _ => value.AsString(),
        };
        return sink.Add(path, kind, text, label);
    }
}
