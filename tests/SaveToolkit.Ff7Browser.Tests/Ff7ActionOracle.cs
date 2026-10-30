using System.Globalization;
using System.Text.Json.Nodes;
using SaveToolkit.Testing.Ff7Browser;

namespace SaveToolkit.Ff7Browser.Tests;

/// <summary>Independent complete-file oracle: only known action fields may change at seam-provided addresses.</summary>
internal static class Ff7ActionOracle
{
    private static readonly string[] InventoryFields = ["inventory/0/quantity", "inventory/1/quantity", "inventory/200/quantity"];
    private static readonly string[] MateriaFields = ["ap", "level"];
    private static readonly string[] VitalFields = ["hp", "mp"];
    private static readonly string[] LevelFields = ["level", "experience"];
    public static byte[] Expected(Ff7Binary fixture, string action, string scope, bool empty, JsonArray changes)
    {
        var expected = fixture.Gameplay.ToArray();
        if (empty)
        {
            Assert.Empty(changes);
            return expected;
        }
        Assert.NotEmpty(changes);
        var selected = scope == "selected" ? Ff7Actions.Selected(action).Single() : null;
        AssertTargetSet(fixture, action, selected, changes);
        foreach (var change in changes)
        {
            var id = change!["valueId"]!.GetValue<string>();
            var suffix = id.Split('/')[^1];
            if (selected is not null) Assert.StartsWith(selected + "/", id, StringComparison.Ordinal);
            switch (action)
            {
                case Ff7Actions.Restock:
                    Assert.StartsWith("inventory/", id, StringComparison.Ordinal);
                    Assert.Equal("quantity", suffix);
                    Assert.Contains(id, InventoryFields);
                    Assert.Equal(99U, Value(change["newValue"]!));
                    break;
                case Ff7Actions.Ap:
                case Ff7Actions.Stars:
                    Assert.StartsWith("materia/", id, StringComparison.Ordinal);
                    Assert.Contains(suffix, MateriaFields);
                    if (fixture.TitleId != Ff7Actions.Titles[0]) Assert.StartsWith("materia/0/", id, StringComparison.Ordinal);
                    Assert.NotEqual("materia/5/ap", id); // orphan detail never becomes owned
                    Assert.Equal(suffix == "ap" ? action == Ff7Actions.Ap ? 1U : fixture.SecondTierAp : action == Ff7Actions.Ap ? 1U : 2U,
                        Value(change["newValue"]!));
                    break;
                case Ff7Actions.Recover:
                    Assert.StartsWith("character/", id, StringComparison.Ordinal);
                    Assert.Contains(suffix, VitalFields);
                    var maximum = id[..^suffix.Length] + "max-" + suffix;
                    Assert.Equal(fixture.Read(maximum), Value(change["newValue"]!));
                    break;
                case Ff7Actions.Level:
                    Assert.StartsWith("character/", id, StringComparison.Ordinal);
                    Assert.Contains(suffix, LevelFields);
                    if (suffix == "level") Assert.Equal(25U, Value(change["newValue"]!));
                    Assert.Equal(suffix == "experience", change["derived"]?.GetValue<bool>() ?? false);
                    break;
                default: throw new InvalidOperationException("Unknown action.");
            }
            Assert.Equal(fixture.Read(id), Value(change["oldValue"]!));
            fixture.Write(expected, id, Value(change["newValue"]!));
        }
        Assert.False(expected.AsSpan().SequenceEqual(fixture.Gameplay));
        return expected;
    }

    public static uint Value(JsonNode value) => uint.Parse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture);

    private static void AssertTargetSet(Ff7Binary fixture, string action, string? selected, JsonArray changes)
    {
        string[] rows = selected is not null ? [selected] : action switch
        {
            Ff7Actions.Restock => ["inventory/0", "inventory/1", "inventory/200"],
            Ff7Actions.Ap or Ff7Actions.Stars => fixture.TitleId == Ff7Actions.Titles[0]
                ? ["materia/0", "materia/1", "materia/200"] : ["materia/0"],
            _ => fixture.FieldOffsets.Keys.Where(id => id.StartsWith("character/", StringComparison.Ordinal)
                    && id.EndsWith("/level", StringComparison.Ordinal)).Select(id => id[..^"/level".Length]).ToArray(),
        };
        string[] suffixes = action switch
        {
            Ff7Actions.Restock => ["quantity"],
            Ff7Actions.Ap => ["ap"],
            Ff7Actions.Stars => ["ap", "level"],
            Ff7Actions.Recover => ["hp", "mp"],
            Ff7Actions.Level => ["level"],
            _ => throw new InvalidOperationException("Unknown action."),
        };
        var actual = changes.Select(change => change!["valueId"]!.GetValue<string>()).ToArray();
        Assert.Equal(actual.Length, actual.Distinct(StringComparer.Ordinal).Count());
        // Some characters have zero XP at this target level. The plugin's independent curve tests
        // own that derived oracle; every intended level/vital/inventory/materia target is exact here.
        var primary = action == Ff7Actions.Level ? actual.Where(id => id.EndsWith("/level", StringComparison.Ordinal)) : actual;
        Assert.Equal(rows.SelectMany(row => suffixes.Select(suffix => row + "/" + suffix)).Order(StringComparer.Ordinal),
            primary.Order(StringComparer.Ordinal));
    }
}
