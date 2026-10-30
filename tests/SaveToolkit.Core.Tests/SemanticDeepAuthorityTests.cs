using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticDeepAuthorityTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void DeepFreshPoliciesUsePinnedCatalogAndConservativelyRejectDrift(bool rejectOriginal, bool wireDrift, bool unions)
    {
        var plugin = new ItemsPlugin();
        var references = new ReferenceFieldService(new ReferencePackStore([plugin]).Pin(plugin, ItemsPlugin.TitleId));
        using var current = new DeepDocument(rejectOriginal, wireDrift, unions) { Value = "1002" };
        var review = new SemanticReview(new(), references);
        review.CaptureTargetedOriginals(() => new DeepDocument(false, false, unions), ["value"], CancellationToken.None);

        var log = review.Query(() => new DeepDocument(false, false, unions), current, _ => { }, _ => { }, 1, CancellationToken.None);

        if (rejectOriginal || wireDrift)
        {
            Assert.False(log.Complete);
            Assert.DoesNotContain(log.Changes, change => change.CanRevert);
            Assert.Contains(log.Warnings, warning => warning.Contains("authority changed", StringComparison.Ordinal));
        }
        else
        {
            var change = Assert.Single(log.Changes);
            Assert.True(log.Complete);
            Assert.True(change.CanRevert);
            Assert.Equal(DeepDocument.Wrap("1001", unions), change.OriginalValue);
            Assert.Equal(DeepDocument.Wrap("1002", unions), change.CurrentValue);
        }
        Assert.True(current.PolicyCalls > 0 || wireDrift);
    }

    private sealed class DeepDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private readonly bool _rejectOriginal;
        public int PolicyCalls { get; private set; }
        public string Value { get; set; } = "1001";
        public DeepDocument(bool rejectOriginal, bool wireDrift, bool unions)
        {
            _rejectOriginal = rejectOriginal;
            FieldSpec spec = new ReferenceSpec("items") { Assess = Assess, AllowCustom = wireDrift };
            for (var depth = 0; depth < 70; depth++)
                spec = unions && depth % 2 == 0 ? new UnionSpec([new("selected", "Selected", spec)]) : new NullableSpec(spec);
            RegisterField(new FieldMetadata("value", "Value", spec), () => Wrap(Value, unions), value =>
            {
                while (value.Kind == FieldValueKind.Union) value = value.AsUnion().Value;
                Value = value.AsString();
            });
        }
        public static FieldValue Wrap(string text, bool unions)
        {
            var value = FieldValue.Of(text);
            if (unions) for (var depth = 0; depth < 35; depth++) value = FieldValue.Union("selected", value);
            return value;
        }
        private ReferenceEligibility Assess(ReferenceRecord record)
        {
            PolicyCalls++;
            return new() { Eligible = !_rejectOriginal || record.Key != "1001" };
        }
        public override string Title => "Deep authority";
        public override ViewNode BuildView() => new SectionNode(Title, [Field("value", "Value")]);
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
