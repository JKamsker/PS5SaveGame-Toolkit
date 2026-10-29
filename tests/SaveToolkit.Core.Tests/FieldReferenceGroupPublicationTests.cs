using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferenceGroupPublicationTests
{
    [Theory]
    [InlineData("root", false)]
    [InlineData("expand", false)]
    [InlineData("query", false)]
    [InlineData("root", true)]
    [InlineData("expand", true)]
    [InlineData("query", true)]
    public void FailedGroupOutputDoesNotRememberRejectedPagesDuringCandidateReplay(string operation, bool annotationConflict)
    {
        using var fixture = new FieldReferenceSessionFixture();
        var opened = new List<FieldReferenceTransactionPlugin.Document>();
        IGroupedSaveDocument Open()
        {
            var candidate = new FieldReferenceTransactionPlugin.Document("save.dat");
            opened.Add(candidate);
            return candidate;
        }
        var group = new StagedGroupDocument(Open, document => document.Serialize(), () => { }, maxSnapshotBytes: 1024);
        using var session = new DocumentSession("group", fixture.Session.File, new FieldReferenceTransactionPlugin(), group,
            openOriginal: () => new FieldReferenceTransactionPlugin.Document("save.dat"));
        _ = session.BuildView();
        _ = session.Expand("good");
        var live = Assert.Single(opened);
        live.AnnotationConflict = annotationConflict;
        live.FaultActive = true;
        live.FailRoot = operation == "root";
        void Fail()
        {
            if (operation == "root") _ = session.BuildView();
            else if (operation == "expand") _ = session.Expand("bad");
            else _ = session.QueryCollection("items", new() { Limit = 1 });
        }
        if (annotationConflict) Assert.Throws<InvalidOperationException>(Fail);
        else Assert.Throws<FieldReferenceResponseLimitException>(Fail);
        live.FailRoot = false;
        live.FaultActive = false;
        var result = session.InvokeAction("rebuild", new Dictionary<string, JsonElement>());
        Assert.True(result.Ok);
        Assert.Empty(result.Warnings);
        var committed = Assert.Single(opened.Skip(1));
        Assert.Equal(0, committed.BadExpansions);
        Assert.Equal(0, committed.Queries);
        Assert.Equal(2, committed.GoodExpansions); // Candidate rebind and session structural rebind retain the successful section.
    }
}
