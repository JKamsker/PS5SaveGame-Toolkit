using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class CultureInvarianceTests
{
    [Theory]
    [MemberData(nameof(TestCulture.Cases), MemberType = typeof(TestCulture))]
    public void Attribution_preserves_the_Gregorian_retrieval_date(string culture)
    {
        using var scope = new TestCulture(culture);
        var attribution = new ReferenceAttribution("https://example.com", "Author", "CC0",
            "https://example.com/license", new DateOnly(2026, 10, 29), "None");

        Assert.Equal("Author, CC0, https://example.com (retrieved 2026-10-29)", attribution.ToShortText());
    }

    [Theory]
    [MemberData(nameof(TestCulture.Cases), MemberType = typeof(TestCulture))]
    public void Numeric_and_date_wire_values_are_invariant(string culture)
    {
        using var scope = new TestCulture(culture);
        var number = FieldValue.Of(-1234.5);
        Assert.Equal("-1234.5", number.ToString());
        Assert.Equal("-1234.5", JsonSerializer.Serialize(number));
        Assert.Equal("\"-1234\"", JsonSerializer.Serialize(FieldValue.Of(-1234)));
        Assert.Equal(-1234.5, Coerce("\"-1234.5\"", FloatSpec.Double).Value.AsDouble());
        Assert.False(Coerce("\"1,5\"", FloatSpec.Double).Ok);
        Assert.False(Coerce("\"1.234,5\"", FloatSpec.Double).Ok);
        Assert.Equal("2026-10-29T12:30:00.0000000",
            Coerce("\"2026-10-29T12:30:00\"", new DateTimeSpec()).Value.AsString());
    }

    [Theory]
    [MemberData(nameof(TestCulture.Cases), MemberType = typeof(TestCulture))]
    public void Identifiers_and_tagged_property_names_use_ordinal_rules(string culture)
    {
        using var scope = new TestCulture(culture);
        Assert.Equal("PPSI00001", SaveSetImporter.ParsePath("ppsi00001/SDIMG_item/data.bin")?.TitleId);
        Assert.True(DiskSaveDirectory.IsConsoleSave("SDIMG_item"));
        Assert.False(DiskSaveDirectory.IsConsoleSave("sdımg_item"));
        const string tagged = "{\"value\":1.5,\"type\":\"float\",\"kind\":\"union\"}";
        var spec = new UnionSpec([new UnionOption("float", "Float", FloatSpec.Double)]);
        Assert.Equal(FieldValue.Union("float", FieldValue.Of(1.5)), JsonSerializer.Deserialize<FieldValue>(tagged));
        Assert.True(Coerce(tagged, spec).Ok);
        Assert.False(Coerce(tagged.Replace("kind", "KIND", StringComparison.Ordinal), spec).Ok);
    }

    [Fact]
    public async Task Scoped_culture_flows_across_await_and_restores_both_cultures()
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        using (new TestCulture(""))
        {
            await Task.Yield();
            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentCulture);
            Assert.Equal(CultureInfo.InvariantCulture, CultureInfo.CurrentUICulture);
        }
        Assert.Same(culture, CultureInfo.CurrentCulture);
        Assert.Same(uiCulture, CultureInfo.CurrentUICulture);
    }

    [Theory]
    [MemberData(nameof(TestCulture.Cases), MemberType = typeof(TestCulture))]
    public void Human_limit_diagnostics_intentionally_use_the_current_number_format(string culture)
    {
        using var scope = new TestCulture(culture);
        using var stream = new MemoryStream(new byte[1235]);
        var exception = Assert.Throws<ReferencePackException>(() => PackFileReader.ReadBounded(stream, 1234, "file"));
        var expectedLimit = 1234.ToString("N0", CultureInfo.CurrentCulture);
        Assert.Equal($"file is larger than the limit of {expectedLimit} bytes.", exception.Message);
    }

    [Fact]
    public void Process_culture_matches_the_opt_in_matrix_environment()
    {
        if (Environment.GetEnvironmentVariable(TestCulture.EnvironmentVariable) is not { Length: > 0 } name) return;
        Assert.Equal(name, CultureInfo.DefaultThreadCurrentCulture?.Name);
        Assert.Equal(name, CultureInfo.DefaultThreadCurrentUICulture?.Name);
        Assert.Equal(name, CultureInfo.CurrentCulture.Name);
        Assert.Equal(name, CultureInfo.CurrentUICulture.Name);
    }

    private static FieldValueCoercer.Result Coerce(string json, FieldSpec spec)
    {
        using var document = JsonDocument.Parse(json);
        return FieldValueCoercer.FromJson(document.RootElement, spec);
    }
}
