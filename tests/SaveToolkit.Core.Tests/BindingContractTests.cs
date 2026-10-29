using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class BindingContractTests
{
    [Fact]
    public void BindingMetadata_PreservesPublishedSpecsAndKeepsMissingSettersReadOnly()
    {
        using var document = new Document();
        var nodes = ((SectionNode)document.BuildView()).Children.Cast<FieldNode>().ToArray();

        Assert.Same(document.Spec, nodes[0].Spec);
        Assert.False(document.Spec.ReadOnly);
        Assert.Equal(document.Spec with { ReadOnly = true }, nodes[1].Spec);
        Assert.Equal(FieldValue.Of(3), document.GetValue("read-only"));
        Assert.Equal("Field is read-only.", Assert.Single(document.ApplyEdits([new("read-only", FieldValue.Of(4))]).Errors).Message);
        Assert.Equal("reserved value", Assert.Single(document.ApplyEdits([new("editable", FieldValue.Of(4))]).Errors).Message);
        Assert.Equal(FieldValue.Of(3), document.GetValue("editable"));

        var outcome = document.ApplyEdits([new("editable", FieldValue.Of(5))]);
        Assert.True(outcome.Succeeded);
        Assert.Equal(new ValueChange("editable", FieldValue.Of(3), FieldValue.Of(5)), Assert.Single(outcome.Changes));
    }

    private sealed class Document : BoundSaveDocument
    {
        private int _value = 3;
        public IntegerSpec Spec { get; } = IntegerSpec.UInt8 with { Max = 10, Unit = "items" };
        public override string Title => "Bindings";
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override ViewNode BuildView() => new SectionNode(Title, [
            Field("editable", "Editable", Spec, () => FieldValue.Of(_value), v => _value = v.AsInt32(),
                v => v.AsInt32() == 4 ? "reserved value" : null),
            Field("read-only", "Read-only", Spec, () => FieldValue.Of(_value)),
        ]);
    }
}
