using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class ProtocolCharacterizationTests
{
    [Theory]
    // Refreshed for #40's character table actions; authored framing, no private sample data.
    [InlineData("PPSA01911", "EF2424B3445844D0863001C22CB7B2E280CB3BB0662E273E870F8FFA8C510A0C")]
    [InlineData("PPSA08664", "3FD52C362F706212904EBB3B2992DA45C1949E33440947564EA54F05B90E3BBE")]
    [InlineData("PPSA18936", "F3F639F8E08F8B7338738406E53F91BD739EDBE02F421040964BFBB4A726CA97")]
    // Direct-open hashes: equal to hosted when images resolve; otherwise pinned separately.
    [InlineData("PPSA01911", "EF2424B3445844D0863001C22CB7B2E280CB3BB0662E273E870F8FFA8C510A0C", false)]
    [InlineData("PPSA08664", "764DBA963DA016DB355601EE919FC5BFF2D881E1BE841ACE4AAA2F5EC2CC299D", false,
        "3FD52C362F706212904EBB3B2992DA45C1949E33440947564EA54F05B90E3BBE")]
    [InlineData("PPSA18936", "E48107D7207DA89ABAF4E2FE570E08287D4072C2DA6ECE9F5D7191A864C75D77", false,
        "F3F639F8E08F8B7338738406E53F91BD739EDBE02F421040964BFBB4A726CA97")]
    public void RootCollectionsLazyRoutesAndCompletePlansKeepTheirProtocol(string title, string expected,
        bool hosted = true, string? resolvedImagesExpected = null)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var fixture = SyntheticGameplay.Create(title);
            var schema = Ff7Schema.ForTitle(title)!;
            // On builds with forward-slash resource names the old direct fallback already exposed maps.
            if (!hosted && resolvedImagesExpected is not null && typeof(FinalFantasy7Plugin).Assembly
                .GetManifestResourceNames().Contains("refpacks/ff7r.rebirth.en/maps/map-grasslands.webp", StringComparer.Ordinal))
                expected = resolvedImagesExpected;
            SeedMateria(fixture.Bytes, schema);
            // Use the host's pinned packs so optional asset resolution is consistent across platforms.
            var plugin = new FinalFantasy7Plugin();
            var references = new ReferencePackStore([plugin]).Pin(plugin, title);
            var file = MemorySaveSet.SingleFile(schema.Prefix + "000", fixture.Bytes, title);
            using var document = hosted ? plugin.Open(file, references) : plugin.Open(file);
            var transcript = new StringBuilder();
            transcript.AppendLine(JsonSerializer.Serialize(WithoutSelectionBindings(document.BuildView())));
            CaptureCollections((IQueryableDocument)document, transcript);
            CaptureLazyRoutes(document, schema, transcript);
            CapturePlans((IActionPlanningDocument)document, schema, transcript);
            var actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(transcript.ToString().ReplaceLineEndings("\n"))));
            Assert.True(expected == actual, $"Protocol hash: expected {expected}; actual {actual}.");
            Assert.Equal(fixture.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // Retain the existing protocol fingerprints except for the intentional SDK-41
    // declarations. BulkSelectionDeclarationTests checks their actual published metadata separately.
    private static ViewNode WithoutSelectionBindings(ViewNode root)
    {
        var tabs = Assert.IsType<TabsNode>(root);
        return tabs with { Tabs = tabs.Tabs.Select(tab => tab with
        {
            Children = tab.Children.Select(WithoutNode).ToArray(),
        }).ToArray() };
    }

    private static ViewNode WithoutNode(ViewNode node) => node switch
    {
        ActionNode { SelectionTableId: not null } action => WithoutBinding(action),
        TableNode table => table with
        {
            SelectionId = null,
            Rows = table.Rows.Select(row => row with
            {
                Cells = row.Cells.ToDictionary(pair => pair.Key,
                    pair => pair.Value is ActionNode { SelectionTableId: not null } action
                        ? WithoutBinding(action) : pair.Value, StringComparer.Ordinal),
            }).ToArray(),
        },
        _ => node,
    };

    private static ActionNode WithoutBinding(ActionNode action)
    {
        var expected = action.ActionId switch
        {
            InventoryPlanning.ActionId => "inventory",
            MateriaPlanning.ActionId or MateriaPlanning.StarActionId => "materia",
            CharacterPlanning.RecoveryActionId or CharacterPlanning.LevelActionId => CharacterPlanning.SelectionTableId,
            _ => throw new InvalidOperationException("Unexpected selection binding in the protocol fingerprint."),
        };
        Assert.Equal(expected, action.SelectionTableId);
        return action with { SelectionTableId = null };
    }

    private static void SeedMateria(byte[] bytes, Ff7Schema schema)
    {
        var layout = GameplayLayout.For(schema);
        var type = schema.IsRemake ? 10004U : 10020U;
        for (var slot = 0; slot < 3; slot++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(layout.MateriaIdOffset + slot * 32), type);
            bytes[layout.MateriaLevelOffset + slot * 32] = slot == 2 ? (byte)0 : (byte)1;
        }
    }

    private static void CaptureCollections(IQueryableDocument document, StringBuilder transcript)
    {
        transcript.AppendLine(JsonSerializer.Serialize(document.Collections));
        foreach (var collection in document.Collections)
        {
            transcript.AppendLine(JsonSerializer.Serialize(document.Query(collection.Id, new() { Limit = 2, IncludeEmpty = true })));
            transcript.AppendLine(JsonSerializer.Serialize(document.Query(collection.Id, new()
            {
                Offset = checked((int)Math.Max(0, collection.TotalCount - 1)), Limit = 2, IncludeEmpty = true,
            })));
        }
    }

    private static void CaptureLazyRoutes(ISaveDocument document, Ff7Schema schema, StringBuilder transcript)
    {
        string[] routes = ["inventory/0", "inventory/15", "materia/0", "materia/7", "catalog/items/0", "catalog/materia/0",
            "records/0", "record/0", "words/0/40/4"];
        string[] additional = schema.IsRemake ? [] :
        [
            "owned/weapon/0", "owned/weapon/1", "owned/materia/0", "owned/materia/15",
            "spatial/map/0", "spatial/folio/0", "spatial/sockets/0",
        ];
        foreach (var route in routes.Concat(additional))
            transcript.AppendLine(JsonSerializer.Serialize(document.ExpandNode(route)));
        foreach (var route in new[] { "inventory/16", "materia/8", "catalog/items/999", "catalog/weapons/0",
            "owned/unknown/0", "spatial/unknown/0", "spatial/folio/7", "record/999", "words/0/40/3", "record/-1" })
            transcript.AppendLine(Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(route)).Message);
    }

    private static void CapturePlans(IActionPlanningDocument document, Ff7Schema schema, StringBuilder transcript)
    {
        var type = schema.IsRemake ? "10004" : "10020";
        foreach (var action in new[] { MateriaPlanning.ActionId, MateriaPlanning.StarActionId })
        {
            var stars = action == MateriaPlanning.StarActionId;
            var parameters = new Dictionary<string, FieldValue>
            {
                ["type"] = FieldValue.Of(type), [stars ? "stars" : "ap"] = FieldValue.Of(stars ? 2L : 1000L),
            };
            ActionScope[] scopes = [new(), new([]), new(["materia/2", "materia/1", "materia/0", "materia/0",
                "materia/3", "materia/01", "materia/1000", "inventory/0"]), new() { QueryScope = new("materia", new(), 0) }];
            foreach (var scope in scopes)
                transcript.AppendLine(JsonSerializer.Serialize(document.PlanAction(action, parameters, scope)));
            foreach (var value in new[] { FieldValue.Of(-1L), FieldValue.Of(0L), FieldValue.Of(6L),
                FieldValue.Of(1_000_000L), FieldValue.Of("bad") })
            {
                parameters[stars ? "stars" : "ap"] = value;
                transcript.AppendLine(JsonSerializer.Serialize(document.PlanAction(action, parameters, new())));
            }
            parameters["type"] = FieldValue.Of("0" + type);
            transcript.AppendLine(JsonSerializer.Serialize(document.PlanAction(action, parameters, new())));
            transcript.AppendLine(JsonSerializer.Serialize(document.PlanAction(action, new Dictionary<string, FieldValue>(), new())));
        }
        transcript.AppendLine(JsonSerializer.Serialize(document.PlanAction("unknown", new Dictionary<string, FieldValue>(), new())));
    }
}
