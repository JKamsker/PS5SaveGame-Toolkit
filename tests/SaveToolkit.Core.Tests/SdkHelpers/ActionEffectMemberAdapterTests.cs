using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class ActionEffectMemberAdapterTests
{
    [Fact]
    public void EffectPathUsesPairedOrdinalCodecInBothDirections()
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("character", document, new DocumentMemberIdentityPolicy
        {
            NavigationPath = static (_, path) => ["character-section", .. path],
            LocalNavigationPath = static (_, path) => path.Count > 0 && path[0] == "character-section"
                ? path.Skip(1).ToArray() : throw new InvalidOperationException("foreign path"),
        });
        var local = new ActionPlan
        {
            Changes = [new PlannedChange("payload", "Payload", FieldValue.Null, FieldValue.Of("raw"))
            {
                OldExists = false,
                Effect = new(ActionEffectKind.StructuralMemento, new TextSpec { ReadOnly = true })
                {
                    Path = ["inventory", "item:0"],
                },
            }],
        };

        var group = member.ToGroupPlan(local);
        Assert.Equal(["character-section", "inventory", "item:0"], group.Changes[0].Effect!.Path);
        var roundTrip = member.ToLocalPlan(group);
        Assert.Equal(local.Changes[0].Effect!.Path, roundTrip.Changes[0].Effect!.Path);
        Assert.Equal("character/payload", group.Changes[0].ValueId);
    }

    [Fact]
    public void UnstablePathCodecFailsClosedInsteadOfStrippingPrefixes()
    {
        using var document = new MemberAdapterDocument();
        var member = new DocumentMemberAdapter("character", document, new DocumentMemberIdentityPolicy
        {
            NavigationPath = static (_, path) => ["group", .. path],
            LocalNavigationPath = static (_, path) => path,
        });
        var plan = new ActionPlan
        {
            Changes = [new PlannedChange("payload", "Payload", FieldValue.Null, FieldValue.Of("x"))
            {
                OldExists = false,
                Effect = new(ActionEffectKind.StructuralMemento, new TextSpec { ReadOnly = true }) { Path = ["local"] },
            }],
        };
        Assert.Throws<InvalidOperationException>(() => member.ToGroupPlan(plan));
    }
}
