using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class GwentCopies(GwentCard card)
{
    private const int ObservedOrdinaryMaximum = 3;
    private const int ObservedLeaderMaximum = 1;
    public GwentCard Card { get; } = card;
    public int Value { get; set; } = card.Copies;
    public int Maximum { get; } = Math.Max(card.Copies, card.Leader ? ObservedLeaderMaximum : ObservedOrdinaryMaximum);
    public string ValueId => $"gwent/collection/{Card.Index}/copies";
    public Sav3Patch? Patch() => Value == Card.Copies ? null :
        new Sav3Patch(Card.CopiesOffset, sizeof(int), BinaryData.EncodeInt32(Value));
}
