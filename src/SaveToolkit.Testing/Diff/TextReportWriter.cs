using System.Globalization;
using System.Text;

namespace SaveToolkit.Testing.Diff;

/// <summary>Human-readable rendering of diff reports and fixture verifications.</summary>
public static class TextReportWriter
{
    public static string Render(SaveDiffReport report, bool includeUnchanged = false)
    {
        var w = new StringBuilder();
        Line(w, $"before: {Describe(report.Before)}");
        Line(w, $"after:  {Describe(report.After)}");
        var t = report.Totals;
        Line(w, $"files:  {t.Changed} changed, {t.Added} added, {t.Removed} removed, {t.ChurnOnly} churn-only, {t.MirrorsMain} mirror main, {t.Unchanged} unchanged");
        Line(w, $"bytes:  {t.ChangedBytes} changed, {t.IgnoredBytes} masked as churn; semantic: {t.SemanticChanges} changes, {t.SemanticIgnored} masked, {t.SemanticErrors} decode failures");
        foreach (var rule in report.IgnoreRules)
        {
            Line(w, $"ignore: {rule}");
        }

        foreach (var file in report.Files.Where(f => includeUnchanged || f.Status != FileDiffStatus.Unchanged))
        {
            w.AppendLine();
            RenderFile(w, file);
        }

        return w.ToString();
    }

    public static string Render(FixtureVerification verification)
    {
        var w = new StringBuilder();
        Line(w, $"fixture {verification.FixtureId}: {(verification.Passed ? "PASS" : "FAIL")}");
        foreach (var error in verification.Errors)
        {
            Line(w, $"  error: {error}");
        }

        foreach (var result in verification.Expectations)
        {
            Line(w, $"  [{(result.Passed ? "ok" : "FAIL")}] {result.Expectation}{(result.Expectation.Note is { } n ? $" ({n})" : "")}: {result.Message}");
        }

        foreach (var item in verification.Unexpected)
        {
            Line(w, $"  [unexpected] {item}");
        }

        return w.ToString();
    }

    private static string Describe(SaveSetSummary s)
    {
        var twins = s.Twins.Mismatches.Count == 0
            ? $"{s.Twins.Identical} backup twins identical"
            : $"{s.Twins.Identical} backup twins identical, {s.Twins.Mismatches.Count} not: "
              + string.Join(", ", s.Twins.Mismatches.Take(5).Select(m => $"{m.Directory}/{m.Path}#g{m.Generation} {m.Status}"));
        return $"{s.Label} [generation {s.Generation}] {s.Files} files in {s.Directories} dirs, {s.Generations} generation(s), titles {string.Join(",", s.TitleIds)}; {twins}";
    }

    private static void RenderFile(StringBuilder w, FileDiff f)
    {
        var marker = f.Status switch
        {
            FileDiffStatus.Changed => "M",
            FileDiffStatus.Added => "A",
            FileDiffStatus.Removed => "D",
            FileDiffStatus.ChurnOnly => "~",
            FileDiffStatus.MirrorsMain => "=",
            _ => " ",
        };
        Line(w, $"{marker} {f.DisplayPath} [g{Gen(f.BeforeGeneration)}→g{Gen(f.AfterGeneration)}] {Len(f.BeforeLength)} → {Len(f.AfterLength)} bytes  {f.Status}");
        if (f.Note is { } note)
        {
            Line(w, $"    {note}");
        }

        if (f.Bytes is { } bytes)
        {
            Line(w, $"    {bytes.ChangedBytes} byte(s) changed in {bytes.RangeCount} range(s), {bytes.IgnoredBytes} masked{(bytes.Truncated ? $"; first {bytes.Ranges.Count} ranges shown" : "")}");
            foreach (var range in bytes.Ranges)
            {
                RenderRange(w, range);
            }
        }

        if (f.Semantic is { } semantic)
        {
            RenderSemantic(w, semantic);
        }
    }

    private static void RenderRange(StringBuilder w, ByteChange r)
    {
        Line(w, $"    0x{r.Offset:X8} +{r.Length} {r.Kind}{(r.DifferingBytes != r.Length ? $" ({r.DifferingBytes} differ)" : "")}");
        if (r.BeforeHex is null || r.AfterHex is null)
        {
            return;
        }

        Line(w, $"      before {HexWindow(r.BeforeHex, r.WindowStart, r.Offset, r.End)}");
        Line(w, $"      after  {HexWindow(r.AfterHex, r.WindowStart, r.Offset, r.End)}");
    }

    private static void RenderSemantic(StringBuilder w, SemanticDiffResult s)
    {
        if (s.Error is { } error)
        {
            Line(w, $"    semantic ({s.Decoder}): decode failed: {error}");
            return;
        }

        Line(w, $"    semantic ({s.Decoder}): {s.Changed} changed, {s.Added} added, {s.Removed} removed, {s.Ignored} masked{(s.InputTruncated ? "; input truncated by decode limits" : "")}{(s.Truncated ? $"; first {s.Changes.Count} shown" : "")}");
        foreach (var c in s.Changes)
        {
            var label = c.Label is { } l ? $"  ({l})" : "";
            Line(w, c.Kind switch
            {
                SemanticChangeKind.Added => $"      + {c.Path} = {c.After} [{c.AfterKind}]{label}",
                SemanticChangeKind.Removed => $"      - {c.Path} = {c.Before} [{c.BeforeKind}]{label}",
                _ => $"      ~ {c.Path}: {c.Before} → {c.After} [{(c.BeforeKind == c.AfterKind ? c.AfterKind : $"{c.BeforeKind}→{c.AfterKind}")}]{label}",
            });
        }

        foreach (var warning in s.Warnings.Take(10))
        {
            Line(w, $"      ! {warning}");
        }
    }

    /// <summary>Space-separated hex with the changed bytes in brackets.</summary>
    private static string HexWindow(string hex, long windowStart, long changeStart, long changeEnd)
    {
        if (hex.Length == 0)
        {
            return "(none)";
        }

        var w = new StringBuilder();
        for (var i = 0; i < hex.Length / 2; i++)
        {
            var offset = windowStart + i;
            w.Append(offset == changeStart ? '[' : ' ');
            w.Append(hex, i * 2, 2);
            if (offset == changeEnd - 1 || (offset == windowStart + (hex.Length / 2) - 1 && offset >= changeStart && offset < changeEnd))
            {
                w.Append(']');
            }
        }

        return w.ToString().TrimStart();
    }

    private static string Gen(int? generation) => generation?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Len(long? length) => length?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static void Line(StringBuilder w, string text) => w.Append(text).Append('\n');
}
