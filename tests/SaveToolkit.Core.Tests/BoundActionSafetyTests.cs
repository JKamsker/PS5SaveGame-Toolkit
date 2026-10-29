using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class BoundActionSafetyTests
{
    [Theory]
    [InlineData("setter")]
    [InlineData("readback")]
    [InlineData("enumeration")]
    [InlineData("lookup")]
    public void FailedAction_RestoresEveryAttemptedValue(string failure)
    {
        using var document = new ActionDocument(failure);
        _ = document.BuildView();

        if (failure == "lookup")
            Assert.Throws<KeyNotFoundException>(() => document.InvokeAction("assign", new Dictionary<string, FieldValue>()));
        else
            Assert.Throws<InvalidOperationException>(() => document.InvokeAction("assign", new Dictionary<string, FieldValue>()));

        Assert.Equal(1, document.GetValue("first").AsInt32());
        Assert.Equal(2, document.GetValue("second").AsInt32());
    }

    [Fact]
    public void SuccessfulAction_ReportsSequentialChangesWithoutRunningValidators()
    {
        using var document = new ActionDocument(null);
        _ = document.BuildView();

        var outcome = document.InvokeAction("assign", new Dictionary<string, FieldValue>());

        Assert.Equal(["first", "second", "first"], outcome.Changes.Select(c => c.ValueId));
        Assert.Equal(10, outcome.Changes[2].OldValue.AsInt32());
        Assert.Equal(30, document.GetValue("first").AsInt32());
        Assert.Equal(20, document.GetValue("second").AsInt32());
    }

    private sealed class ActionDocument(string? failure) : BoundSaveDocument
    {
        private int _first = 1;
        private int _second = 2;

        public override string Title => "Action atomicity";
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public override ViewNode BuildView() => new SectionNode(Title, [
            Field("first", "First", IntegerSpec.Int32, () => FieldValue.Of(_first),
                value => _first = value.AsInt32(), _ => "Direct edits are rejected."),
            Field("second", "Second", IntegerSpec.Int32, ReadSecond, WriteSecond),
        ]);

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
            new() { Changes = SetValues(Edits()) };

        private IEnumerable<ValueEdit> Edits()
        {
            yield return new("first", FieldValue.Of(10));
            yield return new("second", FieldValue.Of(20));
            yield return new("first", FieldValue.Of(30));
            if (failure == "enumeration") throw new InvalidOperationException("Edit enumeration failed.");
            if (failure == "lookup") yield return new("missing", FieldValue.Of(40));
        }

        private FieldValue ReadSecond()
        {
            if (_second == 20 && failure == "readback") throw new InvalidOperationException("Readback failed.");
            return FieldValue.Of(_second);
        }

        private void WriteSecond(FieldValue value)
        {
            _second = value.AsInt32();
            if (_second == 20 && failure == "setter") throw new InvalidOperationException("Setter failed after mutation.");
        }
    }
}
