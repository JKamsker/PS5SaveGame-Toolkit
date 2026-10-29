using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class GroupFingerprintTests
{
    // Captured from the private member wrapper before adopting SDK-04. Never regenerate to accept a migration change.
    [Theory]
    [InlineData(false, "B714D1C9E4898EF770C2E51D1D924C0D7A02C5A6854C4FB6A034DA6D16854CA0", "8D8C7AEA9FD9F685AE04FAC28625C9EDEF9E69174D77E18BAE8596A0C846599E", "en-US")]
    [InlineData(true, "75D1838D5794CA1F2AA5FA4F42BE39B4CB55FB441C7E38EE795EDFC0A022DC2A", "BD9ABAEC42D9E986964E8D804BE8CC0EED16E8A88CAF3D59ACE60FD868975FD8", "en-US")]
    [InlineData(false, "B714D1C9E4898EF770C2E51D1D924C0D7A02C5A6854C4FB6A034DA6D16854CA0", "8D8C7AEA9FD9F685AE04FAC28625C9EDEF9E69174D77E18BAE8596A0C846599E", "th-TH")]
    [InlineData(true, "75D1838D5794CA1F2AA5FA4F42BE39B4CB55FB441C7E38EE795EDFC0A022DC2A", "BD9ABAEC42D9E986964E8D804BE8CC0EED16E8A88CAF3D59ACE60FD868975FD8", "th-TH")]
    public void ViewsAndProtocolMatchPreMigrationFingerprints(bool xor, string expectedView, string expectedProtocol,
        string cultureName)
    {
        using var ambientCulture = new TestCulture(CultureInfo.GetCultureInfo(cultureName));
        AssertHistoricalFingerprints(xor, expectedView, expectedProtocol);
        Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
        Assert.Equal(cultureName, CultureInfo.CurrentUICulture.Name);
    }

    private static void AssertHistoricalFingerprints(bool xor, string expectedView, string expectedProtocol)
    {
        // Historical view provenance uses Gregorian attribution dates.
        using var culture = new TestCulture(CultureInfo.InvariantCulture);
        using var fixture = new GroupIdentityFixture(xor);
        var group = fixture.Group;
        var nodes = ViewTraversal.Nodes(group);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(group);
        var pages = queryable.Collections.Select(collection => queryable.Query(collection.Id, new() { Limit = 2 })).ToArray();
        Assert.NotEmpty(nodes.OfType<GraphNode>().Single().Edges);
        Assert.NotEmpty(nodes.OfType<MapNode>().Single().Markers);
        var view = Hash(new { Nodes = nodes, queryable.Collections, Pages = pages });

        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(group);
        var field = nodes.OfType<FieldNode>().Single(node => node.Label == "volume");
        var parameters = new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of("49.00E+00") };
        var scope = new ActionScope([field.ValueId]);
        var plan = planner.PlanAction("global/json/bulk-set", parameters, scope);
        var unknown = planner.PlanAction("global/unknown", parameters, new([]));
        var invalid = planner.PlanAction("global/json/bulk-set", new Dictionary<string, FieldValue>(), scope);
        var skipped = planner.PlanAction("global/json/bulk-set", parameters, new(["global/value/999999"]));
        Assert.NotEmpty(skipped.Errors);
        Assert.Empty(skipped.Changes);
        Assert.Empty(skipped.SkippedRows);
        // #40 intentionally changes only this invalid-key response. Project its old shape into
        // the historical fingerprint so every other protocol field keeps its original baseline.
        var legacySkipped = new ActionPlan
        {
            SkippedRows = [new("global/value/999999", "Unknown or incompatible row for this action.")],
            Warnings = plan.Warnings,
        };
        var empty = planner.PlanAction("global/json/bulk-set", parameters, new([]));
        var inspection = group.InvokeAction("global/json/inspect", new Dictionary<string, FieldValue>());
        var rejected = planner.ApplyActionPlan("global/json/bulk-set", parameters, scope, new());
        var applied = planner.ApplyActionPlan("global/json/bulk-set", parameters, scope, plan);
        Assert.True(applied.Succeeded);
        var protocol = Hash(new
        {
            Plan = plan, Unknown = unknown, Invalid = invalid, Skipped = legacySkipped, Empty = empty,
            Inspection = inspection, Rejected = rejected,
            Applied = new { applied.Changes, applied.Errors, applied.Warnings, applied.StructureChanged },
            Exports = group.Serialize().Select(output => new { output.RelativePath, Hash = Convert.ToHexString(SHA256.HashData(output.Content.Span)) }),
        });
        _ = group.RestoreMemento(applied.Revert!);
        fixture.AssertOriginalBytes(group);
        Assert.True(expectedView == view && expectedProtocol == protocol, $"View: {view}; protocol: {protocol}");
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
