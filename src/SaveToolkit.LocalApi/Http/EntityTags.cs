namespace SaveToolkit.LocalApi.Http;

/// <summary>An entity tag from a conditional header; <see cref="IsAny"/> is the <c>*</c> wildcard.</summary>
internal sealed record EntityTag(string Tag, bool IsWeak, bool IsAny);

/// <summary>Strict entity-tag list parsing with <c>EntityTagHeaderValue.TryParseStrictList</c> semantics.</summary>
internal static class EntityTags
{
    /// <summary>Parses every header value; any malformed element (or no element at all) rejects the whole list.</summary>
    public static bool TryParseStrictList(IEnumerable<string> values, out List<EntityTag> tags)
    {
        tags = [];
        foreach (var value in values)
        {
            var index = 0;
            while (true)
            {
                index = NextNonSeparator(value, index);
                if (index >= value.Length) break;
                var length = TagLength(value, index, out var tag);
                if (length == 0 || tag is null) return false;
                tags.Add(tag);
                index += length;
                var whitespaceEnd = index + HttpSyntax.WhitespaceLength(value, index);
                if (whitespaceEnd < value.Length && value[whitespaceEnd] != ',') return false;
                index = whitespaceEnd;
            }
        }

        return tags.Count > 0;
    }

    private static int NextNonSeparator(string value, int index)
    {
        while (index < value.Length && value[index] is ' ' or '\t' or ',') index++;
        return index;
    }

    private static int TagLength(string input, int start, out EntityTag? tag)
    {
        tag = null;
        var current = start;
        if (input[start] == '*')
        {
            tag = new EntityTag("*", false, true);
            current++;
        }
        else
        {
            var weak = false;
            if (input[start] is 'W' or 'w')
            {
                if (start + 2 >= input.Length || input[start + 1] != '/') return 0;
                weak = true;
                current += 2;
                current += HttpSyntax.WhitespaceLength(input, current);
            }

            var length = HttpSyntax.QuotedStringLength(input, current);
            if (length == 0) return 0;
            tag = new EntityTag(input.Substring(current, length), weak, false);
            current += length;
        }

        current += HttpSyntax.WhitespaceLength(input, current);
        return current - start;
    }
}
