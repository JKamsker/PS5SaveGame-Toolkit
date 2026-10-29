using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Sessions;

internal static class OutputOwnershipPolicy
{
    public static IReadOnlyList<string> Resolve(DiskSaveFile openedFile, ISaveDocument document)
    {
        if (document is not IAdditionalOutputOwner owner || owner.AdditionalOutputPaths.Count == 0)
            return [];

        var paths = new List<string>(owner.AdditionalOutputPaths.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in owner.AdditionalOutputPaths)
        {
            ValidateRelativeSystemPath(path);
            if (!seen.Add(path)) throw new SaveFormatException($"Additional output '{path}' is declared more than once.");
            if (string.Equals(path, openedFile.PathInDirectory, StringComparison.OrdinalIgnoreCase))
                throw new SaveFormatException($"Additional output '{path}' is the opened file and cannot be declared twice.");
            if (openedFile.Directory.FindAnyFile(path) is null)
                throw new SaveFormatException($"Additional output '{path}' does not exist in the opened save directory.");
            paths.Add($"{((DiskSaveDirectory)openedFile.Directory).RelativeRoot}/{path}");
        }

        return paths.AsReadOnly();
    }

    public static void ValidateRelativeSystemPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains('*', StringComparison.Ordinal) || path.Contains('?', StringComparison.Ordinal)
            || path.StartsWith('/') || path.EndsWith('/') || path.Contains("//", StringComparison.Ordinal)
            || !path.StartsWith("sce_sys/", StringComparison.OrdinalIgnoreCase)
            || path.Split('/').Any(segment => segment is "." or ".." or ""))
            throw new SaveFormatException($"Additional output '{path}' must be an exact existing path under sce_sys/.");
    }
}
