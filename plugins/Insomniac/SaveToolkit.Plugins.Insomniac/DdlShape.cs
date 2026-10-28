using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>Optional diagnostic shapes remain unresolved when unfamiliar, instead of throwing LINQ errors.</summary>
internal static class DdlShape
{
    internal static string? Text(DdlField? field) => field is { Values.Count: 1 } &&
        field.Values[0].Kind == FieldValueKind.Text ? field.Values[0].AsString() : null;

    internal static IReadOnlyDictionary<string, DdlField>? Fields(DdlField? field) =>
        field is { Objects.Count: 1 } ? field.Objects[0].Fields : null;
}
