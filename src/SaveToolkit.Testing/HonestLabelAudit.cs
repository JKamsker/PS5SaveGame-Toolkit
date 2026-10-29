using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Testing;

/// <summary>Mechanical presentation checks only; never proof of semantic or console correctness.</summary>
public enum HonestLabelRule
{
    /// <summary>A Verified field needs an explanation of its evidence.</summary>
    VerifiedEvidence,
    /// <summary>Writes normally carry Experimental plugin maturity; Stable needs human evidence.</summary>
    WriteMaturity,
    /// <summary>A read-only document must not publish writable save controls.</summary>
    ReadOnlyDocument,
    /// <summary>A write control should publish a visible warning.</summary>
    WriteWarning,
}

/// <summary>One advisory about a specific presentation, including action parameter presentations.</summary>
public sealed record HonestLabelFinding(HonestLabelRule Rule, string ControlId, string Message);

/// <summary>Inspects one node. Call for every presentation, not only the first occurrence of a value ID.</summary>
public static class HonestLabelAudit
{
    /// <summary>Action inputs are not document setters. Descriptions are checked for presence, not truth.</summary>
    public static IReadOnlyList<HonestLabelFinding> Inspect(ViewNode node, PluginMaturity maturity, bool canEdit)
    {
        var findings = new List<HonestLabelFinding>();
        switch (node)
        {
            case FieldNode field:
                Evidence(field, field.ValueId);
                if (!field.Spec.ReadOnly) Write(field.ValueId, !string.IsNullOrWhiteSpace(field.Warning));
                break;
            case ActionNode action:
                if (action.Mutates)
                {
                    var warned = !string.IsNullOrWhiteSpace(action.Confirm) ||
                        (action.Tone is Tone.Warning or Tone.Danger && !string.IsNullOrWhiteSpace(action.Description));
                    Write(action.ActionId, warned);
                }
                foreach (var parameter in action.Parameters)
                    Evidence(parameter, action.ActionId + "/parameter/" + parameter.ValueId);
                break;
        }
        return findings;

        void Evidence(FieldNode field, string id)
        {
            if (field.Confidence == Confidence.Verified && string.IsNullOrWhiteSpace(field.Description))
                findings.Add(new(HonestLabelRule.VerifiedEvidence, id, "Verified label has no evidence description."));
        }

        void Write(string id, bool warned)
        {
            if (maturity != PluginMaturity.Experimental)
                findings.Add(new(HonestLabelRule.WriteMaturity, id, "Write is not marked Experimental; Stable requires human in-game evidence."));
            if (!canEdit)
                findings.Add(new(HonestLabelRule.ReadOnlyDocument, id, "Writable save control is published by a read-only document."));
            if (!warned)
                findings.Add(new(HonestLabelRule.WriteWarning, id, "Write control has no local warning/confirmation text; check any document-level notice."));
        }
    }
}
