using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Provider effects independent of the host's reported-change filtering.</summary>
internal sealed class ScalarEffectDocument(FieldValue initial, FieldSpec spec) : BoundSaveDocument
{
    private FieldValue _value = initial;
    private long _derived;
    private readonly byte[] _hidden = [0];
    public int Writes { get; private set; }
    public bool Reject { get; init; }
    public bool Derived { get; init; }
    public bool UnreportedBytes { get; init; }
    public bool StructuralEffect { get; init; }
    public bool Memento { get; init; }
    public bool FailSnapshot { get; init; }
    public bool OversizeSnapshot { get; init; }
    public override string Title => "Scalar effect fixture";

    public override ViewNode BuildView() => new SectionNode(Title,
    [
        Field("value", "Value", spec, () => _value, value =>
        {
            Writes++;
            _value = value;
            if (UnreportedBytes) _hidden[0]++;
            if (Derived) _derived++;
        }, _ => Reject ? "Provider rejects the current value." : null),
        Field("readonly", "Read only", spec, () => _value),
        Field("derived", "Derived", IntegerSpec.Int64, () => FieldValue.Of(_derived)),
    ]);

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var outcome = base.ApplyEdits(edits);
        return !outcome.Succeeded ? outcome : outcome with
        {
            StructureChanged = StructuralEffect,
            Revert = Memento ? DocumentMemento.Snapshot(0, 1) : null,
            Warnings = ["Provider notice"],
        };
    }

    protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied) =>
        Derived ? [new("derived", FieldValue.Of(_derived - 1), FieldValue.Of(_derived))] : [];

    public override IReadOnlyList<FileOutput> Serialize()
    {
        if (FailSnapshot) throw new InvalidOperationException("Cannot prove bytes.");
        // Floating-point bits deliberately distinguish +0/-0 despite typed equality.
        var content = _value.Kind == FieldValueKind.Float ? BitConverter.GetBytes(_value.AsDouble())
            : JsonSerializer.SerializeToUtf8Bytes(_value);
        return [new("value.dat", content), new("other.dat", OversizeSnapshot ? new byte[9 * 1024 * 1024] : _hidden),
            new("derived.dat", BitConverter.GetBytes(_derived))];
    }
}
