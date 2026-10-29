using SaveToolkit.Sdk;

namespace SaveToolkit.Testing;

/// <summary>Opt-in advisory single-case probe. Uses a fresh disposable document and never applies a plan.</summary>
public static class SelectionScopeFixture
{
    /// <summary>
    /// Opens and stages an isolated document, walks the complete bounded view, then plans twice.
    /// Ownership is explicit. Unknown effects, rejected plans, no meaningful row effects, missing
    /// expectations and traversal cuts are NotEvaluated. No automatic conformance fact is installed.
    /// </summary>
    /// <param name="openDocument">Creates a fresh, independently owned document; the fixture disposes it.</param>
    /// <param name="fixture">Deterministic inputs, staging and ownership evidence.</param>
    /// <param name="limits">Optional traversal limits.</param>
    public static SelectionScopeResult Evaluate(Func<ISaveDocument> openDocument, SelectionScopeCase fixture, SelectionScopeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(openDocument);
        ArgumentNullException.ThrowIfNull(fixture);
        limits ??= new();
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxNodes);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxLazyExpansions);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxQueryPages);
        if (fixture.Stage is null || fixture.ExpectedChangedValueIds.Count == 0 || fixture.RowOwnedValueIds.Count == 0)
            return Result(SelectionScopeStatus.NotEvaluated, "Staging, expected row changes and ownership are required.");
        try
        {
            using var document = openDocument();
            _ = document.BuildView();
            fixture.Stage(document);
            var incomplete = new SelectionScopeTraversal(document, limits).Check(fixture);
            if (incomplete is not null) return Result(SelectionScopeStatus.NotEvaluated, incomplete);
            if (document is not IActionPlanningDocument planning)
                return Result(SelectionScopeStatus.NotEvaluated, "Document does not support planning.");
            return Probe(document, planning, fixture);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A failed preparation/planning attempt is no evidence of selection correctness.
            return Result(SelectionScopeStatus.NotEvaluated, $"Preparation or planning failed ({error.GetType().Name}).");
        }
    }

    private static SelectionScopeResult Probe(ISaveDocument document, IActionPlanningDocument planning, SelectionScopeCase fixture)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (row, ids) in fixture.RowOwnedValueIds)
            foreach (var id in ids)
                if (!owners.TryAdd(id, row)) return Result(SelectionScopeStatus.NotEvaluated, "Value ownership is ambiguous.");
        var selected = fixture.RowKeys.ToHashSet(StringComparer.Ordinal);
        if (fixture.ExpectedChangedValueIds.Any(id => !owners.TryGetValue(id, out var row) || !selected.Contains(row)))
            return Result(SelectionScopeStatus.NotEvaluated, "Expected changes must have known selected-row ownership.");
        var before = Snapshot(document);
        var values = owners.Keys.ToDictionary(id => id, document.GetValue, StringComparer.Ordinal);
        var scope = new ActionScope(fixture.RowKeys.ToArray());
        var parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Sdk.Values.FieldValue>(
            fixture.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        var first = planning.PlanAction(fixture.ActionId, parameters, scope);
        // Snapshot each plan before the next call: a plugin may reuse mutable list storage.
        var firstCopy = System.Text.Json.JsonSerializer.Serialize(first, Core.ToolkitJson.Options);
        if (!Unchanged(document, before, values)) return Result(SelectionScopeStatus.Failed, "Planning mutated the staged document.");
        var second = planning.PlanAction(fixture.ActionId, parameters, new(fixture.RowKeys.ToArray()));
        if (!Unchanged(document, before, values)) return Result(SelectionScopeStatus.Failed, "Planning mutated the staged document.");
        if (firstCopy != System.Text.Json.JsonSerializer.Serialize(second, Core.ToolkitJson.Options))
            return Result(SelectionScopeStatus.Failed, "Planning is not deterministic.");
        if (first.Errors.Count != 0) return Result(SelectionScopeStatus.NotEvaluated, "The staged plan was rejected.");
        if (first.Changes.Any(change => !owners.ContainsKey(change.ValueId) &&
            !fixture.PermittedSharedValueIds.Contains(change.ValueId, StringComparer.Ordinal) &&
            !fixture.PermittedDerivedValueIds.Contains(change.ValueId, StringComparer.Ordinal)))
            return Result(SelectionScopeStatus.NotEvaluated, "The plan has effects with unknown ownership.");
        if (first.Changes.Any(change => owners.TryGetValue(change.ValueId, out var row) && !selected.Contains(row)))
            return Result(SelectionScopeStatus.Failed, "The plan affects a known unselected row.");
        if (first.Changes.Any(change => !owners.ContainsKey(change.ValueId) &&
            !fixture.PermittedSharedValueIds.Contains(change.ValueId, StringComparer.Ordinal) && !change.Derived))
            return Result(SelectionScopeStatus.Failed, "An allowed derived-only effect was not marked Derived.");
        var meaningful = first.Changes.Where(change => change.OldExists != change.NewExists || change.OldValue != change.NewValue)
            .Select(change => change.ValueId).ToHashSet(StringComparer.Ordinal);
        if (meaningful.Count == 0) return Result(SelectionScopeStatus.NotEvaluated, "Only no-op plans were observed.");
        if (fixture.ExpectedChangedValueIds.Any(id => !meaningful.Contains(id)))
            return Result(SelectionScopeStatus.Failed, "The plan omitted an expected meaningful selected-row change.");
        return Result(SelectionScopeStatus.Passed, "This staged advisory case satisfied its explicit ownership expectations; browser scope wiring still needs integration coverage.");
    }

    private static Dictionary<string, byte[]> Snapshot(ISaveDocument document) =>
        document.Serialize().ToDictionary(output => output.RelativePath, output => output.Content.ToArray(), StringComparer.Ordinal);

    private static bool Unchanged(ISaveDocument document, Dictionary<string, byte[]> before, Dictionary<string, Sdk.Values.FieldValue> values)
    {
        var after = Snapshot(document);
        return before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var bytes) && pair.Value.AsSpan().SequenceEqual(bytes)) &&
            values.All(pair => document.GetValue(pair.Key) == pair.Value);
    }

    private static SelectionScopeResult Result(SelectionScopeStatus status, string diagnostic) => new(status, [diagnostic]);
}
