namespace SaveToolkit.Core.Sessions;

public sealed partial class DocumentSession
{
    private SessionEditResult? ResolveEditMetadata(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        if (!_index.HasFieldMetadata) return null;
        try
        {
            _index.ResolveMetadata(ids, cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return Rejected(string.Empty, $"Field metadata failed: {ex.Message}");
        }
    }
}
