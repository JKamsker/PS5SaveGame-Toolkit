using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Gearbox;

public sealed class GearboxPlugin : IGamePlugin, IDocumentGroupPlugin, ISaveDecoderProvider
{
    public IReadOnlyList<ISaveDecoderHook> Decoders { get; } = [new GearboxGameplayDecoder()];

    public PluginInfo Info { get; } = new("gearbox", "Borderlands 3 / Tiny Tina's Wonderlands", "0.5.0")
    {
        Description = "Experimental PS5 serial/progression editing, planned inventory actions and coordinated character/profile transactions. Console validation pending.",
        Games = [new GameTitle("PPSA01463", "Borderlands 3"), new GameTitle("PPSA01493", "Tiny Tina's Wonderlands")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://github.com/apocalyptech/bl3-cli-saveedit", "https://github.com/apocalyptech/ttwl-cli-saveedit",
            "https://github.com/seigler/bl3skills.com/tree/66b0bd08777452abd05e5d9246c27f2a8096ca13",
            "https://github.com/Lothrik/wonderlands-skill-calc/tree/d57e7aa68af13afb50d4b54aa0ac5d501655c5b8",
            "https://mapgenie.io/borderlands-3", "https://mapgenie.io/tiny-tinas-wonderlands",
            "https://github.com/FromDarkHell/BL3SaveEditor", "https://github.com/xcier/TTWLSaveEditor",
            "https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/__init__.py",
            "https://github.com/apocalyptech/ttwl-cli-saveedit/blob/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/ttwlsave/__init__.py",
            "https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/datalib.py",
            "https://github.com/apocalyptech/ttwl-cli-saveedit/blob/1fde90dc43e7f7ee261413d85c366e0a7236dd9b/ttwlsave/datalib.py",
            "https://github.com/apocalyptech/bl3-cli-saveedit/blob/d459b28a7e88f1a5ae0f16118272d8a5383b8b04/bl3save/bl3save.py",
            "https://github.com/iyre/bl4-save-tools/tree/c9be7baec6f1df0c348eaaebbed331d635a1f7a9",
            "https://github.com/Nicnl/borderlands4-serials/tree/285f9e1e03c2a46c5e0c45844a682063f655439b",
            "https://github.com/glacierpiece/borderlands-4-save-utility/tree/3668c8abcbe7a51175243e1b1d495518af514dcb",
            "https://github.com/mi5hmash/Borderlands4SaveDataResigner/tree/31b5a923a1360ba5003fd77e4c89f1f8ed5c7c9c"],
    };

    public ProbeResult Probe(ISaveFile file) =>
        file.Directory.TitleId is "PPSA01463" or "PPSA01493" && IsRole(file.FileName) && file.ReadHeader(4).AsSpan().SequenceEqual("GVAS"u8)
            ? ProbeResult.Match(ProbeScore.Title, file.FileName == "profile" ? "Gearbox profile" : $"Character slot {file.FileName}")
            : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (!IsRole(file.FileName) || file.Length > GearboxContainer.MaximumFileSize)
            throw new SaveFormatException("Unsupported Gearbox file role or size.");
        var container = GearboxContainer.Parse(file.ReadAllBytes(), file.Directory.TitleId, file.FileName == "profile");
        return new GearboxDocument(file.RelativePath, file.Directory.TitleId, container, container.Decode(), references);
    }

    public IReadOnlyList<DocumentGroupProposal> ProposeGroups(ISaveFile anchor) => GearboxGroups.Propose(this, anchor);

    public IReadOnlyList<FieldError> ValidateGroup(DocumentGroupInput input, IReferenceContext references) =>
        GearboxGroups.Validate(this, input, references);

    public IGroupedSaveDocument OpenGroup(DocumentGroupInput input, IReferenceContext references)
    {
        var errors = ValidateGroup(input, references);
        if (errors.Count > 0) throw new SaveFormatException(string.Join(" ", errors.Select(e => e.Message)));
        return new GearboxGroupDocument(input, references);
    }

    internal static bool IsRole(string name) => name == "profile" || IsCharacter(name);
    internal static bool IsCharacter(string name) => name.Length is > 0 and <= 3 && name.All(char.IsAsciiDigit) && name[0] != '0';
}
