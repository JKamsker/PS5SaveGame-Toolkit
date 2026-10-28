using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record ScalarBinding(string Id, string Label, FieldSpec Spec, Func<FieldValue> Get,
    Action<FieldValue>? Set = null, Func<FieldValue, string?>? Validate = null);
