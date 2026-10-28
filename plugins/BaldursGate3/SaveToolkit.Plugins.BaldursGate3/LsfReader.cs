namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>LSOF v5–v7: section order and compact/adjacency layouts follow LSLib.</summary>
internal static class LsfReader
{
    internal static LarianResource Read(byte[] bytes, ResourceBudget? budget = null)
    {
        var binary = LsfBinary.Read(bytes, budget);
        var model = LsfTables.Read(LsfTables.ReadNames(binary.Data(LsfLayout.StringsPair)),
            binary.Data(LsfLayout.NodesPair), binary.Data(LsfLayout.AttributesPair), binary.Data(LsfLayout.ValuesPair),
            binary.Version >= 6 ? binary.Data(LsfLayout.KeysPair) : [], binary.Metadata == 1);
        return model with { Format = $"LSOF v{binary.Version} · metadata {binary.Metadata}" };
    }
}
