using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Two role-checked containers. Duplicate item values have separate, non-persisted identities.</summary>
public sealed class GroupPlugin : IGamePlugin, IDocumentGroupPlugin
{
    public const string PluginId = "test.groups";
    public PluginInfo Info { get; } = new(PluginId, "Grouped containers", "1.0.0")
    { Games = [new("PPSA99990", "Group test")] };

    // Fault injection is test-only; real plugins remain stateless.
    public bool RejectValidation { get; set; }
    public bool RejectApply { get; set; }
    public bool RejectRestore { get; set; }
    public bool DeclareSystemOutput { get; set; }
    public FileOutput? AdditionalOutput { get; set; }
    public int? RequiredOutsideBalance { get; set; }
    public int RoleReopens { get; private set; }
    public Func<ISaveFile, IReadOnlyList<DocumentGroupProposal>>? Proposals { get; set; }
    public List<GroupDocument> Opened { get; } = [];
    public IReferenceContext? LastReferences { get; private set; }

    public ProbeResult Probe(ISaveFile file) => file.FileName is "character.dat" or "profile.dat"
        ? ProbeResult.Match(ProbeScore.Title) : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        LastReferences = references;
        RoleReopens++;
        var (balance, _) = Read(file, file.FileName == "character.dat" ? "C" : "P");
        return new SingleDocument(file, balance, () => AdditionalOutput);
    }

    public IReadOnlyList<DocumentGroupProposal> ProposeGroups(ISaveFile anchor) => Proposals?.Invoke(anchor) ??
        [new("containers", "Character + profile", [new("character", anchor.Sibling("character.dat")?.RelativePath ?? "missing-character"),
            new("profile", anchor.Sibling("profile.dat")?.RelativePath ?? "missing-profile")])];

    public IReadOnlyList<FieldError> ValidateGroup(DocumentGroupInput input, IReferenceContext references)
    {
        LastReferences = references;
        if (RejectValidation) return [new("", "Injected cross-member invariant failure.")];
        if (RequiredOutsideBalance is { } expected && input.Members[0].File.Sibling("outside.dat") is { } outside
            && Read(outside, "P").Balance != expected)
            return [new("", "Group validation did not see the complete final overlay.")];
        foreach (var member in input.Members)
        {
            var (balance, items) = Read(member.File, member.Role == "character" ? "C" : "P");
            if (balance < 0 || items.Length > 10) return [new(member.Role, "Container capacity or balance is invalid.")];
        }
        return [];
    }

    public IGroupedSaveDocument OpenGroup(DocumentGroupInput input, IReferenceContext references)
    {
        LastReferences = references;
        var system = DeclareSystemOutput
            ? input.Members[0].File.Directory.FindAnyFile("sce_sys/param.sfo")
            : null;
        var document = new GroupDocument(input, () => RejectApply, () => RejectRestore,
            system?.RelativePath, system?.ReadAllBytes());
        Opened.Add(document);
        return document;
    }

    public static byte[] Create(string role, int balance, params int[] items) =>
        Encoding.UTF8.GetBytes($"{role}|{balance}|{string.Join(',', items)}");

    internal static (int Balance, int[] Items) Read(ISaveFile file, string expectedRole)
    {
        var parts = Encoding.UTF8.GetString(file.ReadAllBytes()).Split('|');
        if (parts.Length != 3 || parts[0] != expectedRole || !int.TryParse(parts[1], out var balance))
            throw new SaveFormatException($"Invalid {expectedRole} container.");
        return (balance, parts[2].Length == 0 ? [] : parts[2].Split(',').Select(int.Parse).ToArray());
    }

    private sealed class SingleDocument(ISaveFile file, int initial, Func<FileOutput?> additionalOutput) : BoundSaveDocument
    {
        private int _balance = initial;
        public override string Title => file.FileName;
        public override ViewNode BuildView() => Field("balance", "Balance", IntegerSpec.Int32,
            () => FieldValue.Of(_balance), value => _balance = value.AsInt32());
        public override IReadOnlyList<FileOutput> Serialize() => [new(file.RelativePath,
            Create(file.FileName == "character.dat" ? "C" : "P", _balance)),
            .. additionalOutput() is { } extra ? new[] { extra } : []];
    }
}
