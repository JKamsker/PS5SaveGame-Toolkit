using System.Text.Json;
using System.Text.RegularExpressions;

namespace SaveToolkit.Testing.Diff;

/// <summary>
/// Shared fixture manifest (<c>savetoolkit-fixtures/1</c>) describing controlled before/after save pairs:
/// provenance (title, build, platform, slot, event), which archives/generations to compare, the changes
/// that must (or must not) appear, and per-title churn rules. The manifest holds no save bytes; archives
/// are referenced by name relative to the artifacts folder. See <c>docs/SAMPLES.md</c>.
/// A manifest with only an <c>ignore</c> block doubles as a churn ignore list (<c>savediff --ignore</c>).
/// </summary>
public sealed partial record FixtureManifest
{
    public const string CurrentFormat = "savetoolkit-fixtures/1";

    public string Format { get; init; } = CurrentFormat;

    public string? Description { get; init; }

    /// <summary>Churn rules applied to every fixture in this manifest.</summary>
    public IReadOnlyList<ChurnRule> Ignore { get; init; } = [];

    public IReadOnlyList<Fixture> Fixtures { get; init; } = [];

    public static FixtureManifest Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (FixtureManifestException ex)
        {
            throw new FixtureManifestException($"{path}: {ex.Message}");
        }
    }

    public static FixtureManifest Parse(string json)
    {
        FixtureManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<FixtureManifest>(json, DiffJson.ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new FixtureManifestException($"invalid JSON: {ex.Message}");
        }

        if (manifest is null)
        {
            throw new FixtureManifestException("the manifest is empty.");
        }

        var errors = manifest.Validate();
        return errors.Count == 0 ? manifest : throw new FixtureManifestException(string.Join(Environment.NewLine, errors));
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Format != CurrentFormat)
        {
            errors.Add($"unsupported format '{Format}', expected '{CurrentFormat}'");
        }

        errors.AddRange(Ignore.SelectMany((r, i) => r.Validate().Select(e => $"ignore[{i}]: {e}")));
        foreach (var duplicate in Fixtures.GroupBy(f => f.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"duplicate fixture id '{duplicate.Key}'");
        }

        foreach (var fixture in Fixtures)
        {
            errors.AddRange(fixture.Validate().Select(e => $"fixture '{fixture.Id}': {e}"));
        }

        return errors;
    }

    [GeneratedRegex("^[A-Z]{4}[0-9]{5}$")]
    internal static partial Regex TitleIdRegex();

    [GeneratedRegex("^([0-9a-fA-F]{2})*$")]
    internal static partial Regex HexRegex();
}

public sealed class FixtureManifestException(string message) : Exception(message);

/// <summary>One controlled capture: the same save taken right before and right after one known in-game event.</summary>
public sealed record Fixture
{
    /// <summary>Unique, stable id, e.g. <c>ppsa01325-collect-one-coin</c>.</summary>
    public string Id { get; init; } = "";

    public string TitleId { get; init; } = "";

    public string? Game { get; init; }

    /// <summary>Game version as shown by the console, e.g. <c>01.004.000</c>.</summary>
    public string? Build { get; init; }

    public string Platform { get; init; } = "PS5";

    public string? Region { get; init; }

    /// <summary>Save slot / profile / character the event happened in.</summary>
    public string? Slot { get; init; }

    public IReadOnlyList<string> Dlc { get; init; } = [];

    /// <summary>The single in-game action between the two captures, e.g. "Bought one Potion for 50 gil".</summary>
    public string Event { get; init; } = "";

    /// <summary>True for a control pair (save, reload, save again with no action) used to learn churn.</summary>
    public bool Control { get; init; }

    public string? CapturedOn { get; init; }

    public string? Notes { get; init; }

    public SaveRef? Before { get; init; }

    public SaveRef? After { get; init; }

    /// <summary>Semantic decoder: <c>json</c>, a plugin id, <c>auto</c>, or null for byte mode only.</summary>
    public string? Decoder { get; init; }

    /// <summary>File globs to compare (see <see cref="SaveDiffOptions.FileFilters"/>); empty = all.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyList<ExpectedChange> Expected { get; init; } = [];

    /// <summary>Churn rules for this fixture only, added to the manifest-level ones.</summary>
    public IReadOnlyList<ChurnRule> Ignore { get; init; } = [];

    /// <summary>When true, every unmasked change must be covered by an expectation.</summary>
    public bool Strict { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Id))
        {
            errors.Add("'id' is required");
        }

        if (!FixtureManifest.TitleIdRegex().IsMatch(TitleId))
        {
            errors.Add($"'titleId' must look like PPSA01234, got '{TitleId}'");
        }

        if (string.IsNullOrWhiteSpace(Event))
        {
            errors.Add("'event' is required (use \"control\" plus \"control\": true for no-action pairs)");
        }

        if (string.IsNullOrWhiteSpace(Before?.Archive) || string.IsNullOrWhiteSpace(After?.Archive))
        {
            errors.Add("'before.archive' and 'after.archive' are required");
        }

        errors.AddRange(Ignore.SelectMany((r, i) => r.Validate().Select(e => $"ignore[{i}]: {e}")));
        errors.AddRange(Expected.SelectMany((x, i) => x.Validate().Select(e => $"expected[{i}]: {e}")));
        return errors;
    }
}

/// <summary>A save set reference: archive (or folder) name relative to the artifacts folder.</summary>
public sealed record SaveRef
{
    public string Archive { get; init; } = "";

    public GenerationSelector Generation { get; init; }

    /// <summary>Optional SHA-256 of the archive file, to detect a replaced capture.</summary>
    public string? Sha256 { get; init; }
}

/// <summary>
/// A change that must appear (or, with <see cref="Changed"/> false, must not appear) in the main file(s)
/// matched by <see cref="Directory"/>/<see cref="File"/>. Either a byte range (<see cref="Offset"/>,
/// <see cref="Length"/>, optional hex <see cref="Before"/>/<see cref="After"/>) or a semantic
/// <see cref="Path"/> (optional value text; <c>(absent)</c> means the path does not exist on that side).
/// </summary>
public sealed record ExpectedChange
{
    public const string Absent = "(absent)";

    public string Directory { get; init; } = "*";

    public string File { get; init; } = "*";

    /// <summary>Match backup (<c>sdimg_sce_bu_</c>) files instead of main files.</summary>
    public bool Backup { get; init; }

    public string? Path { get; init; }

    public long? Offset { get; init; }

    public int? Length { get; init; }

    public string? Before { get; init; }

    public string? After { get; init; }

    public bool Changed { get; init; } = true;

    public string? Note { get; init; }

    public bool IsByteRange => Offset is not null;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Path is null == Offset is null)
        {
            errors.Add("give either 'path' (semantic) or 'offset' + 'length' (bytes)");
        }

        if (Offset is not null && (Offset < 0 || Length is null or <= 0 or > 4096))
        {
            errors.Add("'offset' must be >= 0 and 'length' between 1 and 4096");
        }

        if (IsByteRange)
        {
            foreach (var (name, hex) in new[] { ("before", Before), ("after", After) })
            {
                if (hex is not null && (!FixtureManifest.HexRegex().IsMatch(hex) || hex.Length != Length * 2))
                {
                    errors.Add($"'{name}' must be {Length} bytes of hex");
                }
            }
        }

        return errors;
    }

    public override string ToString() =>
        (IsByteRange ? $"{File} 0x{Offset:X}+{Length}" : $"{File} {Path}") + (Changed ? "" : " (unchanged)");
}
