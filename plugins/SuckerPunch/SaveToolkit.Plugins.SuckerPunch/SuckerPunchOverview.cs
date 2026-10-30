using System.Buffers.Binary;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

internal delegate FieldNode ReadOnlyField(string id, string label, FieldSpec spec, Func<FieldValue> get);

/// <summary>Read-only container and correlation evidence, separate from the document's editable bindings.</summary>
internal sealed class SuckerPunchOverview(ISaveFile file, byte[] bytes, SaveContainer container, TsushimaProgress? progress)
{
    public IReadOnlyList<ViewNode> Build(ReadOnlyField field, bool strictCaption = false)
    {
        var isTsushima = container.Kind == ContainerKind.Tsushima;
        var format = container.Kind switch
        {
            ContainerKind.Tsushima => $"Tsushima container v{container.Word3}",
            ContainerKind.YoteiEmpty => "Yōtei empty placeholder",
            _ => "Yōtei container (unmapped)",
        };
        var nodes = new List<ViewNode>
        {
            new StatsNode(
            [
                new Stat("Title", file.Directory.TitleId),
                new Stat("File", file.PathInDirectory),
                new Stat("Size", $"{bytes.Length:N0} bytes"),
                new Stat("Format", format),
                new Stat("Checksum", container.ChecksumValid ? "Verified" : "Unknown"),
            ]),
            new SectionNode("Container header", Header(isTsushima, field)),
        };
        if (!strictCaption && file.Directory.ParamSfo?.SubTitle is { Length: > 0 } subtitle)
        {
            nodes.Add(new TextNode($"param.sfo SUBTITLE: {subtitle}") { Tone = Tone.Info });
        }

        if (!isTsushima)
        {
            nodes.Add(new TextNode("Yōtei named records and experimental quantity edits use the dedicated Yōtei document. " +
                "This container-only overview does not expose those fields.")
                { Tone = Tone.Warning });
        }

        if (progress is not null)
        {
            nodes.Add(field("progress/difficulty", "Difficulty", new TextSpec(), () => FieldValue.Of(progress.Difficulty)) with
            {
                Confidence = Confidence.Inferred,
                Description = "Length-prefixed name before the difficulty timer table; matches all slot subtitles. " +
                    "Read-only: changing string length would relocate unknown data and references.",
            });
        }

        return nodes;
    }

    private IReadOnlyList<ViewNode> Header(bool isTsushima, ReadOnlyField field)
    {
        string[] labels = isTsushima ? ["Magic", "Content size", "Checksum", "Format version"] :
            ["Header word 0", "Header word 1", "Header word 2", "Header word 3"];
        return Enumerable.Range(0, labels.Length).Select(index => (ViewNode)(field(
            $"container/word{index}", labels[index], IntegerSpec.UInt32 with { Format = IntegerFormat.Hex },
            () => FieldValue.Of((ulong)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint))))) with
        {
            Confidence = isTsushima ? Confidence.Verified : Confidence.Unknown,
            Description = $"Little-endian uint32 at offset 0x{index * sizeof(uint):X2}.",
            Warning = isTsushima ? null : "Header fields are read-only; Yōtei gameplay uses its dedicated document.",
        })).ToArray();
    }
}
