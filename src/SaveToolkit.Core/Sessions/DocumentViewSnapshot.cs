using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Sessions;

/// <summary>A document view and response metadata captured at the same revision.</summary>
public sealed record DocumentViewSnapshot(string Title, bool CanEdit, DocumentState State, ViewNode View);
