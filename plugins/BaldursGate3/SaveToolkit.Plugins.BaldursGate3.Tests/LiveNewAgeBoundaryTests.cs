using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LiveNewAgeBoundaryTests
{
    [Theory]
    [InlineData(2, 1u, 1u)]
    [InlineData(3, 2u, 1u)]
    [InlineData(4, 2u, 2u)]
    [InlineData(5, 2u, 3u)]
    public void EveryContributionTargetHasADeterministicByteExactInverse(long target, uint first, uint second)
    {
        var original = LiveNewAgeFixture.Create();
        var source = LiveNewAgeFixture.Source(original);
        var number = source.Numbers.Single(number => number.Key == "stack/0/member/0/quantity");
        // The authored fixture has two 8-byte contributions, amounts at +4 and +12.
        var entries = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
            original.AsSpan(LsmfFixture.Descriptor(original, 1) + 40)) + 48;
        Assert.Equal([entries + 4, entries + 12], number.Offsets);
        Assert.Equal((2L, 5L, 5L), (number.Min, number.Max, number.Original));
        var expected = original.ToArray();
        LsmfFixture.U32(expected, entries + 4, first);
        LsmfFixture.U32(expected, entries + 12, second);

        source.Set(number, FieldValue.Of(target));
        Assert.Equal(target, source.Get(number));
        Assert.Equal(expected, source.Write()); // Includes member indices, padding and every other family.
        var reopened = LiveNewAgeFixture.Source(source.Write());
        Assert.Equal([target, 4L], reopened.Stacks.Select(stack => stack.Original));
        Assert.Empty(reopened.Diagnostics);
        source.Set(number, FieldValue.Of(number.Min));
        source.Set(number, FieldValue.Of(target));
        Assert.Equal(expected, source.Write());
        source.Set(number, FieldValue.Of(number.Original));
        Assert.False(source.HasEdits);
        Assert.Equal(original, source.Write());
    }

    [Fact]
    public void TemporaryHpUsesStoredBoundsAndPreservesTheOtherHealthWords()
    {
        var source = LiveNewAgeFixture.Source();
        var number = source.Numbers.First(n => n.Key.EndsWith("/temporary", StringComparison.Ordinal));
        source.Set(number, FieldValue.Of(0L));
        var written = source.Write();
        var field = Assert.Single(number.Offsets);
        Assert.Equal(0u, BinaryData.U32(written, field));
        Assert.Equal(2u, BinaryData.U32(written, field + 4));
        Assert.Equal(8u, BinaryData.U32(written, field - 8));
        Assert.Equal(10u, BinaryData.U32(written, field - 4));
    }

    [Fact]
    public void InvalidPackedProfilesAndDuplicateContainerKeysRemainOriginalAndDiagnostic()
    {
        var source = LiveNewAgeFixture.Source();
        foreach (var family in new[] { "Health", "Stats", "Containers" })
        {
            var bytes = source.Access.Model.Bytes.ToArray();
            var offset = family == "Containers" ? source.Slots[1].KeyOffset : source.Numbers.First(n => n.Family == family).Offsets[0];
            if (family == "Containers") bytes[offset] = (byte)source.Slots[0].Slot;
            else LsmfFixture.U32(bytes, offset, uint.MaxValue);
            var invalid = LiveNewAgeFixture.Source(bytes);
            Assert.NotEmpty(invalid.Diagnostics);
            if (family == "Containers") Assert.Empty(invalid.Slots);
            else Assert.DoesNotContain(invalid.Numbers, number => number.Family == family);
            Assert.Equal(bytes, invalid.Write());
        }
    }

    [Fact]
    public void PartiallyOverlappingEditableWordsAreRejected()
    {
        var bytes = LiveNewAgeFixture.Create();
        var health = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
            bytes.AsSpan(LsmfFixture.Descriptor(bytes, 9) + 40)) + 48;
        var statsDescriptor = LsmfFixture.Descriptor(bytes, 10);
        LsmfFixture.U64(bytes, statsDescriptor + 40, (ulong)(health - 48 + 2));
        var stats = health + 2;
        for (var row = 0; row < 2; row++)
            for (var ability = 0; ability < 6; ability++)
                LsmfFixture.U32(bytes, stats + row * 36 + 8 + ability * 4, 10);

        Assert.Throws<SaveFormatException>(() => LiveNewAgeFixture.Source(bytes));
    }

    [Fact]
    public void StandaloneNewAgeWarningDoesNotClaimAnOuterPackageDigest()
    {
        using var document = LiveNewAgeFixture.Open(titleId: "PPSA18463");
        _ = document.BuildView();

        var row = Assert.Single(document.Query("live/0/0/values", new QueryableCollectionQuery { Limit = 1 }).Rows);
        var field = Assert.IsType<FieldNode>(row.Cells["value"]);
        Assert.DoesNotContain("known outer v18 digest is repaired", field.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("when this resource is nested", field.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuerySortAndExactFiltersUseCurrentValuesAndRemainBounded()
    {
        using var document = LiveNewAgeFixture.Open(titleId: "PPSA18463");
        _ = document.BuildView();
        _ = document.ExpandNode("live/0/0");
        var query = new QueryableCollectionQuery { Limit = 2, SortColumn = "value", Descending = true,
            Filters = new Dictionary<string, string> { ["family"] = "Stacks" } };
        var page = document.Query("live/0/0/values", query);
        Assert.Equal(2, page.FilteredCount);
        var field = Assert.IsType<FieldNode>(page.Rows[0].Cells["value"]);
        Assert.Equal(5, field.Value.AsInt32());
        Assert.True(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(2L))]).Succeeded);
        var sorted = document.Query("live/0/0/values", query);
        Assert.Equal(4, Assert.IsType<FieldNode>(sorted.Rows[0].Cells["value"]).Value.AsInt32());
        foreach (var bad in new[] { query with { Limit = 0 }, query with { Limit = int.MaxValue }, query with { Offset = -1 },
            query with { SortColumn = "invalid" }, query with { Cursor = "unsupported" } })
            Assert.Throws<ArgumentException>(() => document.Query("live/0/0/values", bad));
    }

    [Fact]
    public void PlansRejectWrongCollectionsDuplicatesUnknownRowsAndCrossContainerSelectionsAtomically()
    {
        using var document = LiveNewAgeFixture.Open(titleId: "PPSA18463");
        _ = document.BuildView();
        var original = Assert.Single(document.Serialize()).Content.ToArray();
        var empty = new Dictionary<string, FieldValue>();
        foreach (var scope in new[]
        {
            new ActionScope(["live/0/0/stack/0/member/0/quantity", "live/0/0/stack/0/member/0/quantity"]),
            new ActionScope(["missing"]),
            new ActionScope { QueryScope = new("bg3.saved-items", new(), 0) },
        })
        {
            Assert.NotEmpty(document.PlanAction(LsmfLiveActions.Restore, empty, scope).Errors);
            Assert.False(document.ApplyActionPlan(LsmfLiveActions.Restore, empty, scope, new ActionPlan()).Succeeded);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
        Assert.Empty(document.PlanAction(LsmfLiveActions.Restore, empty, new ActionScope([])).Changes);
        Assert.NotEmpty(document.PlanAction(LsmfLiveActions.Swap, empty, new ActionScope()).Errors);
        Assert.NotEmpty(document.PlanAction(LsmfLiveActions.Swap, empty, new ActionScope(["missing", "other"])).Errors);
        Assert.NotEmpty(document.PlanAction(LsmfLiveActions.Restore, new Dictionary<string, FieldValue> { ["bogus"] = FieldValue.Of(1L) }, new()).Errors);
    }

    [Fact]
    public void ScopedMementosRefreshLiveGettersAndCannotBeAppliedToAnotherDocument()
    {
        using var document = LiveNewAgeFixture.Open(titleId: "PPSA18463");
        using var other = LiveNewAgeFixture.Open(titleId: "PPSA18463");
        _ = document.BuildView();
        var rows = document.Query("live/0/0/slots", new QueryableCollectionQuery { Limit = 2 }).Rows;
        var key = Assert.IsType<FieldNode>(rows[0].Cells["slot"]);
        var scope = new ActionScope(rows.Select(row => row.Key).ToArray());
        var parameters = new Dictionary<string, FieldValue>();
        var action = document.ApplyActionPlan(LsmfLiveActions.Swap, parameters, scope, new ActionPlan());
        Assert.True(action.Succeeded);
        Assert.NotNull(action.Revert);
        Assert.Equal(5, document.GetValue(key.ValueId).AsInt32());
        Assert.Throws<ArgumentException>(() => other.RestoreMemento(action.Revert));
        var redo = document.RestoreMemento(action.Revert);
        Assert.Equal(4, document.GetValue(key.ValueId).AsInt32());
        _ = document.RestoreMemento(redo);
        Assert.Equal(5, document.GetValue(key.ValueId).AsInt32());
    }

    [Fact]
    public void SpellPointersUseOnlyTheMeasuredPs5ChainAndRejectInteriorOrSentinelAddresses()
    {
        const string name = "Target_Test";
        var bytes = LsmfFixture.Create(
            ("game.spell.v0.SpellId", 24, 1, new byte[24]),
            ("game.spell.v3.SpellData", 72, 1, new byte[72]),
            ("game.spell.v3.SpellBookComponent", 16, 1, new byte[16]),
            ("test.Pool", 1, 0, System.Text.Encoding.ASCII.GetBytes(name)));
        int Offset(int index) => (int)BinaryData.U64(bytes, LsmfFixture.Descriptor(bytes, index) + 40) + 48;
        LsmfFixture.U64(bytes, Offset(0) + 8, (ulong)(Offset(3) - 48));
        LsmfFixture.U32(bytes, Offset(0) + 16, (uint)name.Length);
        LsmfFixture.U64(bytes, Offset(1), (ulong)(Offset(0) - 48));
        LsmfFixture.U64(bytes, Offset(2), (ulong)(Offset(1) - 48));
        LsmfFixture.U64(bytes, Offset(2) + 8, (ulong)(Offset(1) + 72 - 48));
        Assert.Equal(name, Assert.Single(Assert.Single(LsmfSpellBooks.Read(new LsmfAccess(LsmfReader.Read(bytes)))).Spells));
        foreach (var pointer in new[] { ulong.MaxValue, (ulong)(Offset(0) - 47), (ulong)(Offset(3) - 48) })
        {
            var corrupt = bytes.ToArray();
            LsmfFixture.U64(corrupt, Offset(1), pointer);
            Assert.Throws<SaveFormatException>(() => LsmfSpellBooks.Read(new LsmfAccess(LsmfReader.Read(corrupt))));
        }
    }

    [Fact]
    public void ClassArraysKeepExactUnsignedLevelsAndRejectMisSizedOrUnboundedRanges()
    {
        var bytes = LsmfFixture.Create(("game.stats.v0.ClassesComponent", 16, 1, new byte[16]),
            ("test.ClassHeap", 40, 0, new byte[40]));
        const int record = 64;
        LsmfFixture.U64(bytes, 48, record - 48);
        LsmfFixture.U64(bytes, 56, record + 40 - 48);
        LsmfFixture.U64(bytes, record + 32, ulong.MaxValue);
        var selection = Assert.Single(LsmfClasses.Read(new LsmfAccess(LsmfReader.Read(bytes))));
        Assert.Equal(ulong.MaxValue, selection.Level);
        using var document = LiveNewAgeFixture.Open(bytes, "PPSA18463");
        var field = Assert.IsType<FieldNode>(Assert.Single(document.Query("live/0/0/classes", new()).Rows).Cells["level"]);
        Assert.Equal((Int128)ulong.MaxValue, field.Value.AsInteger());
        Assert.Equal((Int128)ulong.MaxValue, Assert.IsType<IntegerSpec>(field.Spec).Max);
        foreach (var end in new[] { ulong.MaxValue, (ulong)(record + 39 - 48), (ulong)(record + 40 * 33 - 48) })
        {
            var corrupt = bytes.ToArray();
            LsmfFixture.U64(corrupt, 56, end);
            Assert.Throws<SaveFormatException>(() => LsmfClasses.Read(new LsmfAccess(LsmfReader.Read(corrupt))));
        }
    }

}
