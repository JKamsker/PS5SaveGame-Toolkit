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
        var role = Role(plugin, anchor);
        if (role is null) return [];
        var files = anchor.Directory.SaveSet.AllFiles.Where(file => SameGeneration(anchor, file)).ToArray();
        var characters = role == CharacterRole ? [anchor] : files.Where(file => Role(plugin, file) == CharacterRole).ToArray();
        var globals = role == GlobalRole ? [anchor] : files.Where(file => Role(plugin, file) == GlobalRole).ToArray();
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

        return characters.OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .SelectMany(character => globals.OrderBy(file => file.RelativePath, StringComparer.Ordinal).Select(global =>
            {
                var members = new[] { new DocumentGroupMember(CharacterRole, character.RelativePath), new(GlobalRole, global.RelativePath) };
                return new DocumentGroupProposal(character.RelativePath + "|" + global.RelativePath,
                    $"Character + GlobalSaveDataDefault · generation {anchor.Directory.Generation}" +
                    (anchor.Directory.IsBackup ? " · backup files" : ""), members)
                { Errors = Validate(plugin, new([new(CharacterRole, character), new(GlobalRole, global)]), IReferenceContext.Empty) };
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
        if (Role(plugin, character) != CharacterRole || Role(plugin, global) != GlobalRole)
            return [new("", "The selected files do not match the character/global roles.")];
        foreach (var member in input.Members)
        {
            try
            {
                using var document = plugin.Open(member.File, references);
                var output = document.Serialize();
                if (output.Count != 1 || output[0].RelativePath != member.File.RelativePath ||
                    !output[0].Content.Span.SequenceEqual(member.File.ReadAllBytes()))
                    return [new(member.Role, "The member failed byte-identical round-trip validation.")];
            }
            catch (SaveFormatException ex) { return [new(member.Role, ex.Message)]; }
        }
        return [];
    }

    internal static string? Role(XorJsonPlugin plugin, ISaveFile file)
    {
        try
        {
            using var document = plugin.Open(file, IReferenceContext.Empty);
            return document is JsonSaveDocument json
                ? json.IsCharacter && file.Directory.Name.StartsWith("Character", StringComparison.Ordinal) ? CharacterRole
                : json.IsGlobalSettings && file.Directory.Name == "GlobalSaveDataDefault" ? GlobalRole : null
                : null;
        }
        catch (SaveFormatException) { return null; }
    }

    private static bool SameGeneration(ISaveFile left, ISaveFile right) =>
        ReferenceEquals(left.Directory.SaveSet, right.Directory.SaveSet) &&
        left.Directory.TitleId == right.Directory.TitleId &&
        left.Directory.Generation == right.Directory.Generation &&
        left.Directory.IsBackup == right.Directory.IsBackup;
}
