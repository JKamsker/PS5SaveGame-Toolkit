using System.Globalization;
using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Hashing;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class CommunityReferenceTests
{
    private const string RestoredCatalog = "pragmata-restored-identifiers";
    private static readonly uint[] RestoredHashes = [0x27945a5a, 0x619456d1, 0x6f1ca4a8, 0xbc922b61, 0xe205fff9];

    [Fact]
    public void ImportedFactsRetainPinnedAttributionHashesAliasesAndUnknownGameplayLimits()
    {
        var plugin = new PragmataPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA02530");
        var restored = context.Catalog(RestoredCatalog)!;
        var fields = context.Catalog(SchemaReadouts.FieldsCatalog)!;
        var enums = context.Catalog(SchemaReadouts.EnumsCatalog)!;
        Assert.Equal(5, restored.Count);
        Assert.Equal(459, fields.Count);
        Assert.Equal(230, enums.Count);
        Assert.Equal(8, enums.Categories.Count);
        foreach (var catalog in new[] { restored, fields, enums })
        {
            Assert.Equal(HashNames.CommunityLicense, catalog.Dataset.Attribution.License);
            Assert.Contains("4125e497f1ae84cc746bb4a99da612c48bea161e", catalog.Dataset.Attribution.SourceUrl, StringComparison.Ordinal);
            Assert.Contains(catalog.Dataset.Attribution.SourceUrl, plugin.Info.References);
            foreach (var row in Records(catalog))
            {
                Assert.Null(row.Limit);
                Assert.Null(row.AssetId);
                Assert.Equal("Inferred", row.Attributes["confidence"]);
                Assert.Contains("kvasszn", row.AttributionText, StringComparison.Ordinal);
                if (catalog == restored)
                    Assert.Equal(row.Key, Murmur3.Hash(Encoding.UTF8.GetBytes(row.Name)).ToString("x8", CultureInfo.InvariantCulture));
                if (catalog == fields)
                {
                    Assert.Equal(row.Key, $"{row.Attributes["classHash"]}/{row.Attributes["fieldHash"]}");
                    Assert.Equal(row.Attributes["classHash"], Hash(row.Category!));
                    Assert.Equal(row.Attributes["fieldHash"], Hash(row.Name));
                }
            }
        }
        Assert.Equal("LSL / LSide", enums.Find("via.hid.GamePadButton:268435456")!.Name);
        Assert.Equal("Enter / Return", enums.Find("via.hid.KeyboardKey:13")!.Name);
        Assert.Equal("Wait", enums.Find("app.ObjectiveState:0")!.Name);
        // MIT identifiers retain their original provenance; the restored names use their own dataset.
        Assert.Equal(408, context.Catalog("pragmata-identifiers")!.Count);
        Assert.Equal("MIT", context.Catalog("pragmata-identifiers")!.Dataset.Attribution.License);
    }

    [Fact]
    public void RestoredNamesAndEnumDeclarationsMatchSampleHashesWithoutChangingBytesOrBounds()
    {
        var plugin = new PragmataPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02530");
        var fields = context.Catalog(SchemaReadouts.FieldsCatalog)!;
        var enums = context.Catalog(SchemaReadouts.EnumsCatalog)!;
        var restoredObserved = new HashSet<uint>();
        var declarationsObserved = new HashSet<string>(StringComparer.Ordinal);
        var enumsObserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SampleEvidenceTests.Files())
        {
            using var document = (PragmataDocument)plugin.Open(file, context);
            var nodes = Descendants(document.Layout.Roots).ToArray();
            foreach (var hash in RestoredHashes.Where(hash => nodes.Any(node => node.Hash == hash)))
            {
                restoredObserved.Add(hash);
                Assert.Contains(HashNames.CommunityLicense, HashNames.Provenance(hash), StringComparison.Ordinal);
                Assert.Contains("kvasszn", document.References.IdentifierSource(hash), StringComparison.Ordinal);
            }
            foreach (var node in nodes.Where(node => node.IsPrimitive))
            {
                var field = node.Index >= 0 && node.Parent?.Type == FieldTags.Array ? node.Parent : node;
                if (field.Parent?.Type != FieldTags.Class) continue;
                var declaration = fields.Find($"{field.Parent.ClassHash:x8}/{field.Hash:x8}");
                if (declaration is null) continue;
                declarationsObserved.Add(declaration.Key);
                if (node.Type is not (FieldTags.Int32 or FieldTags.UInt32 or FieldTags.Enum)) continue;
                var type = declaration.Attributes["declaredType"];
                var value = document.Read(node).AsInteger();
                if (declaration.Attributes["storageType"] == "U32") value &= uint.MaxValue;
                if (enums.Find(FormattableString.Invariant($"{type}:{value}")) is not { } member || !enumsObserved.Add(type)) continue;
                var control = document.Field(node, raw: true);
                Assert.Contains($"Inferred enum label: {member.Name}", control.Description!, StringComparison.Ordinal);
                Assert.Contains(HashNames.CommunityLicense, control.Description!, StringComparison.Ordinal);
                Assert.Equal(HashNames.All.ContainsKey(node.Hash) ? Confidence.Inferred : Confidence.Unknown, control.Confidence);
                Assert.Equal(document.Read(node), control.Value);
                Assert.Equal(document.Bounds.Spec(node), control.Spec);
                Assert.Equal(control.Value, document.GetValue(control.ValueId));
                Assert.Contains(PragmataDocument.EditWarning, control.Description!, StringComparison.Ordinal);
            }
            Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        }
        Assert.Equal(RestoredHashes.Order(), restoredObserved.Order());
        Assert.InRange(declarationsObserved.Count, 200, fields.Count);
        Assert.Contains("app.ObjectiveState", enumsObserved);
        Assert.Contains("app.GadgetSlotType", enumsObserved);
    }

    [Fact]
    public void EnumLookupRequiresDeclaringClassFieldTypeAndExactValue()
    {
        using var document = EditingTests.Gameplay();
        var plugin = new PragmataPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02530");
        var readouts = new SchemaReadouts(context);
        var parent = new SaveNode(0, FieldTags.Class) { ClassHash = Murmur3.Hash(Encoding.UTF8.GetBytes("app.ObjectiveStatus")) };
        var node = new SaveNode(Murmur3.Hash(Encoding.UTF8.GetBytes("_State")), FieldTags.Int32, parent)
        { Offset = EditingTests.Quantity(document).Offset, Size = 4 };
        var raw = document.Read(node).AsInt32();
        Assert.Null(context.Catalog(SchemaReadouts.EnumsCatalog)!.Find($"app.ObjectiveState:{raw}"));
        Assert.DoesNotContain("Inferred enum label:", readouts.Describe(node, document), StringComparison.Ordinal);
        var known = new SaveNode(node.Hash, FieldTags.Int32, parent)
        { Offset = document.Layout.Detail.Find(KnownHashes.Slot)!.Offset, Size = 4 };
        Assert.Contains("Inferred enum label: Wait", readouts.Describe(known, document), StringComparison.Ordinal);
        var wrongType = new SaveNode(node.Hash, FieldTags.UInt32, parent) { Offset = known.Offset, Size = 4 };
        Assert.DoesNotContain("Inferred enum label:", readouts.Describe(wrongType, document), StringComparison.Ordinal);
        var wrongClass = new SaveNode(node.Hash, FieldTags.Int32, new SaveNode(0, FieldTags.Class)) { Size = 4 };
        Assert.Empty(readouts.Describe(wrongClass, document));
        Assert.Empty(new SchemaReadouts(IReferenceContext.Empty).Describe(node, document));
    }

    private static string Hash(string name) => Murmur3.Hash(Encoding.UTF8.GetBytes(name)).ToString("x8", CultureInfo.InvariantCulture);

    private static IEnumerable<ReferenceRecord> Records(IReferenceCatalog catalog)
    {
        const int pageSize = 200;
        for (var offset = 0; offset < catalog.Count; offset += pageSize)
            foreach (var record in catalog.Search(new ReferenceQuery { Offset = offset, Limit = pageSize }).Items) yield return record;
    }

    private static IEnumerable<SaveNode> Descendants(IEnumerable<SaveNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Descendants(root.Children)) yield return child;
        }
    }
}
