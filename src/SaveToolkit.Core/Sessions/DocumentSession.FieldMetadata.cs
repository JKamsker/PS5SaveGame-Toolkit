namespace SaveToolkit.Core.Sessions;

public sealed partial class DocumentSession
{
    private SessionEditResult? ResolveEditMetadata(IEnumerable<string> ids)
    {
        if (!_index.HasFieldMetadata) return null;
        try
        {
            _index.ResolveMetadata(ids);
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Rejected(string.Empty, $"Field metadata failed: {ex.Message}");
        }
    }
}
