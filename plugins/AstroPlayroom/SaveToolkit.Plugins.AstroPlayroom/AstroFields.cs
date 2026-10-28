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

            foreach (var token in element.Values)
            {
                var raw = allocation.Read(token);
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

                var primitive = !token.IsAttribute && element.Children.Count == 0 &&
                    element.Values.Count(v => !v.IsAttribute) == 1;
                var common = primitive && (path is "PreInstall/Common/NumCoin" or "PreInstall/Common/PlayCount") &&
                    spec is IntegerSpec && initial.AsInteger() >= 0 && initial.AsInteger() <= int.MaxValue;
                var flag = primitive && spec is BoolSpec && (group is "Progress" or "World" ||
                    ((group is "Artifact" or "Puzzle" or "Prop") && (element.Name is "IsGot" or "IsCinematicsPlayed")));
                if (common)
                {
                    // A storage limit, not a claim about the game's accepted coin/play-count range.
                    spec = new IntegerSpec(0, int.MaxValue);
                }

                result.Add(new AstroField(element, token, path, group, spec, initial,
                    common ? Confidence.Verified : flag ? Confidence.Inferred : Confidence.Unknown, common || flag));
            }

            foreach (var child in element.Children)
            {
                Visit(child, path, group);
            }
        }
    }
}
