using System.Globalization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed record AstroField(AstroElement Element, AstroValue Token, string Path, string Group,
    FieldSpec Spec, FieldValue Initial, Confidence Confidence, bool Editable)
{
    public string Label => Token.IsAttribute ? $"@{Token.Name}" : Path switch
    {
        "PreInstall/Common/NumCoin" => "Coins",
        "PreInstall/Common/PlayCount" => "Play count",
        _ => Element.Name
    };
}

internal static class AstroFields
{
    public static AstroField? Coins(IReadOnlyList<AstroField> fields)
    {
        var candidates = fields.Where(field => field.Path == "PreInstall/Common/NumCoin").Take(2).ToArray();
        return candidates.Length == 1 && candidates[0].Editable && candidates[0].Spec is IntegerSpec ? candidates[0] : null;
    }

    public static IReadOnlyList<AstroField> Create(AstroAllocation allocation)
    {
        var result = new List<AstroField>();
        Visit(allocation.Root, "", "");
        return result;

        void Visit(AstroElement element, string parent, string group)
        {
            var path = parent.Length == 0 ? element.Name : $"{parent}/{element.Name}";
            if (parent == "PreInstall")
            {
                group = element.Name;
            }

            // Mixed text can contain thousands of comment-separated tokens. Its primitive shape
            // belongs to the element, so do not recount the whole list for every token.
            var primitiveElement = element.Children.Count == 0 && element.Values.Count(value => !value.IsAttribute) == 1;
            foreach (var token in element.Values)
            {
                result.Add(Classify(element, token, allocation.Read(token), path, parent, group, primitiveElement));
            }

            foreach (var child in element.Children)
            {
                Visit(child, path, group);
            }
        }
    }

    private static AstroField Classify(AstroElement element, AstroValue token, string raw,
        string path, string parent, string group, bool primitiveElement)
    {
        FieldSpec spec = new TextSpec();
        var initial = FieldValue.Of(raw);
        if (raw is "true" or "false")
        {
            spec = new BoolSpec();
            initial = FieldValue.Of(raw == "true");
        }
        else if (long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            spec = IntegerSpec.Int64;
            initial = FieldValue.Of(number);
        }

        var primitive = !token.IsAttribute && primitiveElement;
        var common = primitive && (path is "PreInstall/Common/NumCoin" or "PreInstall/Common/PlayCount") &&
            spec is IntegerSpec && initial.AsInteger() >= 0 && initial.AsInteger() <= int.MaxValue;
        // A group-level scalar is unknown data, not a flag beneath that group.
        var flag = primitive && spec is BoolSpec && (parent != "PreInstall" && group is ("Progress" or "World") ||
            ((group is "Artifact" or "Puzzle" or "Prop") && (element.Name is "IsGot" or "IsCinematicsPlayed")));
        if (common)
        {
            // A storage limit, not a claim about the game's accepted coin/play-count range.
            spec = new IntegerSpec(0, int.MaxValue);
        }

        return new AstroField(element, token, path, group, spec, initial,
            common ? Confidence.Verified : flag ? Confidence.Inferred : Confidence.Unknown, common || flag);
    }
}
