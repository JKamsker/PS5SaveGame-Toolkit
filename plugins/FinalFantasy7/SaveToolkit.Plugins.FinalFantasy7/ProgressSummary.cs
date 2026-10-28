using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed class ProgressSummary(ReferenceData references, Func<ScalarField, FieldNode> bind)
{
    private const int RelationshipOffset = 0xA8;
    private const int CharacterStride = 0x100;
    private const int CraftsmanshipOffset = 0x3DB8;

    public ViewNode Build() => new SectionNode("Relationship and craftsmanship candidates",
    [
        new TextNode("Read-only source candidates. Relationship values are not percentages. Craftsmanship level and XP share one source address; only the raw word is shown, without asserting two independent values.") { Tone = Tone.Info },
        bind(new("research/craftsmanship/raw", "Craftsmanship raw word", CraftsmanshipOffset, IntegerSpec.UInt32,
            Confidence.Inferred, Warning: EmbeddedCatalogs.Attribution + " No independent rank/XP or ingredient writer exists.")),
        .. Enumerable.Range(1, 5).Select(character => bind(new($"research/relationship/{character}", references.Character(character) + " relationship raw value",
            RelationshipOffset + character * CharacterStride, IntegerSpec.UInt32, Confidence.Inferred,
            Warning: references.CharacterSource(character) + " " + EmbeddedCatalogs.Attribution + " Event contributions, caps and gameplay effects are unverified."))),
    ]);
}
