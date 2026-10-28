using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed record AstroAllocation(byte[] Bytes, int Terminator, AstroElement Root, int RootEnd)
{
    public const int Size = 1024 * 1024;
    public const int TextOffset = 0x400;
    public static ReadOnlySpan<byte> Signature => "\r\n<PreInstall>"u8;

    public static bool HasSignature(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= TextOffset + Signature.Length && bytes[..TextOffset].IndexOfAnyExcept((byte)0) < 0 &&
        bytes[TextOffset..].StartsWith(Signature);

    public static AstroAllocation Parse(byte[] bytes)
    {
        if (bytes.Length != Size || !HasSignature(bytes))
        {
            throw new SaveFormatException("Unsupported ASTRO allocation: expected a zero preamble and CRLF followed by <PreInstall>.");
        }

        var terminator = bytes.AsSpan(TextOffset).IndexOf((byte)0);
        if (terminator < 0)
        {
            throw new SaveFormatException("ASTRO text has no NUL terminator within its allocation.");
        }

        terminator += TextOffset;
        if (bytes.AsSpan(terminator).IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new SaveFormatException("Unsupported nonzero data after the ASTRO NUL terminator.");
        }

        try
        {
            // Validate encoding, including the opaque suffix, without normalizing any bytes.
            _ = new UTF8Encoding(false, true).GetCharCount(bytes.AsSpan(TextOffset, terminator - TextOffset));
        }
        catch (DecoderFallbackException ex)
        {
            throw new SaveFormatException("ASTRO text is not valid UTF-8.", ex);
        }

        var tokenizer = new AstroTokenizer(bytes, terminator);
        var root = tokenizer.Parse();
        return new AstroAllocation(bytes, terminator, root, tokenizer.Position);
    }

    public string Read(AstroValue value) => Encoding.UTF8.GetString(Bytes, value.Offset, value.Length);
}

internal sealed record AstroValue(string Id, string Name, int Offset, int Length, bool IsAttribute);

internal sealed class AstroElement(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public List<AstroElement> Children { get; } = [];
    public List<AstroValue> Values { get; } = [];
}
