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
            [openedFile.RelativePath] = openedFile.Sha256,
        };
        foreach (var path in DeclaredPaths)
        {
            var file = _saveSet.FindAnyFile(path) as DiskSaveFile
                ?? throw new SaveFormatException($"Additional output '{path}' does not exist in the opened save directory.");
            using var stream = file.OpenRead();
            var actual = OperationCancellation.HashSha256(stream, CancellationToken.None);
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
            using var stream = file.OpenRead();
            var actual = OperationCancellation.HashSha256(stream, cancellationToken);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Owned output '{path}' changed since the document was opened. Close and reimport the dump.");
        }
    }
}
