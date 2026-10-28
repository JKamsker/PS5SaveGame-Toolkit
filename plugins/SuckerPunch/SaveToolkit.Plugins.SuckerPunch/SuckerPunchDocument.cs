using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

internal sealed class SuckerPunchDocument : BoundSaveDocument
{
    private readonly ISaveFile _file;
    private readonly byte[] _bytes;
    private readonly SaveContainer _container;
    private readonly string? _subtitle;

    private SuckerPunchDocument(ISaveFile file, byte[] bytes, SaveContainer container)
    {
        _file = file;
        _bytes = bytes;
        _container = container;
        _subtitle = file.Directory.ParamSfo?.SubTitle;
    }

    public override string Title => string.IsNullOrWhiteSpace(_subtitle)
        ? $"{_file.Directory.TitleId} · {_file.FileName}"
        : $"{_file.Directory.TitleId} · {_subtitle}";

    public override bool CanEdit => false;

    public static SuckerPunchDocument Open(ISaveFile file)
    {
        var bytes = file.ReadAllBytes();
        var container = SaveContainer.Parse(bytes, file.Directory.TitleId);
        return new SuckerPunchDocument(file, bytes, container);
    }

    public override ViewNode BuildView()
    {
        var confidence = _container.Kind == ContainerKind.Tsushima ? Confidence.Verified : Confidence.Unknown;
        var warning = _container.Kind switch
        {
            ContainerKind.Tsushima => null,
            ContainerKind.YoteiEmpty => "This Yōtei slot is an all-zero placeholder; no game fields are known.",
            _ => "Yōtei's non-zero container layout and integrity algorithm are not yet identified.",
        };

        var format = _container.Kind switch
        {
            ContainerKind.Tsushima => "Tsushima container v" + _container.Word3.ToString(CultureInfo.InvariantCulture),
            ContainerKind.YoteiEmpty => "Yōtei empty placeholder",
            _ => "Yōtei container (unmapped)",
        };

        var overview = new List<ViewNode>
        {
            new StatsNode(
            [
                new Stat("Title", _file.Directory.TitleId),
                new Stat("File", _file.PathInDirectory),
                new Stat("Size", $"{_bytes.Length:N0} bytes"),
                new Stat("Format", format),
                new Stat("Checksum", _container.ChecksumValid ? "Verified" : "Unknown"),
            ]),
            new SectionNode("Container header", BuildHeaderFields(confidence, warning)),
        };

        if (!string.IsNullOrWhiteSpace(_subtitle))
        {
            overview.Add(new TextNode($"param.sfo SUBTITLE: {_subtitle}") { Tone = Tone.Info });
        }

        return new TabsNode(
        [
            new Tab("Overview", overview),
            new Tab("Raw bytes", [new HexNode("content", _bytes.Length) { Title = "Complete save bytes" }]),
        ]);
    }

    public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(_file.RelativePath, _bytes.ToArray())];

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        if (blobId != "content")
        {
            throw new KeyNotFoundException($"Unknown blob '{blobId}'.");
        }

        if (offset < 0 || count < 0 || offset > _bytes.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return _bytes.AsMemory((int)offset, count);
    }

    private IReadOnlyList<ViewNode> BuildHeaderFields(Confidence confidence, string? warning)
    {
        var spec = IntegerSpec.UInt32 with { Format = IntegerFormat.Hex };
        var fields = new List<ViewNode>
        {
            Field("container/word0", "Header word 0", spec, () => FieldValue.Of((ulong)_container.Magic)) with
            {
                Confidence = confidence,
                Description = "Little-endian uint32 at offset 0x00.",
                Warning = warning,
            },
            Field("container/word1", "Header word 1", spec, () => FieldValue.Of((ulong)_container.Word1)) with
            {
                Confidence = confidence,
                Description = "Little-endian uint32 at offset 0x04.",
            },
            Field("container/word2", "Header word 2", spec, () => FieldValue.Of((ulong)_container.Word2)) with
            {
                Confidence = confidence,
                Description = "Little-endian uint32 at offset 0x08.",
            },
            Field("container/word3", "Header word 3", spec, () => FieldValue.Of((ulong)_container.Word3)) with
            {
                Confidence = confidence,
                Description = "Little-endian uint32 at offset 0x0C.",
            },
        };

        if (_container.Kind == ContainerKind.Tsushima)
        {
            fields[0] = ((FieldNode)fields[0]) with { Label = "Magic (0x00)" };
            fields[1] = ((FieldNode)fields[1]) with { Label = "Content size (0x04)" };
            fields[2] = ((FieldNode)fields[2]) with { Label = "Checksum (0x08)" };
            fields[3] = ((FieldNode)fields[3]) with { Label = "Format version (0x0C)" };
        }

        return fields;
    }
}
