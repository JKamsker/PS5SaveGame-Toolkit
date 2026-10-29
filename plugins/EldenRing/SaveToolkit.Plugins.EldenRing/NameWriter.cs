using System.Text;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.EldenRing;

// Fixed buffers from the pinned ClayAmore PlayStation layout; never clear opaque trailing words.
internal sealed class NameWriter
{
    private readonly byte[] _bytes;
    private readonly (int Offset, byte[] Original)[] _buffers;
    private readonly string _originalName;
    private static readonly UnicodeEncoding Encoding = new(false, false, true);

    public NameWriter(byte[] bytes, CharacterSlot slot)
    {
        _bytes = bytes;
        _originalName = EldenRingParser.ReadName(bytes, slot.ProfileOffset);
        _buffers = new[] { slot.PlayerOffset!.Value + EldenRingLayout.NameOffset, slot.ProfileOffset }
            .Select(offset => (offset, bytes.AsSpan(offset, EldenRingLayout.NameBytes).ToArray())).ToArray();
    }

    public string? Validate(FieldValue value)
    {
        if (value.Kind != FieldValueKind.Text) return "Choose a character name as text.";
        var name = value.AsString();
        // Accepted imports may predate the authoring rules. Undo must restore their exact value.
        if (name == _originalName) return null;
        if (string.IsNullOrWhiteSpace(name) || name.Length > EldenRingLayout.NameCharacters
            || name.Any(char.IsControl))
            return "Use 1–16 UTF-16 code units, with no control characters or embedded terminators.";
        try { _ = Encoding.GetByteCount(name); }
        catch (EncoderFallbackException) { return "The name contains an unpaired UTF-16 surrogate."; }
        return null;
    }

    public void Set(FieldValue value)
    {
        var encoded = Encoding.GetBytes(value.AsString());
        // Reconstruct from each original buffer, so changing back restores even nonzero padding exactly.
        foreach (var (offset, original) in _buffers)
        {
            original.CopyTo(_bytes, offset);
            encoded.CopyTo(_bytes, offset);
            _bytes.AsSpan(offset + encoded.Length, sizeof(ushort)).Clear();
        }
    }
}
