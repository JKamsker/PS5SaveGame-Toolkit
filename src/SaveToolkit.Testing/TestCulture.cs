using System.Globalization;

namespace SaveToolkit.Testing;

/// <summary>Opt-in culture setup for test processes and scoped, async-flow-local regressions.</summary>
public sealed class TestCulture : IDisposable
{
    public const string EnvironmentVariable = "SAVETOOLKIT_TEST_CULTURE";
    private static readonly string[] NamedCultures = ["de-AT", "tr-TR", "ja-JP", "ar-SA"];
    // In invariant mode .NET may return the invariant culture even for SpecificCultures.
    private static readonly bool HasNamedCultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Any(culture => culture.Name.Length != 0);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public TestCulture(string name) : this(CultureInfo.GetCultureInfo(name))
    {
    }

    public TestCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>Named cases when globalization is available; one invariant case in invariant mode.</summary>
    public static IEnumerable<object[]> Cases => HasNamedCultures
        ? NamedCultures.Select(name => new object[] { name })
        : [new object[] { "" }];

    /// <summary>Call once at test-assembly startup, before parallel tests begin. Does nothing unless opted in.</summary>
    public static void InitializeFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is not { Length: > 0 } name) return;
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
