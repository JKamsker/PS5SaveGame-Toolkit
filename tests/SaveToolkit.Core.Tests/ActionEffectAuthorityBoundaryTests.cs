using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectAuthorityBoundaryTests
{
    [Fact]
    public void CanonicalClosureMayUseExactlyTwoAuthoritiesPerAdmittedRow()
    {
        using var document = new AuthorityDocument();
        var index = new ViewIndex(document);
        index.Add(document.BuildView());

        var plan = new ActionPlan
        {
            Changes = [new PlannedChange("alias", "Alias", FieldValue.Of(1), FieldValue.Of(2))
            {
                Derived = true,
            }],
        };

        var metadata = FieldMetadataPlanning.Validate(index, document, plan);

        Assert.Equal(["alias", "target"], metadata.Select(field => field.ValueId));
        Assert.Equal(2, document.ResolverCalls);
    }

    [Fact]
    public void CanonicalClosureBeyondTwoAuthoritiesIsRejectedBeforeGetterOrPublication()
    {
        using var document = new AuthorityDocument();
        document.Overflow = true;
        var index = new ViewIndex(document);
        index.Add(document.BuildView());
        var plan = new ActionPlan
        {
            Changes = [new PlannedChange("alias", "Alias", FieldValue.Of(1), FieldValue.Of(2))
            {
                Derived = true,
            }],
        };

        var error = Assert.Throws<InvalidOperationException>(() => FieldMetadataPlanning.Validate(index, document, plan));

        Assert.Contains("bounded", error.Message, StringComparison.OrdinalIgnoreCase);
        // The entire closure must fit before any live getter or publication.
        Assert.Equal(0, document.GetterCalls);
        Assert.Equal(2, document.ResolverCalls);
    }

    private sealed class AuthorityDocument : BoundSaveDocument, IActionEffectDocument, IFieldMetadataDocument
    {
        private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal)
        {
            ["alias"] = 1, ["target"] = 1, ["primary"] = 1,
        };

        public int ResolverCalls { get; private set; }
        public int GetterCalls { get; private set; }
        public bool Overflow { get; set; }
        public override string Title => "Authority";

        public AuthorityDocument()
        {
            RegisterField("alias", IntegerSpec.Int32 with { ReadOnly = true },
                () => { GetterCalls++; return FieldValue.Of(_values["alias"]); });
            RegisterField("target", IntegerSpec.Int32 with { ReadOnly = true },
                () => { GetterCalls++; return FieldValue.Of(_values["target"]); });
            RegisterField("primary", IntegerSpec.Int32,
                () => { GetterCalls++; return FieldValue.Of(_values["primary"]); });
        }

        public override ViewNode BuildView() => new SectionNode(Title,
            [new ActionNode("action", "Action") { SupportsPlanning = true }]);

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            ResolverCalls++;
            return valueIds.Select(id => id switch
            {
                "alias" => new FieldMetadata("alias", "Alias", IntegerSpec.Int32 with { ReadOnly = true })
                {
                    BindingKind = FieldBindingKind.Derived, CanonicalValueId = "target",
                },
                "target" when Overflow => new FieldMetadata("target", "Target", IntegerSpec.Int32 with { ReadOnly = true })
                {
                    BindingKind = FieldBindingKind.Derived, CanonicalValueId = "primary",
                },
                "target" => new FieldMetadata("target", "Target", IntegerSpec.Int32)
                {
                    BindingKind = FieldBindingKind.Primary, CanonicalValueId = "target",
                },
                "primary" => new FieldMetadata("primary", "Primary", IntegerSpec.Int32)
                {
                    BindingKind = FieldBindingKind.Primary, CanonicalValueId = "primary",
                },
                _ => null,
            }).Where(field => field is not null).Select(field => field!).ToArray();
        }

        public DocumentMemento CaptureMemento() => DocumentMemento.Snapshot(_values["primary"], 1);
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => new();
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan) => new();
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
