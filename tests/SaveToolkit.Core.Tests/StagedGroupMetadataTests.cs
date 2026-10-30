using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class StagedGroupMetadataTests
{
    [Fact]
    public void HiddenMetadataIsReboundOnTheRestoredMutationCandidate()
    {
        using var staged = new StagedGroupDocument(() => new CandidateDocument(), _ => [], () => { }, 4096);
        Assert.Single(staged.ResolveFieldMetadata(["hidden"]));

        var result = staged.ApplyEdits([new ValueEdit("hidden", FieldValue.Of(2))]);

        Assert.True(result.Succeeded);
        Assert.Equal(2, staged.GetValue("hidden").AsInt32());
    }

    private sealed class CandidateDocument : BoundSaveDocument, IGroupedSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;

        public override string Title => "Candidate";
        public override bool CanEdit => true;
        public override ViewNode BuildView() => new TextNode("No rendered metadata");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        public DocumentMemento CaptureMemento() => CaptureSnapshot()!;

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            RegisterField(new FieldMetadata("hidden", "Hidden", IntegerSpec.Int32),
                () => FieldValue.Of(_value), value => _value = value.AsInt32());
            return base.ResolveFieldMetadata(valueIds);
        }

        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(_value, 64);

        protected override void LoadSnapshot(DocumentMemento snapshot) =>
            _value = ((SnapshotMemento<int>)snapshot).State;
    }
}
