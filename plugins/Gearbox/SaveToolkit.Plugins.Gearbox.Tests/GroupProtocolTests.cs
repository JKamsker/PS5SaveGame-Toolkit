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

    // Refreshed for #40 legacy-field evidence and write warnings; still pins plans and export bytes.
    [Theory]
    [InlineData(false, "F6230C01C08B3ECE9D19A6121304C245CFD4B1D33AC04916D8E3B845386FA80B")]
    [InlineData(true, "D0427AB8DC32351377832EED070C3FAD6BC27C59C3ADF26B85B5306F409CDF2B")]
    public void ViewsPlansAndExport_RetainPreAdapterFingerprint(bool wonderlands, string expected)
    {
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
    [InlineData("PPSA01463", "149963F60C77A4DE4DF33DE6ED4C1B550FF3E6382DB6DCF0923B2CBD4B5BDC44")]
    [InlineData("PPSA01493", "BAAAC5820879135BC830E3EE59DA7A29FBFB2D52D9A332A1D32E9557EB4901F8")]
    public void SampleGenerationsAndBackups_RetainPreAdapterFingerprint(string title, string expected)
    {
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
