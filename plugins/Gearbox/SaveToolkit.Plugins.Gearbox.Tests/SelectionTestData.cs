using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>Authored duplicate serials in all three containers; no private sample dependency.</summary>
internal static class SelectionTestData
{
    internal const int ItemCount = 3;
    internal static string Title(bool wonderlands) => wonderlands ? "PPSA01493" : "PPSA01463";

    internal static GearboxDocument Open(bool wonderlands, bool profile)
    {
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(wonderlands, profile, tail: [0xAB, 0xCD]), Title(wonderlands), profile);
        var root = container.Decode();
        var serial = IndependentSerialProbe.Encode(SyntheticItem.Payload(wonderlands), wonderlands ? 5 : 4, -1);
        foreach (var list in profile ? new[] { GearboxInventory.Bank, GearboxInventory.LostLoot } : [GearboxInventory.Backpack])
        {
            var spec = root.Schema.Spec(root.Type, list);
            for (var index = 0; index < ItemCount; index++)
            {
                if (root.Schema.IsMessage(spec.Type))
                {
                    var item = GearboxTestData.AddMessage(root, list);
                    item.SetInteger("flags", 1);
                    item.SetInteger("pickup_order_index", index);
                    item.Add(new ProtoField(root.Schema.Tag(item.Type, "item_serial_number"), 2,
                        root.Schema.Spec(item.Type, "item_serial_number"), [], serial, null));
                }
                else root.Add(new ProtoField(root.Schema.Tag(root.Type, list), 2, spec, [], serial, null));
            }
        }
        if (!profile)
        {
            foreach (var index in new[] { 0, 2 })
                GearboxTestData.AddMessage(root, "equipped_inventory_list").SetInteger("inventory_list_index", index);
        }
        var imported = GearboxContainer.Parse(container.Encode(root), Title(wonderlands), profile);
        return new GearboxDocument(profile ? "profile" : "1", Title(wonderlands), imported, imported.Decode(), IReferenceContext.Empty);
    }

    internal static MemorySaveSet Set(bool wonderlands)
    {
        using var character = Open(wonderlands, profile: false);
        using var profile = Open(wonderlands, profile: true);
        return new MemorySaveSet().With(Title(wonderlands), "TEST", new Dictionary<string, byte[]>
        {
            ["1"] = character.Serialize().Single().Content.ToArray(),
            ["profile"] = profile.Serialize().Single().Content.ToArray(),
        });
    }

    internal static GearboxGroupDocument Group(bool wonderlands)
    {
        var set = Set(wonderlands);
        return (GearboxGroupDocument)new GearboxPlugin().OpenGroup(new(((ISaveSet)set).AllFiles.Select(file =>
            new DocumentGroupFile(file.FileName == "profile" ? "profile" : "character", file)).ToArray()), IReferenceContext.Empty);
    }

    internal static Workspace Workspace(bool wonderlands, SelectionGroupObserver? observer = null)
    {
        var plugin = new GearboxPlugin();
        var sources = ((ISaveSet)Set(wonderlands)).AllFiles.Select(file => new ImportSource(file.RelativePath, file.OpenRead));
        var set = SaveSetImporter.Import(sources,
            Path.Combine(TestScratch.ProcessFolder, Guid.NewGuid().ToString("N")));
        IGamePlugin selectedPlugin = observer is null ? plugin : observer;
        return new Workspace("gearbox-selection", "Selection", set,
            new PluginCatalog([selectedPlugin]), new ReferencePackStore([plugin]));
    }

    internal static Dictionary<string, byte[]> Bytes(ISaveDocument document) =>
        document.Serialize().ToDictionary(output => output.RelativePath, output => output.Content.ToArray());

    internal static void AssertBytes(ISaveDocument document, IReadOnlyDictionary<string, byte[]> expected) =>
        SelectionByteAssertions.Equal(document, expected);

    internal static IReadOnlyList<ViewNode> Details(ISaveDocument document, TableRow row) =>
        document.ExpandNode(Assert.IsType<SectionNode>(row.Cells["detail"]).LazyId!);

    internal static ActionNode DiscoverItemAction(DocumentSession session, string collectionId, string rowKey, int rowIndex, string actionId)
    {
        _ = session.BuildView();
        var page = session.QueryCollection(collectionId, new() { Offset = rowIndex, Limit = 1 },
            expectedRevision: session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        var row = Assert.Single(page.Rows);
        Assert.Equal(rowKey, row.Key);
        var details = session.Expand(Assert.IsType<SectionNode>(row.Cells["detail"]).LazyId!);
        var action = Assert.Single(details.OfType<ActionNode>(), node => node.ActionId == actionId);
        Assert.True(action.SupportsPlanning);
        Assert.Null(action.SelectionTableId); // Placement in the queried row supplies its table's scope.
        return action;
    }
}
