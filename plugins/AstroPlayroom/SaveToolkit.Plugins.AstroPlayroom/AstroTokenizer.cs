using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AstroPlayroom;

/// <summary>Small byte tokenizer: numeric names are legal and source spans remain authoritative.</summary>
internal sealed class AstroTokenizer(byte[] bytes, int end)
{
    private const int MaximumDepth = 64;
    private const int MaximumElements = 4096;
    private const int MaximumValues = 4096;
    private int _elements;
    private int _values;

    public int Position { get; private set; } = AstroAllocation.TextOffset;

    public AstroElement Parse()
    {
        SkipWhitespace();
        var root = Element("", new Dictionary<string, int>(StringComparer.Ordinal), 0);
        if (root.Name != "PreInstall")
        {
            throw Error("Expected PreInstall root.");
        }

        // The second supplied generation has leftover text after this root. It is opaque, not XML.
        return root;
    }

    private AstroElement Element(string parent, Dictionary<string, int> siblings, int depth)
    {
        if (depth >= MaximumDepth || ++_elements > MaximumElements)
        {
            throw Error("ASTRO tree exceeds the supported depth or element count.");
        }

        Expect((byte)'<');
        var name = Name();
        var occurrence = siblings.GetValueOrDefault(name);
        siblings[name] = occurrence + 1;
        var node = new AstroElement($"{parent}/{name}[{occurrence}]", name);
        Attributes(node);
        if (Take((byte)'/'))
        {
            Expect((byte)'>');
            return node;
        }

        Expect((byte)'>');
        var children = new Dictionary<string, int>(StringComparer.Ordinal);
        var textCount = 0;
        while (Position < end)
        {
            if (Starts("</"u8))
            {
                Position += 2;
                if (Name() != name)
                {
                    throw Error($"Mismatched closing tag for {name}.");
                }

                SkipWhitespace();
                Expect((byte)'>');
                return node;
            }

            if (Starts("<!--"u8))
            {
                Position += 4;
                var length = bytes.AsSpan(Position, end - Position).IndexOf("-->"u8);
                if (length < 0)
                {
                    throw Error("Unterminated comment.");
                }

                Position += length + 3;
            }
            else if (bytes[Position] == '<')
            {
                node.Children.Add(Element(node.Id, children, depth + 1));
            }
            else
            {
                var start = Position;
                while (Position < end && bytes[Position] != '<')
                {
                    Position++;
                }

                var stop = Position;
                while (start < stop && IsWhitespace(bytes[start]))
                {
                    start++;
                }

                while (stop > start && IsWhitespace(bytes[stop - 1]))
                {
                    stop--;
                }

                if (stop > start)
                {
                    AddValue(node, new AstroValue($"{node.Id}/text[{textCount++}]", "Text", start, stop - start, false));
                }
            }
        }

        throw Error($"Unclosed tag {name}.");
    }

    private void Attributes(AstroElement node)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (Position < end)
        {
            var hadWhitespace = SkipWhitespace();
            if (bytes[Position] is (byte)'>' or (byte)'/')
            {
                return;
            }

            if (!hadWhitespace)
            {
                throw Error("Expected whitespace before attribute.");
            }

            var name = Name();
            if (!names.Add(name))
            {
                throw Error("Duplicate attribute.");
            }

            SkipWhitespace();
            Expect((byte)'=');
            SkipWhitespace();
            if (Position >= end || bytes[Position] is not ((byte)'\'' or (byte)'"'))
            {
                throw Error("Expected quoted attribute value.");
            }

            var quote = bytes[Position++];
            var start = Position;
            while (Position < end && bytes[Position] != quote)
            {
                if (bytes[Position++] == '<')
                {
                    throw Error("Unescaped tag in attribute.");
                }
            }

            var length = Position - start;
            Expect(quote);
            AddValue(node, new AstroValue($"{node.Id}/@{name}", name, start, length, true));
        }

        throw Error("Unterminated opening tag.");
    }

    private void AddValue(AstroElement node, AstroValue value)
    {
        if (++_values > MaximumValues)
        {
            throw Error("ASTRO tree exceeds the supported value count.");
        }

        node.Values.Add(value);
    }

    private string Name()
    {
        var start = Position;
        while (Position < end && IsName(bytes[Position]))
        {
            Position++;
        }

        if (Position == start)
        {
            throw Error("Expected a tag or attribute name.");
        }

        return Encoding.ASCII.GetString(bytes, start, Position - start);
    }

    private bool SkipWhitespace()
    {
        var start = Position;
        while (Position < end && IsWhitespace(bytes[Position]))
        {
            Position++;
        }

        return Position != start;
    }

    private bool Take(byte value)
    {
        if (Position >= end || bytes[Position] != value)
        {
            return false;
        }

        Position++;
        return true;
    }

    private void Expect(byte value)
    {
        if (!Take(value))
        {
            throw Error($"Expected '{(char)value}'.");
        }
    }

    private bool Starts(ReadOnlySpan<byte> value) => bytes.AsSpan(Position, end - Position).StartsWith(value);
    private SaveFormatException Error(string message) => new($"{message} (byte 0x{Position:X})");
    private static bool IsWhitespace(byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    private static bool IsName(byte value) => value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z'
        or >= (byte)'0' and <= (byte)'9' or (byte)'_' or (byte)'-' or (byte)'.' or (byte)':';
}
