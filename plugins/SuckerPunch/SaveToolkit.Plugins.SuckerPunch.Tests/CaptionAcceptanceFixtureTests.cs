using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class CaptionAcceptanceFixtureTests
{
    [Theory]
    [InlineData(TsushimaCaptionFixtureVersion.V47)]
    [InlineData(TsushimaCaptionFixtureVersion.V48)]
    [InlineData(TsushimaCaptionFixtureVersion.V49)]
    public void PublicFactoryProbesOpensOwnsAndRoundTripsExactSources(TsushimaCaptionFixtureVersion version)
    {
        var fixture = SuckerPunchCaptionAcceptanceFixtures.Create(version);
        Assert.Equal(version, fixture.Version);
        Assert.Equal("PPSA02225", fixture.TitleId);
        Assert.Equal((uint)version, BinaryPrimitives.ReadUInt32LittleEndian(fixture.GameplayBytes.Span[12..]));
        Assert.Equal("PPSA02225/sdimg_CAPTION/dis.dat", fixture.PrimaryRelativePath);
        Assert.Equal("PPSA02225/sdimg_CAPTION/sce_sys/param.sfo", fixture.SfoRelativePath);
        Assert.Equal("PPSA02225/sdimg_CAPTION/other.bin", fixture.CompanionRelativePath);
        var set = Import(fixture);
        var primary = set.FindFile(fixture.PrimaryRelativePath)!;
        var plugin = new SuckerPunchPlugin();
        Assert.True(plugin.Probe(primary).IsMatch);
        using var document = plugin.Open(primary);
        _ = document.BuildView();
        Assert.Equal(["sce_sys/param.sfo"], ((IAdditionalOutputOwner)document).AdditionalOutputPaths);
        Assert.Equal(fixture.Caption, CaptionTestData.Text(fixture.SfoBytes.ToArray()));
        Assert.Equal(31, fixture.MaxUtf8Bytes);
        var plan = CaptionTestData.Plan(document, fixture.Caption);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.True(CaptionTestData.Apply(document, fixture.Caption).Succeeded);
        var outputs = document.Serialize().ToArray();
        Assert.Equal(2, outputs.Length);
        Assert.Equal(2, outputs.Select(output => output.RelativePath).Distinct(StringComparer.Ordinal).Count());
        foreach (var output in outputs)
            Assert.Equal(set.FindAnyFile(output.RelativePath)!.ReadAllBytes(), output.Content.ToArray());
        Assert.Equal(fixture.CompanionBytes.ToArray(), set.FindFile(fixture.CompanionRelativePath)!.ReadAllBytes());
        Assert.DoesNotContain(outputs, output => output.RelativePath == fixture.CompanionRelativePath);
    }

    [Fact]
    public void PublicFixtureSupportsMultibyteCaptionAndReopenWithUnchangedOtherFiles()
    {
        var fixture = SuckerPunchCaptionAcceptanceFixtures.Create();
        var set = Import(fixture);
        using var document = new SuckerPunchPlugin().Open(set.FindFile(fixture.PrimaryRelativePath)!);
        const string caption = "Night 😀 α";
        Assert.True(CaptionTestData.Apply(document, caption).Succeeded);
        var outputs = document.Serialize().ToDictionary(output => output.RelativePath, output => output.Content.ToArray());
        Assert.Equal(fixture.GameplayBytes.ToArray(), outputs[fixture.PrimaryRelativePath]);
        var patched = outputs[fixture.SfoRelativePath];
        Assert.Equal(fixture.SfoBytes.Length, patched.Length);
        Assert.Equal(fixture.SfoBytes.Span[^1], patched[^1]);
        var reopenedSet = Import(fixture, patched);
        using var reopened = new SuckerPunchPlugin().Open(reopenedSet.FindFile(fixture.PrimaryRelativePath)!);
        Assert.Empty(CaptionTestData.Plan(reopened, caption).Changes);
        Assert.Equal(patched, CaptionTestData.Metadata(reopened));
        Assert.Equal(fixture.CompanionBytes.ToArray(), reopenedSet.FindFile(fixture.CompanionRelativePath)!.ReadAllBytes());
    }

    [Fact]
    public void PublicFactoryRejectsUnknownSchemasAndProvidesIndependentDeterministicBuffers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SuckerPunchCaptionAcceptanceFixtures.Create((TsushimaCaptionFixtureVersion)0));
        var first = SuckerPunchCaptionAcceptanceFixtures.Create();
        var second = SuckerPunchCaptionAcceptanceFixtures.Create();
        Assert.Equal(TsushimaCaptionFixtureVersion.V49, first.Version);
        Assert.Equal(first.GameplayBytes.ToArray(), second.GameplayBytes.ToArray());
        Assert.Equal(first.SfoBytes.ToArray(), second.SfoBytes.ToArray());
        Assert.Equal(first.CompanionBytes.ToArray(), second.CompanionBytes.ToArray());
        var clone = first.SfoBytes.ToArray();
        clone[^1] ^= 0xFF;
        Assert.NotEqual(clone[^1], first.SfoBytes.Span[^1]);
        Assert.NotEqual(clone[^1], second.SfoBytes.Span[^1]);
    }

    private static ISaveSet Import(SuckerPunchCaptionAcceptanceFixture fixture, byte[]? sfo = null)
    {
        // The consumer imports the bridge's full paths; no internal writer is used here.
        var prefix = fixture.PrimaryRelativePath[..(fixture.PrimaryRelativePath.LastIndexOf('/') + 1)];
        var rawDirectory = prefix.Split('/')[1];
        return new MemorySaveSet().With(fixture.TitleId, rawDirectory["sdimg_".Length..], new Dictionary<string, byte[]>
        {
            [fixture.PrimaryRelativePath[prefix.Length..]] = fixture.GameplayBytes.ToArray(),
            [fixture.SfoRelativePath[prefix.Length..]] = sfo ?? fixture.SfoBytes.ToArray(),
            [fixture.CompanionRelativePath[prefix.Length..]] = fixture.CompanionBytes.ToArray(),
        });
    }
}
