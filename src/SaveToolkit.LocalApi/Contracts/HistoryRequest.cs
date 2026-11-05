namespace SaveToolkit.Api.Contracts;

/// <summary>Optional document revision precondition for undo/redo; no body preserves legacy behavior.</summary>
public sealed record HistoryRequest(long? Revision = null);
