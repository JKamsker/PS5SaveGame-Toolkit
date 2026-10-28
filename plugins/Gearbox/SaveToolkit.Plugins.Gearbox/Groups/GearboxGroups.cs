using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Explicit same-generation pairs, including PS5's separate character/profile directories.</summary>
internal static class GearboxGroups
{
    internal const string CharacterRole = "character";
    internal const string ProfileRole = "profile";
    private const int MaximumAlternatives = 32;

    public static IReadOnlyList<DocumentGroupProposal> Propose(GearboxPlugin plugin, ISaveFile anchor)
    {
        if (plugin.Probe(anchor).Score == ProbeScore.None) return [];
        if (anchor.PathInDirectory != anchor.FileName)
            return [new("unsupported-path", "Character + profile (unsupported path)", [new(
                anchor.FileName == ProfileRole ? ProfileRole : CharacterRole, anchor.RelativePath)])
            { Errors = [new("", "Grouped character/profile files must be at the save-directory root.")] }];
        var files = anchor.Directory.SaveSet.AllFiles.Where(f => SameGeneration(anchor, f)).ToArray();
        var profiles = anchor.FileName == ProfileRole ? [anchor] : files.Where(f => f.PathInDirectory == ProfileRole).ToArray();
        var characters = anchor.FileName == ProfileRole
            ? files.Where(f => f.PathInDirectory == f.FileName && GearboxPlugin.IsCharacter(f.FileName)).ToArray()
            : [anchor];
        if (characters.Length == 0 || profiles.Length == 0 || (long)characters.Length * profiles.Length > MaximumAlternatives)
            return [new("character-profile-unavailable", "Character + profile (unavailable)", [new(
                anchor.FileName == ProfileRole ? ProfileRole : CharacterRole, anchor.RelativePath)])
            { Errors = [new("", (long)characters.Length * profiles.Length > MaximumAlternatives ? $"More than {MaximumAlternatives} partner alternatives; import a narrower file set."
                : "A character and a profile must exist in the same imported set, title, generation and backup status.")] }];

        return characters.OrderBy(f => f.RelativePath, StringComparer.Ordinal).SelectMany(character =>
            profiles.OrderBy(f => f.RelativePath, StringComparer.Ordinal).Select((profile, index) =>
        {
            var members = new[] { new DocumentGroupFile(CharacterRole, character), new(ProfileRole, profile) };
            return new DocumentGroupProposal(character.RelativePath + "|" + profile.RelativePath,
                $"Slot {character.FileName} + profile · generation {anchor.Directory.Generation}" +
                (anchor.Directory.IsBackup ? " · backup files" : "") +
                (profiles.Length > 1 ? $" · unverified profile choice {index + 1}/{profiles.Length}" : ""),
                members.Select(m => new DocumentGroupMember(m.Role, m.File.RelativePath)).ToArray())
            { Errors = Validate(plugin, new(members), IReferenceContext.Empty) };
        })).ToArray();
    }

    public static IReadOnlyList<FieldError> Validate(GearboxPlugin plugin, DocumentGroupInput input, IReferenceContext references)
    {
        if (input.Members.Count != 2 || input.Members.Count(m => m.Role == CharacterRole) != 1 ||
            input.Members.Count(m => m.Role == ProfileRole) != 1)
            return [new("", "Select exactly one character and one profile. Backups are separate explicit pairs.")];
        var character = input.Members.Single(m => m.Role == CharacterRole).File;
        var profile = input.Members.Single(m => m.Role == ProfileRole).File;
        if (!SameGeneration(character, profile) || character.RelativePath.Equals(profile.RelativePath, StringComparison.OrdinalIgnoreCase))
            return [new("", "Character and profile must share the imported set, title, backup status and generation.")];
        if (!GearboxPlugin.IsCharacter(character.FileName) || character.PathInDirectory != character.FileName ||
            profile.FileName != ProfileRole || profile.PathInDirectory != ProfileRole)
            return [new("", "The selected paths do not match their character/profile roles.")];
        foreach (var member in input.Members)
        {
            if (plugin.Probe(member.File).Score == ProbeScore.None)
                return [new(member.Role, "Unsupported Gearbox title, file role or container.")];
            try
            {
                using var document = plugin.Open(member.File, references);
                if (!document.Serialize().Single().Content.Span.SequenceEqual(member.File.ReadAllBytes()))
                    return [new(member.Role, "The member failed byte-identical round-trip validation.")];
            }
            catch (SaveFormatException ex) { return [new(member.Role, ex.Message)]; }
        }
        // No transfer, capacity or mirror-reconciliation rules are asserted by this read-only selection.
        return [];
    }

    private static bool SameGeneration(ISaveFile left, ISaveFile right) =>
        ReferenceEquals(left.Directory.SaveSet, right.Directory.SaveSet) &&
        left.Directory.TitleId == right.Directory.TitleId && left.Directory.Generation == right.Directory.Generation &&
        left.Directory.IsBackup == right.Directory.IsBackup;
}
