namespace SaveToolkit.Sdk.Values;

/// <summary>Explicit null or a value conforming to Inner. Null never means a missing property.</summary>
public sealed record NullableSpec(FieldSpec Inner) : FieldSpec;

/// <summary>A named type choice. Id is stable and unique within the enclosing union.</summary>
public sealed record UnionOption(string Id, string Label, FieldSpec Spec);

/// <summary>
/// An explicit choice among inner specs. Values use FieldValue.Union(optionId, value), so choices
/// with identical scalar wire shapes (such as integer and text) stay distinguishable.
/// Wrap in NullableSpec to allow a null independently of the chosen type.
/// </summary>
public sealed record UnionSpec(IReadOnlyList<UnionOption> Options) : FieldSpec;

/// <summary>An explicit union option ID and its typed inner value, retained even when options share the same wire shape.</summary>
public sealed record UnionFieldValue(string Type, FieldValue Value);
