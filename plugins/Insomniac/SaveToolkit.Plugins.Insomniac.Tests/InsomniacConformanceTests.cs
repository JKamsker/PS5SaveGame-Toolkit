using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class InsomniacConformanceTests : PluginConformanceTests<InsomniacPlugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => ["PPSA01474"];
    protected override int MaxFilesPerTitle => 128;

    // Integrity is unresolved. Rejection of meaningful edits is tested by PlayerCorpusTests.
    protected override IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields) => [];
}
