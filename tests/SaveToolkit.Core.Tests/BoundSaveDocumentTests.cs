using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class BoundSaveDocumentTests
{
    [Theory]
    [InlineData("setter")]
    [InlineData("readback")]
    [InlineData("derived")]
    [InlineData("unexpected")]
    public void FailedBatch_RestoresEveryAttemptedEdit(string failure)
    {
        using var document = new FailingDocument(failure);
        _ = document.BuildView();

        var result = document.ApplyEdits([
            new ValueEdit("first", FieldValue.Of(10)),
            new ValueEdit("second", FieldValue.Of(20)),
        ]);

        Assert.False(result.Succeeded);
        Assert.Equal(1, document.GetValue("first").AsInt32());
        Assert.Equal(2, document.GetValue("second").AsInt32());
        Assert.Empty(result.Changes);
        Assert.Equal("second", result.Errors.Single().ValueId);
    }

    [Fact]
    public void SuccessfulBatch_ReportsPrimaryAndDerivedChanges()
    {
        using var document = new FailingDocument(null);
        _ = document.BuildView();

        var result = document.ApplyEdits([new ValueEdit("first", FieldValue.Of(10))]);

        Assert.True(result.Succeeded);
        Assert.Equal(["first", "total"], result.Changes.Select(c => c.ValueId));
        Assert.Equal(1, result.Changes[0].OldValue.AsInt32());
        Assert.Equal(10, result.Changes[0].NewValue.AsInt32());
    }

    private sealed class FailingDocument(string? failure) : BoundSaveDocument
    {
        private int _first = 1;
        private int _second = 2;

        public override string Title => "Atomic binding test";
        public override IReadOnlyList<FileOutput> Serialize() => [];

        public override ViewNode BuildView() => new SectionNode(Title, [
            Field("first", "First", IntegerSpec.Int32, () => FieldValue.Of(_first), v => _first = v.AsInt32()),
            Field("second", "Second", IntegerSpec.Int32, ReadSecond, WriteSecond),
        ]);

        private FieldValue ReadSecond()
        {
            if (_second == 20 && failure == "readback") throw new InvalidOperationException("Readback failed.");
            return FieldValue.Of(_second);
        }

        private void WriteSecond(FieldValue value)
        {
            _second = value.AsInt32();
            if (_second == 20 && failure == "setter") throw new InvalidOperationException("Setter failed after mutation.");
            if (_second == 20 && failure == "unexpected") throw new FormatException("Unexpected setter failure.");
        }

        protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied)
        {
            yield return new ValueChange("total", FieldValue.Of(3), FieldValue.Of(_first + _second));
            if (failure == "derived") throw new InvalidOperationException("Derived enumeration failed.");
        }
    }
}
