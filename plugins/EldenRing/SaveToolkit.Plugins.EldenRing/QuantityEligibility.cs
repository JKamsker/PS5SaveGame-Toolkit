namespace SaveToolkit.Plugins.EldenRing;

// Opening-time policy only: reference names and current edited quantities cannot grant eligibility.
internal sealed record QuantityEligibility(uint Ceiling, bool DocumentCanEdit, string? Restriction)
{
    private const uint RowaFruit = 0x400050F0;
    private const uint SmithingStoneOne = 0x40002774;
    private const uint ObservedRowaFruit = 12;
    private const uint ObservedSmithingStoneOne = 3;
    public const string EditableStatus = "Editable quantity";
    public const string ProtectedStatus = "Protected";
    public const string ReadOnlyStatus = "Read-only version";

    public bool IsEligible => Restriction is null;
    public bool CanEdit => DocumentCanEdit && IsEligible;
    public string Status => !DocumentCanEdit ? ReadOnlyStatus : IsEligible ? EditableStatus : ProtectedStatus;
    public string Description => (DocumentCanEdit ? "" : "Read-only: this character/profile version has not been verified for writing. ")
        + (IsEligible
            ? FormattableString.Invariant($"Observed quantity range: 1–{Ceiling}. Eligibility and ceiling are fixed when opened; legal carry/storage caps are unverified.")
            : "Protected: " + Restriction);

    public static QuantityEligibility Evaluate(InventoryEntry entry, InventoryContainer container, int handleCount, bool canEdit)
    {
        // Conservative observed values, NOT PARAM-derived legal stack caps. Preserve the existing two-ID policy.
        var ceiling = entry.ItemId switch { RowaFruit => ObservedRowaFruit, SmithingStoneOne => ObservedSmithingStoneOne, _ => 0U };
        var restrictions = new List<string>();
        if (container.Warnings.Count != 0)
            restrictions.Add("Container declared/observed row counts disagree. " + string.Join(" ", container.Warnings));
        if (entry.IsEmpty)
            restrictions.Add("All-zero vacant slot; quantity editing cannot insert a record.");
        else
        {
            if (entry.Group != "common") restrictions.Add($"Serialized array '{entry.Group}' is outside common-array quantity editing.");
            if (entry.Category != "goods") restrictions.Add($"Resolved category '{entry.Category}' is outside goods-only quantity editing.");
            if (entry.Resolution != "direct") restrictions.Add($"Identity resolution '{entry.Resolution}' is not direct.");
            if (ceiling == 0) restrictions.Add(entry.ItemId is null
                ? "Item identity is unresolved; no observed quantity ceiling is established."
                : "Exact item ID is outside the two-ID observed-ceiling allowlist; its quantity/dependency rules are unverified.");
            if (entry.Quantity == 0) restrictions.Add("Imported quantity is zero; quantity editing cannot create or remove a stack.");
            if (ceiling > 0 && entry.Quantity > ceiling)
                restrictions.Add(FormattableString.Invariant($"Imported quantity {entry.Quantity} exceeds the conservative observed ceiling {ceiling}."));
            if (handleCount != 1) restrictions.Add("Handle is repeated within this container; quantity editing requires a unique handle.");
        }
        return new(ceiling, canEdit, restrictions.Count == 0 ? null : string.Join(" ", restrictions));
    }
}
