using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

internal sealed class CoverageFixture : IDisposable
{
    public const string RecoveryAction = "character/recover-current-vitals";
    public const string LevelAction = "character/set-level";

    public CoverageFixture(string title, string name, Action<byte[], Ff7Schema>? arrange = null)
    {
        Schema = Ff7Schema.ForTitle(title)!;
        var diskFile = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == name);
        Original = diskFile.ReadAllBytes();
        arrange?.Invoke(Original, Schema);
        File = MemorySaveSet.SingleFile(name, Original, title);
        Document = Plugin.Open(File);
        Session = new("coverage", diskFile, Plugin, Document, openOriginal: () => Plugin.Open(File));
        _ = Session.BuildView();
    }

    public FinalFantasy7Plugin Plugin { get; } = new();
    public Ff7Schema Schema { get; }
    public byte[] Original { get; }
    public ISaveFile File { get; }
    public ISaveDocument Document { get; }
    public DocumentSession Session { get; }
    public IActionPlanningDocument Planner => (IActionPlanningDocument)Document;
    public byte[] Bytes() => Assert.Single(Document.Serialize()).Content.ToArray();
    public static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    public static uint Read(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    public ActionPreview Preview(string action, int character, int? level = null) => Session.PlanAction(action,
        level is { } target ? new Dictionary<string, JsonElement> { ["level"] = JsonSerializer.SerializeToElement(target) } : [],
        new([$"character/{character}"]));

    public byte[] ApplyAndVerifyHistory(ActionPreview preview, IEnumerable<int> allowedOffsets)
    {
        Assert.NotNull(preview.PlanId);
        Assert.Equal(Original, Bytes()); // Preview, including a dismissed preview, writes nothing.
        Assert.False(Session.State.CanUndo);
        Assert.True(Session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var edited = Bytes();
        ByteChangeAssertions.OnlyOffsetsChanged(Original, edited, allowedOffsets);
        using var reopened = Plugin.Open(MemorySaveSet.SingleFile(File.FileName, edited, File.Directory.TitleId));
        _ = reopened.BuildView();
        Assert.Equal(edited, Assert.Single(reopened.Serialize()).Content.ToArray());
        Assert.Equal(LinkedRecords.Parse(Original, Schema, false), LinkedRecords.Parse(edited, Schema, false));
        foreach (var change in preview.Plan.Changes) Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId));
        Assert.True(Session.Undo().Ok);
        Assert.False(Session.State.CanUndo);
        Assert.Equal(Original, Bytes());
        Assert.True(Session.Redo().Ok);
        Assert.Equal(edited, Bytes());
        Assert.True(Session.ApplyActionPlan(preview.PlanId, preview.Revision).Stale);
        return edited;
    }

    public void Dispose() => Session.Dispose();
}
