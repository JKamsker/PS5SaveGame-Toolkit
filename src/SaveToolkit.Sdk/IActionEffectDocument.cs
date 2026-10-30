namespace SaveToolkit.Sdk;

/// <summary>
/// Opt-in capability for documents that publish complete metadata for action-owned consequences.
/// Implementations must capture all persisted and identity state in a reusable, size-accounted token.
/// </summary>
public interface IActionEffectDocument : IActionPlanningDocument, IFieldMetadataDocument
{
    /// <summary>Captures complete persisted and identity state for whole-action undo and redo.</summary>
    DocumentMemento CaptureMemento();
}
