using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GroupProtocolTests
{
    private static readonly Dictionary<string, FieldValue> Empty = [];

    // Captured against the private member mapper before adopting SDK-04. Synthetic data only.
    [Theory]
    [InlineData(false, "07CADF9C05CA74AFEEF05D495B9F0BDDDE4C2C237E4E5A869921030057C1CEF5", "en-US")]
    [InlineData(true, "C9BAE4BE585835EA1F3C518BFBD558238763DDBF0C50C253CFF5DDF02B3B22D5", "en-US")]
    [InlineData(false, "07CADF9C05CA74AFEEF05D495B9F0BDDDE4C2C237E4E5A869921030057C1CEF5", "th-TH")]
    [InlineData(true, "C9BAE4BE585835EA1F3C518BFBD558238763DDBF0C50C253CFF5DDF02B3B22D5", "th-TH")]
    public void ViewsPlansAndExport_RetainPreAdapterFingerprint(bool wonderlands, string expected, string cultureName)
    {
        using var ambientCulture = new TestCulture(CultureInfo.GetCultureInfo(cultureName));
        AssertSyntheticFingerprint(wonderlands, expected);
        Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
        Assert.Equal(cultureName, CultureInfo.CurrentUICulture.Name);
    }

    private static void AssertSyntheticFingerprint(bool wonderlands, string expected)
    {
        // Historical view provenance uses Gregorian attribution dates.
        using var culture = new TestCulture(CultureInfo.InvariantCulture);
        using var group = Open(wonderlands);
        var transcript = new List<string>();
        Capture(group, transcript);
        var initial = group.CaptureMemento();
        var edits = group.ApplyEdits([new("character/ammo/0", FieldValue.Of(1.0))]);
        Assert.True(edits.Succeeded);
        RecordOutcome(transcript, edits);
        Apply(group, transcript, "character/ammo/restore-imported", Empty, new(["character/ammo/0"]));
        Apply(group, transcript, "character/items/match-level", Empty, new(["character/inventory_items/item-0"]));
        Apply(group, transcript, "character/items/mark", new Dictionary<string, FieldValue> { ["mark"] = FieldValue.Of(2) },
            new(["character/inventory_items/item-0", "character/inventory_items/item-1"]));
        // Includes unchanged skipped-row and general-error identities as well as successful plans.
        transcript.Add(JsonSerializer.Serialize(group.PlanAction("character/items/match-level", Empty, new(["character/inventory_items/item-2"]))));
        transcript.Add(JsonSerializer.Serialize(group.PlanAction("profile/items/match-level", Empty, new([]))));
        Capture(group, transcript);
        var redo = group.RestoreMemento(initial);
        Capture(group, transcript);
        _ = group.RestoreMemento(redo);
        Capture(group, transcript);
        Assert.Equal(expected, Hash(JsonSerializer.SerializeToUtf8Bytes(transcript)));
    }

    [Theory]
    [InlineData("PPSA01463", "754A3F4DDE9882B8AD16D995190AA8D55D69455D5A7484A844BE383F82C71D0C", "en-US")]
    [InlineData("PPSA01493", "145AC55D2DD596EEA0C096D82B8CA0D5D9F557A4E985BE5834DE8D5D7B99B254", "en-US")]
    [InlineData("PPSA01463", "754A3F4DDE9882B8AD16D995190AA8D55D69455D5A7484A844BE383F82C71D0C", "th-TH")]
    [InlineData("PPSA01493", "145AC55D2DD596EEA0C096D82B8CA0D5D9F557A4E985BE5834DE8D5D7B99B254", "th-TH")]
    public void SampleGenerationsAndBackups_RetainPreAdapterFingerprint(string title, string expected, string cultureName)
    {
        using var ambientCulture = new TestCulture(CultureInfo.GetCultureInfo(cultureName));
        AssertSampleFingerprint(title, expected);
        Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
        Assert.Equal(cultureName, CultureInfo.CurrentUICulture.Name);
    }

    private static void AssertSampleFingerprint(string title, string expected)
    {
        using var culture = new TestCulture(CultureInfo.InvariantCulture);
        var set = SampleSaves.Load(title);
        var plugin = new GearboxPlugin();
        var transcript = new List<string>();
        var characters = set.AllDiskFiles.Where(file => file.FileName == "1")
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
        Assert.Equal(6, characters.Length);
        foreach (var character in characters)
        {
            using var group = (GearboxGroupDocument)plugin.OpenGroup(
                GearboxGroupTests.Input(set, Assert.Single(plugin.ProposeGroups(character))), IReferenceContext.Empty);
            Capture(group, transcript);
        }
        Assert.Equal(expected, Hash(JsonSerializer.SerializeToUtf8Bytes(transcript)));
    }

    internal static GearboxGroupDocument Open(bool wonderlands)
    {
        using var character = EditCoverageData.Create(wonderlands);
        using var profile = EditCoverageData.Create(wonderlands, profile: true);
        var set = new MemorySaveSet().With(wonderlands ? "PPSA01493" : "PPSA01463", "TEST", new Dictionary<string, byte[]>
        {
            ["1"] = character.Serialize().Single().Content.ToArray(),
            ["profile"] = profile.Serialize().Single().Content.ToArray(),
        });
        return (GearboxGroupDocument)new GearboxPlugin().OpenGroup(new(((ISaveSet)set).AllFiles.Select(file =>
            new DocumentGroupFile(file.FileName == "profile" ? "profile" : "character", file)).ToArray()), IReferenceContext.Empty);
    }

    private static void Apply(GearboxGroupDocument group, List<string> transcript, string action,
        IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var plan = group.PlanAction(action, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.NotEmpty(plan.Changes);
        transcript.Add(JsonSerializer.Serialize(plan));
        var outcome = group.ApplyActionPlan(action, parameters, scope, plan);
        Assert.True(outcome.Succeeded);
        RecordOutcome(transcript, outcome);
    }

    private static void RecordOutcome(List<string> transcript, EditOutcome outcome) => transcript.Add(JsonSerializer.Serialize(new
    {
        outcome.Changes, outcome.Errors, outcome.Warnings, outcome.StructureChanged,
        HasRevert = outcome.Revert is not null,
    }));

    private static void Capture(GearboxGroupDocument group, List<string> transcript)
    {
        var view = group.BuildView();
        transcript.Add(JsonSerializer.Serialize<ViewNode>(view));
        foreach (var node in GearboxGroupTests.Walk(group, view)) transcript.Add(JsonSerializer.Serialize<ViewNode>(node));
        transcript.Add(JsonSerializer.Serialize(group.Collections));
        foreach (var collection in group.Collections)
        {
            var page = group.Query(collection.Id, new() { Limit = 2 });
            transcript.Add(JsonSerializer.Serialize(page));
            foreach (var node in page.Rows.SelectMany(row => row.Cells.Values).SelectMany(node => GearboxGroupTests.Walk(group, node)))
                transcript.Add(JsonSerializer.Serialize<ViewNode>(node));
        }
        foreach (var output in group.Serialize()) transcript.Add(output.RelativePath + ":" + Hash(output.Content.Span));
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
