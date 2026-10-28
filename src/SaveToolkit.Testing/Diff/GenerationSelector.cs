using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Testing.Diff;

public enum GenerationMode
{
    /// <summary>Every generation; generation n of one side pairs with generation n of the other.</summary>
    All,

    /// <summary>Only the highest generation of each path (the newest appended snapshot).</summary>
    Latest,

    /// <summary>Only the given generation.</summary>
    Only,
}

/// <summary>
/// Picks which generations (repeated ZIP occurrences of one path) take part in a comparison.
/// Selecting a single generation on both sides compares two snapshots of the same dump.
/// JSON: <c>"all"</c>, <c>"latest"</c> or a number.
/// </summary>
[JsonConverter(typeof(GenerationSelectorJsonConverter))]
public readonly record struct GenerationSelector
{
    private GenerationSelector(GenerationMode mode, int index)
    {
        Mode = mode;
        Index = index;
    }

    public GenerationMode Mode { get; }

    /// <summary>The generation for <see cref="GenerationMode.Only"/>.</summary>
    public int Index { get; }

    public static GenerationSelector All => default;

    public static GenerationSelector Latest => new(GenerationMode.Latest, 0);

    public static GenerationSelector Only(int generation) =>
        generation >= 0 ? new(GenerationMode.Only, generation) : throw new ArgumentOutOfRangeException(nameof(generation));

    public static bool TryParse(string? text, out GenerationSelector selector)
    {
        selector = All;
        if (string.IsNullOrWhiteSpace(text) || text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            selector = Latest;
            return true;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            selector = Only(index);
            return true;
        }

        return false;
    }

    public static GenerationSelector Parse(string? text) =>
        TryParse(text, out var selector) ? selector : throw new FormatException($"Invalid generation '{text}'. Use 'all', 'latest' or a number.");

    public override string ToString() => Mode switch
    {
        GenerationMode.All => "all",
        GenerationMode.Latest => "latest",
        _ => Index.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Keeps the selected generations of each logical path.</summary>
    internal IEnumerable<T> Apply<T, TKey>(IEnumerable<T> items, Func<T, TKey> pathKey, Func<T, int> generation)
        where TKey : notnull
    {
        var mode = Mode;
        var index = Index;
        return mode switch
        {
            GenerationMode.All => items,
            GenerationMode.Only => items.Where(i => generation(i) == index),
            _ => items.GroupBy(pathKey).Select(g => g.MaxBy(generation)!),
        };
    }
}

public sealed class GenerationSelectorJsonConverter : JsonConverter<GenerationSelector>
{
    public override GenerationSelector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => GenerationSelector.All,
            JsonTokenType.Number when reader.TryGetInt32(out var n) && n >= 0 => GenerationSelector.Only(n),
            JsonTokenType.String when GenerationSelector.TryParse(reader.GetString(), out var s) => s,
            _ => throw new JsonException("A generation must be \"all\", \"latest\" or a non-negative number."),
        };

    public override void Write(Utf8JsonWriter writer, GenerationSelector value, JsonSerializerOptions options)
    {
        if (value.Mode == GenerationMode.Only)
        {
            writer.WriteNumberValue(value.Index);
        }
        else
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
