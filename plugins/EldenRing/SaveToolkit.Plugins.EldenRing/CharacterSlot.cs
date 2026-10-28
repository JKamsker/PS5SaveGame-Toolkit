namespace SaveToolkit.Plugins.EldenRing;

internal sealed record CharacterSlot(int Index, int Offset, uint Version, int? PlayerOffset, int ProfileOffset)
{
    public bool Active => PlayerOffset.HasValue;
}

internal sealed record ParsedSave(uint ProfileVersion, IReadOnlyList<CharacterSlot> Slots)
{
    public bool CanEdit => ProfileVersion == EldenRingLayout.WritableProfileVersion
        && Slots.Any(slot => slot.Active)
        && Slots.Where(slot => slot.Active).All(slot => slot.Version == EldenRingLayout.WritableSlotVersion);
}
