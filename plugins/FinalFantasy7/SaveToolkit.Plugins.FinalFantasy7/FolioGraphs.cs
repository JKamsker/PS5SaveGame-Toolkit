using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Literal source folio flags; no guessed prerequisite edges, costs or purchase writer.</summary>
internal sealed class FolioGraphs(byte[] bytes, ReferenceData references, Func<ScalarField, FieldNode> bind)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly IReadOnlyList<ProgressFlag> Flags = Load();

    public IReadOnlyList<ViewNode> Sections() => [.. Enumerable.Range(0, Ff7Schema.CharacterCount)
        .Select(index => SectionNode.Lazy(references.Character(index) + " folio candidates", $"spatial/folio/{index}")
            with { Description = references.CharacterSource(index) + " " + EmbeddedCatalogs.Attribution })];

    public IReadOnlyList<ViewNode> Expand(int character)
    {
        if (character < 0 || character >= Ff7Schema.CharacterCount) throw new KeyNotFoundException("Unknown FFVII folio group.");
        var flags = Flags.Where(flag => flag.Region == character.ToString(CultureInfo.InvariantCulture)).ToArray();
        var path = new[] { $"tab:{SocketGraphs.TabTitle}", $"section:spatial/folio/{character}" };
        var fields = flags.Select(flag => bind(new($"folio/{flag.Offset:X}/raw", $"Folio raw byte 0x{flag.Offset:X}", flag.Offset,
            IntegerSpec.UInt8, Confidence.Inferred, Byte: true, Warning:
                $"See the graph for each candidate bit in this byte. {flag.Source}; {EmbeddedCatalogs.Attribution}. " +
                "Read-only: SP costs, prerequisite edges, purchase effects and console identity are unverified."))).ToArray();
        // Several flags share a byte: publish one raw control per byte and link each candidate to it.
        var graph = new GraphNode(references.Character(character) + " folio candidates",
            [.. flags.Select((flag, index) => new GraphItem(flag.Id, flag.Name,
                ((bytes[flag.Offset] >> flag.Bit) & 1) == 1 ? GraphState.Unlocked : GraphState.Unknown)
            {
                Branch = flag.Category,
                Description = $"Candidate bit {flag.Bit} at 0x{flag.Offset:X} = {(bytes[flag.Offset] >> flag.Bit) & 1}. " +
                    $"A zero bit does not prove availability or locking. {flag.Source}; {EmbeddedCatalogs.Attribution}. " + references.CharacterSource(character),
                Target = new(ViewTargetKind.Field, fields[index].ValueId) { Path = path },
            })], [], "Read-only candidate folio flags grouped by source category. Unlocked means stored bit 1 only; " +
                "all zero-bit states, prerequisites, SP costs and game layout are unknown. No purchase, respec or weapon-upgrade dependency is implied.");
        return [graph, new SectionNode("Raw folio bytes", [.. fields.DistinctBy(field => field.ValueId)])];
    }

    private static IReadOnlyList<ProgressFlag> Load()
    {
        using var stream = typeof(FolioGraphs).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.FinalFantasy7.Resources.rebirth-folios.json")
            ?? throw new InvalidOperationException("Embedded FFVII folio candidates are missing.");
        return JsonSerializer.Deserialize<ProgressFlag[]>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Embedded FFVII folio candidates are invalid.");
    }
}
