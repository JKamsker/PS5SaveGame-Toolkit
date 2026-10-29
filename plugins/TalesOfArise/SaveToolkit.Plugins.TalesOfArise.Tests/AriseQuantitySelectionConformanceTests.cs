using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseQuantitySelectionConformanceTests
{
    private const string FirstRow = "inventory/0/quantity";
    private const string SecondRow = "inventory/1/quantity";

    [Fact]
    public void SetQuantity_AdvisoryCasePassesWithMeaningfulSelectedAndUnselectedOwnership()
    {
        var fixture = new AriseTestContainer();
        var original = fixture.Encrypt();
        var inventory = fixture.Sections.Single(section => section.Name == "SaveData_ItemManager");
        // These aliases cover the very same four bytes as each quantity/flags/ID word.
        // Ownership comes from fixed record offsets, rather than labels or guessed ID prefixes.
        var rowOwners = new Dictionary<string, IReadOnlyList<string>>
        {
            [FirstRow] = [FirstRow, $"raw/{inventory.Offset:X}", $"evidence/{inventory.Id:X8}/000000"],
            [SecondRow] = [SecondRow, $"raw/{inventory.Offset + AriseTestContainer.ItemRecordSize:X}",
                $"evidence/{inventory.Id:X8}/{AriseTestContainer.ItemRecordSize:X6}"],
        };
        var selection = new SelectionScopeCase("inventory/set-quantity", "inventory", [FirstRow])
        {
            Parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(12L) },
            Stage = document =>
            {
                _ = document.LoadInventory();
                var staged = document.ApplyEdits([new(FirstRow, FieldValue.Of(0L)), new(SecondRow, FieldValue.Of(0L))]);
                if (!staged.Succeeded) throw new InvalidOperationException("Could not stage both owned inventory rows.");
            },
            RowOwnedValueIds = rowOwners,
            ExpectedChangedValueIds = rowOwners[FirstRow],
        };
        // The real view includes all 138,832 explorer words and rich F8 evidence.
        // Explicit budgets cover that complete fixed schema instead of accepting a traversal cut.
        var result = SelectionScopeFixture.Evaluate(() => new TalesOfArisePlugin().Open(MemorySaveSet.SingleFile(
            AriseTestContainer.FileName, original, AriseTestContainer.TitleId)), selection, new()
        {
            MaxNodes = 2_000_000,
            MaxLazyExpansions = 1_024,
            MaxQueryPages = 4_096,
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"SDK-41 advisory status: {result.Status}. {string.Join("; ", result.Diagnostics)}");
        switch (result.Status)
        {
            case SelectionScopeStatus.NotEvaluated:
                Assert.Fail("Advisory evidence was not evaluated: " + string.Join("; ", result.Diagnostics));
                break;
            case SelectionScopeStatus.Failed:
                Assert.Fail("Advisory evidence failed: " + string.Join("; ", result.Diagnostics));
                break;
            case SelectionScopeStatus.Passed:
                break;
            default:
                Assert.Fail("Unknown selection advisory status.");
                break;
        }
    }
}
