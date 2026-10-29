using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed class AstroSchema
{
    private readonly AstroAllocation _allocation;
    private readonly IReadOnlyList<AstroElement> _elements;

    public AstroSchema(AstroAllocation allocation)
    {
        _allocation = allocation;
        _elements = Descendants(allocation.Root).ToArray();
        // Values, offsets, whitespace and suffix bytes are deliberately excluded. Occurrences and
        // attribute identities remain significant; a patch number cannot be deduced from this hash.
        var identities = _elements.Select(e => $"element:{e.Id}")
            .Concat(_elements.SelectMany(e => e.Values.Select(v => $"value:{v.Id}")))
            .Order(StringComparer.Ordinal);
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', identities))));
    }

    public string Fingerprint { get; }

    public IReadOnlyList<AstroCollectionRecord> Records { get; private set; } = [];

    public void Inventory(IReadOnlyList<AstroField> fields)
    {
        var byElement = fields.ToLookup(f => f.Element);
        Records = _allocation.Root.Children.Where(g => g.Name is "Artifact" or "Puzzle" or "Prop")
            .SelectMany(g => g.Children.Select(e => new AstroCollectionRecord(g.Name, e,
                e.Children.SelectMany(c => byElement[c]).ToArray())))
            .ToArray();
    }

    public SectionNode Report() => new("Schema report", [
        new TextNode($"Structure/key SHA-256: {Fingerprint}") { Monospace = true },
        new TextNode("This identifies the saved structure, not a game patch version. Reference aliases require an exact schema match."),
        new StatsNode([
            new Stat("Elements", _elements.Count.ToString()),
            new Stat("Primitive values", _elements.Sum(e => e.Values.Count).ToString()),
            new Stat("Opaque suffix bytes", (_allocation.Terminator - _allocation.RootEnd).ToString())]),
        new TableNode([new TableColumn("group", "Exact saved group"), new TableColumn("records", "Immediate children"),
            new TableColumn("values", "Primitive values")],
            _allocation.Root.Children.Select(group => new TableRow(group.Id, new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["group"] = new TextNode(group.Name),
                ["records"] = new TextNode(group.Children.Count.ToString()),
                ["values"] = new TextNode(Descendants(group).Sum(e => e.Values.Count).ToString())
            })).ToArray()) { Title = "Saved groups (including unknown groups)" },
        new TextNode(_elements.Any(e => e.Name.StartsWith("Armadillo", StringComparison.Ordinal))
            ? "Special-bot keys detected in an unvalidated schema. Inspect Raw; event eligibility, rescue and transfer remain unsupported."
            : "Special bots unavailable: no Armadillo keys exist in this save. No keys will be inserted.")
    ]);

    private static IEnumerable<AstroElement> Descendants(AstroElement element)
    {
        yield return element;
        foreach (var child in element.Children.SelectMany(Descendants))
        {
            yield return child;
        }
    }
}

internal sealed record AstroCollectionRecord(string Group, AstroElement Element, IReadOnlyList<AstroField> Fields)
{
    // Structure never changes in this writer; only the values behind these fields can change.
    public string Key { get; } = $"{Group}/{Element.Name}";
    public AstroField? Ownership { get; } = Flag(Fields, "IsGot");
    public AstroField? Cinematic { get; } = Flag(Fields, "IsCinematicsPlayed");

    private static AstroField? Flag(IReadOnlyList<AstroField> fields, string name)
    {
        AstroField? candidate = null;
        foreach (var field in fields)
        {
            if (field.Element.Name != name) continue;
            if (candidate is not null) return null;
            candidate = field;
        }
        return candidate?.Spec is SaveToolkit.Sdk.Values.BoolSpec && !candidate.Token.IsAttribute &&
            candidate.Element.Children.Count == 0 && candidate.Element.Values.Count == 1 ? candidate : null;
    }
}
