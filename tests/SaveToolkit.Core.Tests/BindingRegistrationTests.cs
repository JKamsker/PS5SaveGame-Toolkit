using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class BindingRegistrationTests(ITestOutputHelper output)
{
    private const int BindingCount = 1_000;
    private const int Measurements = 5;
    private const long RegistrationBudget = 100_000;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageRegistrationDoesNotAllocatePresentationNodes(bool readOnly)
    {
        using var document = new RegistrationDocument();
        var ids = Enumerable.Range(0, BindingCount).Select(i => $"field/{i}").ToArray();
        for (var i = 0; i < Measurements; i++) document.RegisterAll(ids, readOnly);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Measurements; i++) document.RegisterAll(ids, readOnly);
        var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / Measurements;

        output.WriteLine($"Read-only={readOnly}: {allocated:N0} bytes per {BindingCount} registrations.");
        Assert.True(allocated <= RegistrationBudget, $"Registration allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void StorageRegistrationDoesNotReadValues()
    {
        using var document = new RegistrationDocument();
        document.RegisterAll(["value"], false);

        Assert.Equal(0, document.Reads);
        Assert.Equal(FieldValue.Of(1), document.GetValue("value"));
        Assert.Equal(1, document.Reads);
    }

    [Fact]
    public void RenderingRetainsSpecAndValidatorAndReadsTheLiveValue()
    {
        using var document = new RegistrationDocument();
        var value = 3;
        var spec = new IntegerSpec(0, 20) { Unit = "items" };
        document.Bind("value", spec, () => FieldValue.Of(value), v => value = v.AsInt32(),
            v => v.AsInt32() == 13 ? "Unlucky." : null);

        Assert.Same(spec, document.Render("value").Spec);
        Assert.True(document.ApplyEdits([new("value", FieldValue.Of(12))]).Succeeded);
        Assert.Equal(FieldValue.Of(12), document.Render("value").Value);
        var rejected = document.ApplyEdits([new("value", FieldValue.Of(13))]);
        Assert.Equal("Unlucky.", Assert.Single(rejected.Errors).Message);
        Assert.Equal(12, value);
    }

    [Fact]
    public void ReadOnlyRegistrationDoesNotMutateTheSuppliedSpec()
    {
        using var document = new RegistrationDocument();
        var spec = new BoolSpec();
        document.Bind("value", spec, () => FieldValue.Of(true));

        Assert.True(document.Render("value").Spec.ReadOnly);
        Assert.False(spec.ReadOnly);
        Assert.Equal("Field is read-only.", Assert.Single(document.ApplyEdits([new("value", FieldValue.Of(false))]).Errors).Message);
    }

    [Fact]
    public void RegisteringAgainReplacesEveryBindingComponent()
    {
        using var document = new RegistrationDocument();
        var oldValue = 1;
        var newValue = 2;
        document.Bind("value", new BoolSpec(), () => FieldValue.Of(oldValue), v => oldValue = v.AsInt32(), _ => "Old validator.");
        var spec = IntegerSpec.Int32;
        document.Bind("value", spec, () => FieldValue.Of(newValue), v => newValue = v.AsInt32());

        Assert.Equal(FieldValue.Of(2), document.GetValue("value"));
        Assert.True(document.ApplyEdits([new("value", FieldValue.Of(4))]).Succeeded);
        Assert.Same(spec, document.Render("value").Spec);
        Assert.Equal(1, oldValue);
        Assert.Equal(4, newValue);
    }

    [Fact]
    public void UnrenderedBindingsStillRollBackASetterThatMutatesThenThrows()
    {
        using var document = new RegistrationDocument();
        var first = 1;
        var second = 2;
        var spec = IntegerSpec.Int32;
        document.Bind("first", spec, () => FieldValue.Of(first), v => first = v.AsInt32());
        document.Bind("second", spec, () => FieldValue.Of(second), v =>
        {
            second = v.AsInt32();
            if (second == 20) throw new InvalidOperationException("Setter failed.");
        });

        var result = document.ApplyEdits([new("first", FieldValue.Of(10)), new("second", FieldValue.Of(20))]);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Changes);
        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralActionsClearStorageBindingsOnSuccessAndFailure(bool fail)
    {
        using var document = new RegistrationDocument();
        document.RegisterAll(["value"], false);

        if (fail) Assert.Throws<InvalidOperationException>(() => document.ChangeStructure(true));
        else _ = document.ChangeStructure(false);

        Assert.Throws<KeyNotFoundException>(() => document.GetValue("value"));
        Assert.Throws<KeyNotFoundException>(() => document.Render("value"));
        document.RegisterAll(["value"], false);
        Assert.Equal(FieldValue.Of(fail ? 1 : 2), document.GetValue("value"));
    }

    [Fact]
    public void MementoRestoreClearsStorageBindingsAndRestoresValues()
    {
        using var document = new RegistrationDocument();
        var changed = document.ChangeStructure(false);
        document.RegisterAll(["value"], false);

        var redo = document.RestoreMemento(changed.Revert!);

        Assert.Throws<KeyNotFoundException>(() => document.Render("value"));
        document.RegisterAll(["value"], false);
        Assert.Equal(FieldValue.Of(1), document.GetValue("value"));
        document.RestoreMemento(redo);
        document.RegisterAll(["value"], false);
        Assert.Equal(FieldValue.Of(2), document.GetValue("value"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingFieldCallersKeepTheirNodeAndBindingBehavior(bool readOnly)
    {
        using var document = new RegistrationDocument();

        var node = document.LegacyField(readOnly);

        Assert.Equal("value", node.ValueId);
        Assert.Equal("Visible value", node.Label);
        Assert.Equal(FieldValue.Of(1), node.Value);
        Assert.Equal(readOnly, node.Spec.ReadOnly);
        Assert.Equal(1, document.Reads);
        Assert.Equal(!readOnly, document.ApplyEdits([new("value", FieldValue.Of(2))]).Succeeded);
    }

    private sealed class RegistrationDocument : BoundSaveDocument
    {
        private static readonly FieldSpec Spec = IntegerSpec.Int32;
        private readonly Func<FieldValue> _get;
        private readonly Action<FieldValue> _set;
        private int _value = 1;

        public RegistrationDocument()
        {
            _get = () => { Reads++; return FieldValue.Of(_value); };
            _set = value => _value = value.AsInt32();
        }

        public int Reads { get; private set; }
        public override string Title => "Registration";
        public override ViewNode BuildView() => new TextNode(Title);
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public void RegisterAll(string[] ids, bool readOnly)
        {
            foreach (var id in ids) RegisterField(id, Spec, _get, readOnly ? null : _set);
        }

        public void Bind(string id, FieldSpec spec, Func<FieldValue> get, Action<FieldValue>? set = null,
            Func<FieldValue, string?>? validate = null) => RegisterField(id, spec, get, set, validate);

        public FieldNode Render(string id) => Field(id, "Visible value");
        public FieldNode LegacyField(bool readOnly) => Field("value", "Visible value", Spec, _get, readOnly ? null : _set);

        public ActionOutcome ChangeStructure(bool fail) => Structural(() =>
        {
            _value++;
            if (fail) throw new InvalidOperationException("Structure failed.");
        });

        protected override DocumentMemento CaptureSnapshot() => new Snapshot(_value);
        protected override void LoadSnapshot(DocumentMemento snapshot) => _value = ((Snapshot)snapshot).Value;
        private sealed class Snapshot(int value) : DocumentMemento
        {
            public int Value { get; } = value;
            public override long SizeBytes => sizeof(int);
        }
    }
}
