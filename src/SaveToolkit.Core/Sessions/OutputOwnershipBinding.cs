using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Sessions;

internal sealed class OutputOwnershipBinding
{
    private readonly IReadOnlyDictionary<string, string> _fingerprints;
    private readonly ISaveSet _saveSet;

    public OutputOwnershipBinding(DiskSaveFile openedFile, ISaveDocument document)
    {
        _saveSet = openedFile.Directory.SaveSet;
        DeclaredPaths = OutputOwnershipPolicy.Resolve(openedFile, document);
        var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [openedFile.RelativePath] = HashImportedSource(openedFile),
        };
        foreach (var path in DeclaredPaths)
        {
            var file = _saveSet.FindAnyFile(path) as DiskSaveFile
                ?? throw new SaveFormatException($"Additional output '{path}' does not exist in the opened save directory.");
            var actual = OwnedSourceReader.Hash(file, path, CancellationToken.None);
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Owned output '{path}' changed while the document was opening. Close and reimport the dump.");
            fingerprints.Add(path, file.Sha256);
        }

        _fingerprints = fingerprints;
        OwnedPaths = [openedFile.RelativePath, .. DeclaredPaths];
    }

    public IReadOnlyList<string> DeclaredPaths { get; }
    public IReadOnlyList<string> OwnedPaths { get; }
    public bool RequiresCompleteOutputs => DeclaredPaths.Count > 0;

    public void Check(CancellationToken cancellationToken)
    {
        foreach (var (path, expected) in _fingerprints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = _saveSet.FindAnyFile(path)
                ?? throw new InvalidOperationException($"Owned output '{path}' disappeared; close and reimport the dump.");
            var actual = file is DiskSaveFile disk
                ? OwnedSourceReader.Hash(disk, path, cancellationToken)
                : throw new InvalidOperationException($"Owned output '{path}' could not be read. Close and reimport the dump.");
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Owned output '{path}' changed since the document was opened. Close and reimport the dump.");
        }
    }

    private static string HashImportedSource(DiskSaveFile file)
    {
        var actual = OwnedSourceReader.Hash(file, file.RelativePath, CancellationToken.None);
        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Owned output '{file.RelativePath}' changed while the document was opening. Close and reimport the dump.");
        return file.Sha256;
    }
}
