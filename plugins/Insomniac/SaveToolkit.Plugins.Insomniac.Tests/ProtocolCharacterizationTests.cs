using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class ProtocolCharacterizationTests
{
    // Fingerprints captured before the maintainability refactor. They pin complete serialized
    // protocol objects and ordered sink events without committing private sample content.
    // Gameplay views were intentionally repinned for the currency tab and corrected counter labels (#40).
    // The honest-label review repins only gameplay UI wording; decoder evidence stays unchanged (#40).
    [Theory]
    [InlineData(false, 0, "48632f9b81d78f5c3672cc5f6eb2861ad2dd970faddf15b03af05602c8ff8d52",
        "71e8abc6c279f1835e0eedc3f5632b2e1050dd3e0614af75e2e01b1bfa7c2ab2")]
    [InlineData(false, 1, "e6d20d7d2d3bc200ac9759f945413f5dbdb6e0ab837293604850dc19768408c8",
        "25025f4f7aa4cf4146d11de5ede2c6292dfbe422d1340e0cf85caa0184b6144e")]
    [InlineData(true, 0, "79499c9bd9f5d7b642ab7626e400b269b878d36fbfee2cd23a7df5af9253af01",
        "c2d7bb354d38d68a99398698c20f4600a9cc6e56975cc3657383e2b6431e497e")]
    public void RepresentativeViewsPagesAndEvidenceRetainTheirProtocol(
        bool preferences, int generation, string expectedView, string expectedEvidence)
    {
        using var culture = new InvariantCultureScope();
        var file = preferences
            ? SampleSaves.Load("PPSA01474").AllDiskFiles.First(f => f.FileName == "i29-userprefs.save" && !f.Directory.IsBackup)
            : PlayerCorpusTests.Find("i29slot0-s-autosave", generation);
        var plugin = new InsomniacPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        using var document = plugin.Open(file, references);
        using var view = new ProtocolDigest();
        var root = Assert.IsType<TabsNode>(document.BuildView());
        view.Add<ViewNode>(root);
        var systems = root.Tabs.Single(tab => tab.Title == "Systems");
        foreach (var section in systems.Children.OfType<SectionNode>())
            view.Add(document.ExpandNode(section.LazyId!));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        view.Add(queryable.Collections);
        foreach (var collection in queryable.Collections)
            view.Add(queryable.Query(collection.Id, new() { Limit = 3 }));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());

        using var sink = new RecordingSink();
        Assert.Single(plugin.Decoders).Decode(file, sink);
        var actualView = view.Finish();
        var actualEvidence = sink.Finish();
        Assert.True(expectedView == actualView && expectedEvidence == actualEvidence,
            $"View: {actualView}; evidence: {actualEvidence}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(1000)]
    public void SinkRefusalPreservesTheExactPrefixAndStopsAtEveryEmissionPhase(int refuseAt)
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var decoder = Assert.Single(new InsomniacPlugin().Decoders);
        using var complete = new RecordingSink();
        decoder.Decode(file, complete);
        Assert.True(complete.AddCalls > refuseAt);
        using var rejecting = new RecordingSink(refuseAt);
        decoder.Decode(file, rejecting);
        Assert.Equal(refuseAt, rejecting.AddCalls);
        Assert.Equal(complete.Events.Take(rejecting.Events.Count), rejecting.Events);
    }

    private sealed class ProtocolDigest : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        internal void Add<T>(T value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            _hash.AppendData(Encoding.ASCII.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture) + ":"));
            _hash.AppendData(bytes);
        }

        internal string Finish() => Convert.ToHexStringLower(_hash.GetHashAndReset());
        public void Dispose() => _hash.Dispose();
    }

    private sealed class InvariantCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        internal InvariantCultureScope() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    private sealed class RecordingSink(int refuseAt = int.MaxValue) : ISaveDecodeSink, IDisposable
    {
        private readonly ProtocolDigest _digest = new();
        internal List<string> Events { get; } = [];
        internal int AddCalls { get; private set; }
        public int MaxValues => int.MaxValue;
        public int MaxValueLength => int.MaxValue;
        public int MaxDepth => 64;
        public long MaxFileBytes => long.MaxValue;

        public bool Add(string path, string kind, string value, string? label = null)
        {
            Record("add", path, kind, value, label);
            return ++AddCalls < refuseAt;
        }

        public void Warn(string message) => Record("warn", message);
        public void MarkTruncated(string reason) => Record("truncated", reason);
        private void Record(params string?[] values)
        {
            _digest.Add(values);
            // Prefix comparisons also retain event boundaries and order.
            Events.Add(JsonSerializer.Serialize(values));
        }

        internal string Finish() => _digest.Finish();
        public void Dispose() => _digest.Dispose();
    }
}
