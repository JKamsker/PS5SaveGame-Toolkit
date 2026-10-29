using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.XorJson;

internal static class XorJsonGroups
{
    internal const string CharacterRole = "character";
    internal const string GlobalRole = "global";
    private const int MaximumAlternatives = 16;

    public static IReadOnlyList<DocumentGroupProposal> Propose(XorJsonPlugin plugin, ISaveFile anchor)
    {
        // Inputs are immutable. Reuse checks only within this operation, never across
        // sessions or reference contexts, and still validate each complete no-op output.
        var checks = new Dictionary<ISaveFile, MemberCheck>();
        MemberCheck Check(ISaveFile file)
        {
            if (!checks.TryGetValue(file, out var check))
                checks.Add(file, check = CheckMember(plugin, file, IReferenceContext.Empty));
            return check;
        }
        var role = Check(anchor).Role;
        if (role is null) return [];
        var files = anchor.Directory.SaveSet.AllFiles.Where(file => SameGeneration(anchor, file)).ToArray();
        var characters = role == CharacterRole ? [anchor] : files.Where(file =>
            DirectoryRole(file) == CharacterRole && Check(file).Role == CharacterRole).ToArray();
        var globals = role == GlobalRole ? [anchor] : files.Where(file =>
            DirectoryRole(file) == GlobalRole && Check(file).Role == GlobalRole).ToArray();
        if (characters.Length == 0 || globals.Length == 0 || (long)characters.Length * globals.Length > MaximumAlternatives)
        {
            var member = role == CharacterRole ? new DocumentGroupMember(CharacterRole, anchor.RelativePath) :
                new DocumentGroupMember(GlobalRole, anchor.RelativePath);
            var reason = (long)characters.Length * globals.Length > MaximumAlternatives
                ? $"More than {MaximumAlternatives} character/global alternatives exist; import a narrower file set."
                : "A character and GlobalSaveDataDefault file must share title, generation and backup status.";
            return [new("character-global-unavailable", "Character + global (unavailable)", [member])
            { Errors = [new("", reason)] }];
        }

        var orderedGlobals = globals.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
        return characters.OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .SelectMany(character => orderedGlobals.Select(global =>
            {
                var members = new[] { new DocumentGroupMember(CharacterRole, character.RelativePath), new(GlobalRole, global.RelativePath) };
                return new DocumentGroupProposal(character.RelativePath + "|" + global.RelativePath,
                    $"Character + GlobalSaveDataDefault · generation {anchor.Directory.Generation}" +
                    (anchor.Directory.IsBackup ? " · backup files" : ""), members)
                { Errors = Check(character).Errors.Count > 0 ? Check(character).Errors : Check(global).Errors };
            })).ToArray();
    }

    public static IReadOnlyList<FieldError> Validate(XorJsonPlugin plugin, DocumentGroupInput input, IReferenceContext references)
    {
        if (input.Members.Count != 2 || input.Members.Count(member => member.Role == CharacterRole) != 1 ||
            input.Members.Count(member => member.Role == GlobalRole) != 1)
            return [new("", "Select exactly one character and one GlobalSaveDataDefault member.")];
        var character = input.Members.Single(member => member.Role == CharacterRole).File;
        var global = input.Members.Single(member => member.Role == GlobalRole).File;
        if (!SameGeneration(character, global))
            return [new("", "Character and global files must share title, imported generation and backup status.")];
        var checks = input.Members.Select(member => CheckMember(plugin, member.File, references)).ToArray();
        if (checks.Where((check, index) => check.Role != input.Members[index].Role).Any())
            return [new("", "The selected files do not match the character/global roles.")];
        return checks.FirstOrDefault(check => check.Errors.Count > 0)?.Errors ?? [];
    }

    private static MemberCheck CheckMember(XorJsonPlugin plugin, ISaveFile file, IReferenceContext references)
    {
        string? role = null;
        try
        {
            if (DirectoryRole(file) is not { } directoryRole) return new(null, []);
            using var document = (JsonSaveDocument)plugin.Open(file, references);
            role = document.IsCharacter && directoryRole == CharacterRole ? CharacterRole
                : document.IsGlobalSettings && directoryRole == GlobalRole ? GlobalRole : null;
            if (role is null) return new(null, []);
            var output = document.Serialize();
            return output.Count == 1 && output[0].RelativePath == file.RelativePath &&
                output[0].Content.Span.SequenceEqual(file.ReadAllBytes())
                ? new(role, []) : new(role, [new(role, "The member failed byte-identical round-trip validation.")]);
        }
        catch (SaveFormatException ex) { return new(role, [new(role ?? "", ex.Message)]); }
    }

    private static string? DirectoryRole(ISaveFile file) => file.Directory.Name.StartsWith("Character", StringComparison.Ordinal)
        ? CharacterRole : file.Directory.Name == "GlobalSaveDataDefault" ? GlobalRole : null;

    private static bool SameGeneration(ISaveFile left, ISaveFile right) =>
        ReferenceEquals(left.Directory.SaveSet, right.Directory.SaveSet) &&
        left.Directory.TitleId == right.Directory.TitleId &&
        left.Directory.Generation == right.Directory.Generation &&
        left.Directory.IsBackup == right.Directory.IsBackup;

    private sealed record MemberCheck(string? Role, IReadOnlyList<FieldError> Errors);
}
