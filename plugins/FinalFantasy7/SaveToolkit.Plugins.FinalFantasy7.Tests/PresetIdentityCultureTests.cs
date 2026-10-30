using System.Globalization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class PresetIdentityCultureTests
{
    [Theory]
    [InlineData("en-US", false)]
    [InlineData("ar-SA", false)]
    [InlineData("th-TH", true)]
    [InlineData("tr-TR", true)]
    public void MissingMateriaSlotKeepsItsInvariantIdentity(string cultureName, bool overrideSigns)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(cultureName).Clone();
        // Exercise user overrides as well as the standard Arabic sign convention.
        if (overrideSigns) culture.NumberFormat.NegativeSign = "~";
        using var scope = new TestCulture(culture);
        var references = new ReferenceData(IReferenceContext.Empty, Ff7Schema.ForTitle("PPSA08664")!);
        var target = new PresetTarget(PresetIdentityDomain.MateriaInstance, 7, 1);
        var candidate = new PresetCandidate("", null, 1, 7, PresetDetailStatus.MissingDetail, "Authored fixture");
        var reference = FinalFantasy7PresetIdentity.ToCandidate(target, candidate, references);
        Assert.Equal("ff7:materia-candidate/7/1/-1", reference.StableId);
    }
}
