using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using Xunit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class ConformanceSerializationTests
{
    private const string TitleId = "PPSA99999";
    private const string DirectoryName = "CONFORMANCE";
    private const string PrimaryName = "save.dat";
    private const string CompanionName = "companion.bin";
    private const string SystemName = "sce_sys/param.sfo";
    private const string Root = TitleId + "/sdimg_" + DirectoryName + "/";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaredSystemOutput_NoOpMatchesCompleteSourceBytes(bool upperCasePaths)
    {
        var file = Source();
        var system = Assert.Single(file.Directory.SystemFiles);
        Assert.Null(file.Directory.SaveSet.FindFile(system.RelativePath));
        Assert.Same(system, file.Directory.SaveSet.FindAnyFile(system.RelativePath));
        using var document = new DeclaredSystemDocument(Outputs(file, PrimaryName, SystemName));
        if (upperCasePaths)
            document.Outputs = document.Outputs.Select(output => output with
                { RelativePath = output.RelativePath.ToUpperInvariant() }).ToArray();

        Conformance.Verify(document, file);

        Assert.Equal([SystemName], document.AdditionalOutputPaths);
        Assert.Equal(2, document.Serialize().Count);
        Assert.Equal(2, document.Serialize().Select(output => output.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(document.Serialize(), output => output.RelativePath == Root + CompanionName);
        Assert.Equal([0, 0xFF, 0x80, 0x42], system.ReadAllBytes());
    }

    [Fact]
    public void LegacyPlugin_NoOpMatchesPrimaryBytes()
    {
        var file = Source();
        using var document = new CounterPlugin().Open(file);

        Conformance.Verify(document, file);

        Assert.Equal(file.RelativePath, Assert.Single(document.Serialize()).RelativePath);
    }

    [Fact]
    public void LegacyGameOutputs_NoOpRetainsMultipleFileBehavior()
    {
        var file = Source();
        using var document = new OutputDocument(Outputs(file, PrimaryName, CompanionName));

        Conformance.Verify(document, file);

        Assert.Equal(2, document.Serialize().Count);
    }

    [Theory]
    [InlineData("missing.dat")]
    [InlineData("sce_sys/missing.sfo")]
    public void UnknownOutput_IsRejectedEvenWhenItsBytesMatchAnExistingFile(string path)
    {
        var file = Source();
        using var document = new DeclaredSystemDocument(
            [new(Root + path, file.ReadAllBytes()), .. Outputs(file, PrimaryName, SystemName)]);

        var error = Assert.ThrowsAny<XunitException>(() => Conformance.Verify(document, file));

        Assert.Equal($"Serialize produced unknown path {Root + path}.", error.Message);
    }

    [Theory]
    [InlineData(PrimaryName, false)]
    [InlineData(PrimaryName, true)]
    [InlineData(SystemName, false)]
    [InlineData(SystemName, true)]
    public void ChangedOutput_IsRejectedForGameAndSystemFiles(string path, bool appendByte)
    {
        var file = Source();
        var source = file.Directory.FindAnyFile(path)!;
        var original = source.ReadAllBytes();
        byte[] changed = appendByte ? [.. original, 0] : original.ToArray();
        if (!appendByte) changed[^1] ^= 1;
        using var document = new DeclaredSystemDocument(Outputs(file, PrimaryName, SystemName)
            .Select(output => output.RelativePath == source.RelativePath
                ? output with { Content = changed } : output).ToArray());

        var error = Assert.ThrowsAny<XunitException>(() => Conformance.Verify(document, file));

        Assert.Equal($"No-op round trip changed {source.RelativePath}.", error.Message);
        Assert.Equal(original, source.ReadAllBytes());
    }

    private static ISaveFile Source() => new MemorySaveSet().With(TitleId, DirectoryName,
        new Dictionary<string, byte[]>
        {
            [PrimaryName] = CounterPlugin.Create(5),
            [CompanionName] = [7, 8, 9],
            // Opaque invented bytes: this tests output lookup/identity, not SFO parsing or writing.
            [SystemName] = [0, 0xFF, 0x80, 0x42],
        }).Directories[0].Files.Single(file => file.PathInDirectory == PrimaryName);

    private static IReadOnlyList<FileOutput> Outputs(ISaveFile primary, params string[] names) =>
        names.Select(name => primary.Directory.FindAnyFile(name)!)
            .Select(file => new FileOutput(file.RelativePath, file.ReadAllBytes())).ToArray();

    private sealed class Conformance : PluginConformanceTests<CounterPlugin>
    {
        protected override IReadOnlyList<string> SampleTitleIds => [];

        public static void Verify(ISaveDocument document, ISaveFile file) => AssertNoOpSerialization(document, file);
    }

    private class OutputDocument(IReadOnlyList<FileOutput> outputs) : BoundSaveDocument
    {
        public IReadOnlyList<FileOutput> Outputs { get; set; } = outputs;
        public override string Title => "Synthetic conformance output";
        public override ViewNode BuildView() => new SectionNode(Title, []);
        public override IReadOnlyList<FileOutput> Serialize() => Outputs;
    }

    private sealed class DeclaredSystemDocument(IReadOnlyList<FileOutput> outputs)
        : OutputDocument(outputs), IAdditionalOutputOwner
    {
        public IReadOnlyList<string> AdditionalOutputPaths => [SystemName];
    }
}
