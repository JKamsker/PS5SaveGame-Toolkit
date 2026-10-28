using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.Gearbox;

public sealed class GearboxPlugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("gearbox", "Borderlands 3 / Tiny Tina's Wonderlands", "0.1.0")
    {
        Description = "PS5 GVAS/protobuf character and profile editor. Item serials are read-only.",
        Games = [new GameTitle("PPSA01463", "Borderlands 3"), new GameTitle("PPSA01493", "Tiny Tina's Wonderlands")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/apocalyptech/bl3-cli-saveedit", "https://github.com/apocalyptech/ttwl-cli-saveedit",
            "https://github.com/FromDarkHell/BL3SaveEditor", "https://github.com/xcier/TTWLSaveEditor"],
    };

    public ProbeResult Probe(ISaveFile file) =>
        file.Directory.TitleId is "PPSA01463" or "PPSA01493" && IsRole(file.FileName) && file.ReadHeader(4).AsSpan().SequenceEqual("GVAS"u8)
            ? ProbeResult.Match(ProbeScore.Title, file.FileName == "profile" ? "Gearbox profile" : $"Character slot {file.FileName}")
            : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file)
    {
        if (!IsRole(file.FileName) || file.Length > GearboxContainer.MaximumFileSize)
            throw new SaveFormatException("Unsupported Gearbox file role or size.");
        var container = GearboxContainer.Parse(file.ReadAllBytes(), file.Directory.TitleId, file.FileName == "profile");
        return new GearboxDocument(file.RelativePath, file.Directory.TitleId, container, container.Decode());
    }

    private static bool IsRole(string name) => name == "profile" || (name.Length is > 0 and <= 3 && name.All(char.IsAsciiDigit) && name[0] != '0');
}
