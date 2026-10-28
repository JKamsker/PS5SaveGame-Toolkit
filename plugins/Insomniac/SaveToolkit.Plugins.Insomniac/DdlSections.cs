using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class DdlSections
{
    internal const uint PreferencesTag = 0x2EE9_1673;
    internal const uint DimensionTag = 0x019C_54B9;
    internal const uint ObjectivesTag = 0x21A6_159F;
    internal const uint AwardsTag = 0xC623_E9CC;
    internal const uint FogTag = 0xE58D_899C;
    internal const string SchemaSource = "https://github.com/neptuwunium/rivet/blob/a631c96ed6455f9873a646825d37883f99defa6e/docs/ddl/rcra_ddl.json";
    internal const string Attribution = "Schema: Neptuwunium / Rivet, a631c96e, CC0-1.0; " + SchemaSource +
        ". PS5 wire types and names are checked against this file; runtime schema offsets are not used.";

    private readonly Dictionary<uint, IReadOnlyDictionary<string, DdlField>> _roots = [];
    private readonly Dictionary<uint, string> _labels = [];

    internal DdlSections(Dat1Container container)
    {
        foreach (var section in container.Sections)
        {
            var strategy = Strategy(section.Tag);
            if (strategy is null) continue;
            if (!_roots.TryAdd(section.Tag, new DdlReader(container, section, strategy.Value.Prefix).Read()))
                throw new SaveFormatException($"Duplicate DDL section 0x{section.Tag:X8}.");
            _labels.Add(section.Tag, strategy.Value.Label);
        }
    }

    internal IReadOnlyDictionary<uint, IReadOnlyDictionary<string, DdlField>> Roots => _roots;
    internal string Label(uint tag) => _labels.GetValueOrDefault(tag, $"0x{tag:X8}");

    private static (string Label, int Prefix)? Strategy(uint tag) => tag switch
    {
        RiftPlayerView.CampaignTag => ("Campaign", 0),
        RiftPlayerView.GoldBoltTag => ("Gold bolt options", 0),
        RiftPlayerView.SpeedrunTag => ("Speedrun", 0),
        PreferencesTag => ("Preferences", 0),
        DimensionTag => ("Dimensions", 0),
        0x1CEA_6CEB => ("Segments", 4),
        ObjectivesTag => ("Objectives", 8),
        0x6DED_1778 => ("Backpack", 0),
        0x9DFA_045F => ("Proximity spawns", 0),
        0x9E13_3672 => ("Globals", 0),
        0xA21A_60FC => ("Activities", 0),
        AwardsTag => ("Awards", 0),
        FogTag => ("Fog of war", 0),
        _ => null,
    };
}
