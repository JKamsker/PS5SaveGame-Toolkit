using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Plugins;

/// <summary>Built-in fallback: identifies the container format and shows a hex view. Never writes.</summary>
public sealed class RawFilePlugin : IGamePlugin
{
    public const string PluginId = "builtin.raw";

    public PluginInfo Info { get; } = new(PluginId, "Raw file inspector", "1.0.0")
    {
        Description = "Format sniffing, entropy and hex view for files no game plugin understands.",
        Maturity = PluginMaturity.ReadOnly,
    };

    public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Fallback, FormatSniffer.Identify(file.ReadHeader(512)));

    public ISaveDocument Open(ISaveFile file) => new RawDocument(file);

    private sealed class RawDocument(ISaveFile file) : ISaveDocument
    {
        private const int EntropySample = 1 << 20;
        private const int MaxBlobRead = 64 * 1024;

        public string Title => file.FileName;

        public bool CanEdit => false;

        public ViewNode BuildView()
        {
            var header = file.ReadHeader(EntropySample);
            var format = FormatSniffer.Identify(header);
            var entropy = FormatSniffer.Entropy(header);
            var hint = entropy switch
            {
                > 7.9 => "Looks encrypted or compressed.",
                > 6.5 => "Dense binary data, possibly compressed in parts.",
                _ => "Plain structured data; a parser is likely feasible.",
            };

            return new SectionNode(file.FileName,
            [
                new StatsNode(
                [
                    new Stat("Size", file.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes"),
                    new Stat("Format", format ?? "unknown"),
                    new Stat("Entropy", entropy.ToString("0.00", CultureInfo.InvariantCulture) + " bits/byte") { Hint = "Measured over the first MiB." },
                ]),
                new TextNode($"No game plugin claimed this file. {hint}") { Tone = Tone.Muted },
                new FieldNode("raw.header", "First 16 bytes", new TextSpec { ReadOnly = true },
                    FieldValue.Of(Convert.ToHexString(header.AsSpan(0, Math.Min(16, header.Length))))),
                new HexNode("content", file.Length) { Title = "Content" },
            ]);
        }

        public FieldValue GetValue(string valueId) => valueId == "raw.header"
            ? FieldValue.Of(Convert.ToHexString(file.ReadHeader(16)))
            : throw new KeyNotFoundException(valueId);

        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) =>
            EditOutcome.Rejected(edits.FirstOrDefault()?.ValueId ?? string.Empty, "The raw inspector is read-only.");

        public IReadOnlyList<FileOutput> Serialize() => [];

        public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
        {
            if (blobId != "content")
            {
                throw new KeyNotFoundException(blobId);
            }

            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            count = (int)Math.Clamp(Math.Min(count, file.Length - offset), 0, MaxBlobRead);
            using var stream = file.OpenRead();
            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[count];
            stream.ReadExactly(buffer);
            return buffer;
        }

        public void Dispose()
        {
        }
    }
}
