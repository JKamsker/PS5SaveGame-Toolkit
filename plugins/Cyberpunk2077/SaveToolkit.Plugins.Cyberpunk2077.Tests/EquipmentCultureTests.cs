using System.Globalization;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class EquipmentCultureTests
{
    [Theory]
    [InlineData("en-US", false)]
    [InlineData("ar-SA", false)]
    [InlineData("th-TH", true)]
    [InlineData("tr-TR", true)]
    public void OmittedSlotKeepsItsInvariantIdentity(string cultureName, bool overrideSigns)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(cultureName).Clone();
        // Regional number settings may be customized independently of the culture name.
        if (overrideSigns) culture.NumberFormat.NegativeSign = "~";
        using var scope = new TestCulture(culture);
        var slot = new EquipmentSlot("Head", -1, false, null, null, "Omitted", 0, false);
        Assert.Equal("equipment/Head/-1", slot.Key);
        Assert.False(slot.IsEmpty);
    }
}
