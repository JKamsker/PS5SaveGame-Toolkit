using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Bounded F1 evidence paths for the stable character words in a gameplay record.</summary>
internal sealed class FinalFantasy7EvidenceDecoder : ISaveDecoderHook
{
    private const int PathDepth = 3;
    private const int VitalDelta = CharacterLayout.HpDelta;

    public string Id => "ff7r-character-evidence";

    public bool CanDecode(ISaveFile file) => new FinalFantasy7Plugin().Probe(file).Score > ProbeScore.Fallback;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes)
        {
            throw new InvalidDataException("FFVII save exceeds the evidence decoder input bound.");
        }

        if (!CanDecode(file))
        {
            throw new SaveFormatException("This is not a recognised Final Fantasy VII save file.");
        }

        var schema = Ff7Schema.ForTitle(file.Directory.TitleId)!;
        var bytes = file.ReadAllBytes();
        _ = schema.TryGetRole(file.FileName, out var common);
        _ = LinkedRecords.Parse(bytes, schema, common);
        if (sink.MaxDepth < PathDepth)
        {
            sink.MarkTruncated("FFVII character evidence paths require a decode depth of at least 3.");
            return;
        }

        if (common)
        {
            sink.Warn("Common/system files have no mapped character evidence.");
            return;
        }

        for (var character = 0; character < Ff7Schema.CharacterCount; character++)
        {
            var offset = CharacterLayout.Offset(schema, character);
            if (!Add(sink, $"character/{character}/level", "integer", bytes[offset]) ||
                !Add(sink, $"character/{character}/experience", "integer", U32(bytes, offset + CharacterLayout.ExperienceDelta)) ||
                !Add(sink, $"character/{character}/hp", "integer", U32(bytes, offset + CharacterLayout.HpDelta)) ||
                !Add(sink, $"character/{character}/max-hp", "integer", U32(bytes, offset + VitalDelta + CharacterLayout.MaximumVitalDelta)) ||
                !Add(sink, $"character/{character}/mp", "integer", U32(bytes, offset + CharacterLayout.MpDelta)) ||
                !Add(sink, $"character/{character}/max-mp", "integer", U32(bytes, offset + CharacterLayout.MpDelta + CharacterLayout.MaximumVitalDelta)))
            {
                return;
            }

            if (!schema.IsRemake && !Add(sink, $"character/{character}/weapon", "integer",
                U32(bytes, CharacterLayout.WeaponOffset(character))))
            {
                return;
            }
        }
    }

    private static bool Add(ISaveDecodeSink sink, string path, string kind, uint value) =>
        sink.Add(path, kind, value.ToString(CultureInfo.InvariantCulture));

    private static bool Add(ISaveDecodeSink sink, string path, string kind, byte value) =>
        sink.Add(path, kind, value.ToString(CultureInfo.InvariantCulture));

    private static uint U32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
