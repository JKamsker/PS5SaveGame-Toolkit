using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class ImportedQuantitySampleTests
{
    [Fact]
    public void AllGameplaySamplesRestoreExactOpeningBytesAndUndoRedoToTheStagedReductions()
    {
        var files = SampleEvidenceTests.Files().Where(file => file.FileName != "data00-1.bin").ToArray();
        Assert.Equal(11, files.Length);
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            using var document = new PragmataDocument(file.RelativePath, original);
            using var session = new DocumentSession("restore-sample", (DiskSaveFile)file, new PragmataPlugin(), document);
            _ = session.BuildView();
            Assert.Equal(original, document.Serialize().Single().Content.ToArray());
            var positives = ImportedQuantityTests.Quantities(document).Where(node => document.Read(node).AsInt32() > 0).ToArray();
            Assert.NotEmpty(positives);
            Assert.True(session.ApplyEdits(positives.Select(node => ImportedQuantitySessionTests.Edit(node.Path, 0)).ToArray(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            var reduced = session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray();
            var scope = new ActionScope(document.Layout.Items.Select(QuantityActions.RowKey).ToArray());
            var preview = session.PlanAction(ImportedQuantityTests.ActionId, new Dictionary<string, System.Text.Json.JsonElement>(), scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(preview.PlanId);
            Assert.Equal(positives.Length, preview.Plan.Changes.Count);
            Assert.Equal(reduced, document.Serialize().Single().Content.ToArray());
            Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.Equal(original, document.Serialize().Single().Content.ToArray());
            Assert.Equal(original, session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray()); // Includes hashes/encryption and the preserved trailer.
            Assert.True(session.Undo().Ok);
            Assert.Equal(reduced, session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        }
    }
}
