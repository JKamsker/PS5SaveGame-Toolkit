using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class WriteWarningTests
{
    private const string ConsoleWarning = "Not validated on console. The game may reject or overwrite the edited save.";
    private const string CurrencyQualification = "The game may clamp large values. Absent currency categories are created only when a nonzero amount is set.";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Overview_WarnsAboutConsoleRejectionAndContentClamping(bool wonderlands, bool profile)
    {
        using var document = Create(wonderlands, profile);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var warning = Assert.IsType<TextNode>(view.Tabs[0].Children[0]);

        Assert.Equal(Tone.Warning, warning.Tone);
        Assert.StartsWith(ConsoleWarning, warning.Text);
        Assert.Contains("Experimental PS5 support", warning.Text);
        Assert.Contains("The game may clamp or remove edited content.", warning.Text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LegacyEditableFields_WarnAboutConsoleRejectionAndKeepSpecificQualifications(bool wonderlands, bool profile)
    {
        using var document = Create(wonderlands, profile);
        if (!profile) _ = WriteAtomicityTests.AddMessage(document.Root, "resource_pools");
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var fields = view.Tabs[0].Children.OfType<FieldNode>().ToDictionary(f => f.ValueId);
        var currencyIds = profile ? new[] { "keys/2420693941" } : ["currency/618814354", "currency/3679636065"];
        foreach (var id in currencyIds)
            AssertWarning(fields[id], ConsoleWarning + " " + CurrencyQualification);
        if (profile) return;

        AssertWarning(fields["character/name"], ConsoleWarning);
        var ammo = Assert.Single(document.ExpandNode("ammo").OfType<TableNode>());
        AssertWarning(Assert.IsType<FieldNode>(Assert.Single(ammo.Rows).Cells["amount"]),
            ConsoleWarning + " The game may clamp ammo to the capacity provided by its SDU.");
    }

    private static void AssertWarning(FieldNode field, string expected)
    {
        Assert.False(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Verified, field.Confidence);
        Assert.Equal(expected, field.Warning);
    }

    private static GearboxDocument Create(bool wonderlands, bool profile)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(ContainerTests.Create(wonderlands, profile), title, profile);
        return new GearboxDocument(profile ? "profile" : "1", title, container, container.Decode(), IReferenceContext.Empty);
    }
}
