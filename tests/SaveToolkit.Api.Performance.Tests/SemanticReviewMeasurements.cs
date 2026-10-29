using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Plugins.BaldursGate3;
using SaveToolkit.Plugins.FinalFantasy16;
using SaveToolkit.Plugins.Pragmata;
using SaveToolkit.Plugins.TalesOfArise;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Api.Performance.Tests;

/// <summary>One synchronous session review request, excluding import, open, edits and HTTP plumbing.</summary>
[Collection(typeof(AllocationTestCollection))]
public sealed class SemanticReviewMeasurements(ITestOutputHelper output)
{
    private const int Repetitions = 7;
    private const int EditCount = 100;
    private const int WarmupIterations = 5;

    [Theory]
    [InlineData("Pragmata", "PPSA02530", false)]
    [InlineData("TalesOfArise", "PPSA02745", false)]
    [InlineData("FinalFantasy16", "PPSA10665", false)]
    [InlineData("BaldursGate3", "PPSA18463", false)]
    [InlineData("Pragmata", "PPSA02530", true)]
    [InlineData("TalesOfArise", "PPSA02745", true)]
    [InlineData("FinalFantasy16", "PPSA10665", true)]
    [InlineData("BaldursGate3", "PPSA18463", true)]
    [Trait("Category", "SamplePerformance")]
    public void RealQueryableReview(string name, string title, bool projection)
    {
        IGamePlugin plugin = name switch
        {
            "Pragmata" => new PragmataPlugin(),
            "TalesOfArise" => new TalesOfArisePlugin(),
            "FinalFantasy16" => new FinalFantasy16Plugin(),
            _ => new BaldursGate3Plugin(),
        };
        var file = SampleSaves.Load(title).AllDiskFiles.Where(f => !f.Directory.IsBackup && plugin.Probe(f).IsMatch
                && (name != "FinalFantasy16" || f.FileName == "game-save-data00")
                && (name != "TalesOfArise" || f.FileName == "SaveData_002.sav"))
            .MaxBy(f => f.Length);
        if (file is null) Assert.Skip($"Required gameplay sample unavailable: {name}.");
        var references = new ReferencePackStore([plugin]).Pin(plugin, title);
        var current = new CountingReviewDocument(plugin.Open(file, references), projection);
        var originalPages = 0;
        using var session = new DocumentSession("perf-review", file, plugin, current,
            openOriginal: () => new CountingReviewDocument(plugin.Open(file, references), projection, () => originalPages++), references: references);
        var measurement = new ApiMeasurement(output, name + (projection ? "-F8-projection" : "-full-view"));
        var allocationLimit = (name, projection) switch
        {
            ("Pragmata", false) => 650_000_000,
            ("Pragmata", true) => 150_000_000,
            ("BaldursGate3", false) => 100_000_000,
            ("BaldursGate3", true) => 22_000_000,
            ("TalesOfArise", false) => 60_000_000,
            ("TalesOfArise", true) => 40_000_000,
            ("FinalFantasy16", false) => 30_000_000,
            _ => 5_000_000,
        };
        SemanticChangeLog Review(string operation) => Measure(session, current, measurement, operation, allocationLimit);
        measurement.Describe($"File: {file.FileName}; {file.Length} bytes");
        // Discover canonical lazy sections before reading dynamic collection metadata.
        _ = ReviewSampleViews.Walk(current, current.BuildView()).Count();
        foreach (var collection in current.Collections.OrderByDescending(c => c.TotalCount))
            measurement.Describe($"Collection: {collection.Id}; {collection.TotalCount} rows");
        var firstPages = current.Pages;
        var firstBytes = GC.GetAllocatedBytesForCurrentThread();
        var firstStart = Stopwatch.GetTimestamp();
        var firstLog = session.GetChanges();
        var firstTime = Stopwatch.GetElapsedTime(firstStart).TotalMilliseconds;
        measurement.Record("no-edits-first", GC.GetAllocatedBytesForCurrentThread() - firstBytes, firstTime);
        measurement.Describe($"First review: {current.Pages - firstPages} current + {originalPages} original pages; complete={firstLog.Complete}");
        var initial = Review("no-edits");
        Assert.Empty(firstLog.Changes);
        Assert.Empty(initial.Changes);
        Assert.Equal(firstLog.Complete, initial.Complete);
        measurement.Describe($"Complete: {initial.Complete}; warnings: {string.Join(" | ", initial.Warnings)}");
        var fields = EditableFields(session, current).DistinctBy(f => f.ValueId).ToArray();
        measurement.Describe($"Editable integer fields: {fields.Length}");
        Assert.NotEmpty(fields);
        // Even spacing crosses native pages rather than selecting only the first 100 rows.
        var selected = Enumerable.Range(0, Math.Min(EditCount, fields.Length))
            .Select(i => fields[i * (fields.Length - 1) / Math.Max(1, Math.Min(EditCount, fields.Length) - 1)]).ToArray();
        var edits = selected.Select(f => Incoming(f, Alternative(f))).ToArray();
        var firstEdit = session.ApplyEdits([edits[0]]);
        Assert.True(firstEdit.Ok, string.Join(" | ", firstEdit.Errors.Select(e => e.Message)));
        var single = Review("one-edit");
        AssertProven(single, selected[..1], current);
        _ = EditableFields(session, current).Count(); // Republish after plugins rebuild structure on edits.
        var bulk = session.ApplyEdits(edits[1..]);
        Assert.True(bulk.Ok, string.Join(" | ", bulk.Errors.Select(e => e.Message)));
        for (var i = selected.Length; i < EditCount; i++)
        {
            var field = selected[i % selected.Length];
            var first = Alternative(field);
            var spec = (IntegerSpec)field.Spec;
            var second = first.AsInteger() < spec.Max && first.AsInteger() + 1 != field.Value.AsInteger()
                ? FieldValue.Of(first.AsInteger() + 1) : field.Value;
            var value = current.GetValue(field.ValueId) == first ? second : first;
            Assert.True(session.ApplyEdits([Incoming(field, value)]).Ok);
        }
        measurement.Describe($"100 edit operations over {selected.Length} distinct fields");
        var many = Review("100-edits");
        AssertProven(many, selected, current);
        _ = EditableFields(session, current).Count();
        Assert.True(session.ApplyEdits(selected.Select(f => Incoming(f, f.Value)).ToArray()).Ok);
        var reverted = Review("100-edits-reverted");
        Assert.Empty(reverted.Changes);
    }

    private static SemanticChangeLog Measure(DocumentSession session, CountingReviewDocument document,
        ApiMeasurement measurement, string operation, long allocationLimit)
    {
        // Warm each state; retain min-of-N timing as diagnostics. Page/allocation/result
        // assertions gate regressions without depending on scheduler preemption or host speed.
        SemanticChangeLog? log = null;
        var bestTime = double.MaxValue;
        int? pages = null;
        var run = 0;
        var startPages = 0;
        var allocation = AllocationProbe.Measure(() =>
        {
            var start = Stopwatch.GetTimestamp();
            log = session.GetChanges();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (run > WarmupIterations) bestTime = Math.Min(bestTime, elapsed);
        }, warmupRuns: WarmupIterations, measuredRuns: Repetitions, beforeRun: () =>
        {
            run++;
            log = null;
            startPages = document.Pages;
        }, afterRun: () =>
        {
            if (log is null) return;
            var queried = document.Pages - startPages;
            Assert.Equal(pages ?? queried, queried);
            pages = queried;
            if (!log.Complete) Assert.NotEmpty(log.Warnings);
        });

        Assert.NotNull(log);
        measurement.Record(operation, allocation.MinimumBytes, bestTime);
        measurement.Describe($"{operation}: {pages} current pages; {log.Changes.Count} changes; complete={log.Complete}");
        Assert.InRange(pages ?? 0, 0, DocumentSessionOptions.Default.MaxReviewQueryPages);
        allocation.AssertLessThan(allocationLimit, "One sample-backed semantic review.");
        return log;
    }

    private static IEnumerable<FieldNode> EditableFields(DocumentSession session, CountingReviewDocument document)
    {
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in ReviewSampleViews.Walk(session.BuildView(), session.Expand, expanded).OfType<FieldNode>())
            if (Editable(field)) yield return field;
        // Only retain test edit candidates; the measured review still walks all exposed collections.
        foreach (var collection in document.Collections.ToArray())
        {
            for (var offset = 0; offset < Math.Min(collection.TotalCount, DocumentSessionOptions.Default.MaxReviewQueryRows);
                offset += QueryableCollectionLimits.MaxPageSize)
            {
                var page = session.QueryCollection(collection.Id, new()
                { Offset = offset, Limit = QueryableCollectionLimits.MaxPageSize, IncludeEmpty = true });
                foreach (var row in page.Rows)
                    foreach (var cell in row.Cells.Values)
                        foreach (var field in ReviewSampleViews.Walk(cell, session.Expand, expanded).OfType<FieldNode>())
                            if (Editable(field)) yield return field;
            }
        }
    }

    private static FieldValue Alternative(FieldNode field)
    {
        var spec = (IntegerSpec)field.Spec;
        return FieldValue.Of(field.Value.AsInteger() == spec.Max ? field.Value.AsInteger() - 1 : field.Value.AsInteger() + 1);
    }

    private static IncomingEdit Incoming(FieldNode field, FieldValue value) =>
        new(field.ValueId, JsonSerializer.SerializeToElement(value));

    private static bool Editable(FieldNode field) => !field.Spec.ReadOnly && field.Spec is IntegerSpec spec
        && spec.Min < spec.Max && field.Value.Kind == FieldValueKind.Integer;

    private static void AssertProven(SemanticChangeLog log, IReadOnlyList<FieldNode> selected, ISaveDocument document)
    {
        if (log.Complete) Assert.All(selected.Where(f => document.GetValue(f.ValueId) != f.Value),
            f => Assert.Contains(log.Changes, c => c.ValueId == f.ValueId));
        else Assert.NotEmpty(log.Warnings);
        foreach (var field in selected)
            foreach (var change in log.Changes.Where(c => c.ValueId == field.ValueId))
            {
                Assert.Equal(field.Value, change.OriginalValue);
                Assert.Equal(document.GetValue(field.ValueId), change.CurrentValue);
            }
    }
}
