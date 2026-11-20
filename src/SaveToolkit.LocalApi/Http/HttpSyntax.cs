namespace SaveToolkit.LocalApi.Http;

/// <summary>RFC 9110 token and quoted-string primitives used by the header parsers.</summary>
internal static class HttpSyntax
{
    public static bool IsTokenChar(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
            or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

    public static int TokenLength(string input, int start)
    {
        var index = start;
        while (index < input.Length && IsTokenChar(input[index])) index++;
        return index - start;
    }

    public static int WhitespaceLength(string input, int start)
    {
        var index = start;
        while (index < input.Length && input[index] is ' ' or '\t') index++;
        return index - start;
    }

    /// <summary>Length of a quoted string (including its quotes and quoted pairs) at <paramref name="start"/>, or 0.</summary>
    public static int QuotedStringLength(string input, int start)
    {
        if (start >= input.Length || input[start] != '"') return 0;
        var index = start + 1;
        while (index < input.Length)
        {
            var c = input[index];
            if (c == '\\')
            {
                if (index + 1 >= input.Length) return 0;
                index += 2;
                continue;
            }

            // Like HttpRuleParser, bare CR/LF inside a quoted string are allowed (multipart part headers can carry them).
            if (c == '"') return index + 1 - start;
            index++;
        }

        return 0;
    }

    /// <summary>Removes one pair of bounding quotes without unescaping, like <c>HeaderUtilities.RemoveQuotes</c>.</summary>
    public static string RemoveQuotes(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
}

/// <summary>
/// A parsed media type with its parameters. Values keep their quotes; like <c>NameValueHeaderValue</c>, a
/// parameter without <c>=</c> has a null value and one with an empty <c>=</c> has an empty value.
/// </summary>
internal sealed record MediaType(string Type, string SubType, IReadOnlyList<KeyValuePair<string, string?>> Parameters)
{
    public string Value => Type + "/" + SubType;

    public string? Suffix => SubType.LastIndexOf('+') is var plus and >= 0 ? SubType[(plus + 1)..] : null;

    public string? Parameter(string name) =>
        Parameters.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    public static MediaType? Parse(string? input)
    {
        if (string.IsNullOrEmpty(input)) return null;
        var index = HttpSyntax.WhitespaceLength(input, 0);
        var typeLength = HttpSyntax.TokenLength(input, index);
        if (typeLength == 0) return null;
        var type = input.Substring(index, typeLength);
        index += typeLength;
        index += HttpSyntax.WhitespaceLength(input, index);
        if (index >= input.Length || input[index] != '/') return null;
        index++;
        index += HttpSyntax.WhitespaceLength(input, index);
        var subLength = HttpSyntax.TokenLength(input, index);
        if (subLength == 0) return null;
        var subType = input.Substring(index, subLength);
        index += subLength;
        var parameters = ParseParameters(input, ref index);
        return parameters is null ? null : new MediaType(type, subType, parameters);
    }

    /// <summary>Parses <c>*( OWS ";" OWS name [ "=" [ value ] ] )</c> to the end of the input.</summary>
    internal static List<KeyValuePair<string, string?>>? ParseParameters(string input, ref int index)
    {
        var parameters = new List<KeyValuePair<string, string?>>();
        while (true)
        {
            index += HttpSyntax.WhitespaceLength(input, index);
            if (index >= input.Length) return parameters;
            if (input[index] != ';') return null;
            index++;
            index += HttpSyntax.WhitespaceLength(input, index);
            if (index >= input.Length) return parameters;
            var nameLength = HttpSyntax.TokenLength(input, index);
            if (nameLength == 0) return null;
            var name = input.Substring(index, nameLength);
            index += nameLength;
            index += HttpSyntax.WhitespaceLength(input, index);
            string? value = null;
            if (index < input.Length && input[index] == '=')
            {
                index++;
                index += HttpSyntax.WhitespaceLength(input, index);
                // The value may be empty; an invalid one is empty too and fails at the next separator.
                var length = index < input.Length && input[index] == '"'
                    ? HttpSyntax.QuotedStringLength(input, index)
                    : HttpSyntax.TokenLength(input, index);
                value = input.Substring(index, length);
                index += length;
            }

            parameters.Add(new(name, value));
        }
    }

    public bool IsJson => string.Equals(Value, "application/json", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Suffix, "json", StringComparison.OrdinalIgnoreCase);

    public bool IsForm => string.Equals(Value, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
        || IsMultipartForm;

    public bool IsMultipartForm => string.Equals(Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase);
}
