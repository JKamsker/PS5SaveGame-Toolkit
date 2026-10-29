using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class ProtocolCharacterizationTests
{
    [Theory]
    // Captured before planner/dispatcher cleanup; authored framing, no private sample data.
    [InlineData("PPSA01911", "BDBC5BD5BD3CD7BEC53B93DBBEFE678A65036106A540F72BDF63179E1489D7F7")]
    [InlineData("PPSA08664", "55312EB8E34AD223B36895587AC9768CBB9F112E6DAAC62E0427AD368E16EA72")]
    [InlineData("PPSA18936", "CC1C8FFBD66F13D9EA6BF993A413D5943C06C790E058BF2DC5A9E957718C8709")]
    public void RootCollectionsLazyRoutesAndCompletePlansKeepTheirProtocol(string title, string expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var fixture = SyntheticGameplay.Create(title);
            var schema = Ff7Schema.ForTitle(title)!;
            SeedMateria(fixture.Bytes, schema);
            using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile(schema.Prefix + "000", fixture.Bytes, title));
            var transcript = new StringBuilder();
            transcript.AppendLine(JsonSerializer.Serialize(document.BuildView()));
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
