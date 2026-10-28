using System.Text.Json.Serialization;

namespace SaveToolkit.Testing.Diff;

public sealed record SaveDiffOptions
{
    public GenerationSelector BeforeGeneration { get; init; }

    public GenerationSelector AfterGeneration { get; init; }

    /// <summary>Also compare <c>sce_sys/*</c> (param.sfo, keystone, icon0.png).</summary>
    public bool IncludeSystemFiles { get; init; }

    /// <summary>Globs; a file is compared when one matches its path in the directory or <c>rawDirectory/path</c>. Empty = all.</summary>
    public IReadOnlyList<string> FileFilters { get; init; } = [];

    /// <summary>Only files of this title id; null = all titles.</summary>
    public string? TitleFilter { get; init; }

    public ChurnFilter Churn { get; init; } = ChurnFilter.None;

    public ByteDiffOptions Bytes { get; init; } = new();

    /// <summary>Semantic decoder; null = byte mode only.</summary>
    public ISaveDecoder? Decoder { get; init; }

    public DecodeLimits DecodeLimits { get; init; } = new();

    public int MaxSemanticChanges { get; init; } = 500;

    /// <summary>Do not diff a backup file again when its content equals its main twin on both sides.</summary>
    public bool CollapseMirroredBackups { get; init; } = true;
}

[JsonConverter(typeof(JsonStringEnumConverter<FileDiffStatus>))]
public enum FileDiffStatus
{
    Unchanged,
    Changed,

    /// <summary>Content differs, but only inside masked churn.</summary>
    ChurnOnly,
    Added,
    Removed,

    /// <summary>A backup (<c>sdimg_sce_bu_</c>) whose before and after content equal its main twin's; see the main file.</summary>
    MirrorsMain,
}

[JsonConverter(typeof(JsonStringEnumConverter<TwinStatus>))]
public enum TwinStatus
{
    Identical,
    Differs,
    MissingBackup,
    MissingMain,
}

/// <summary>Main file vs. its console backup twin inside one save set.</summary>
public sealed record TwinCheck(string TitleId, string Directory, string Path, int Generation, TwinStatus Status);

public sealed record TwinSummary
{
    public int Identical { get; init; }

    /// <summary>All pairs that are not identical (differing content or a missing twin).</summary>
    public IReadOnlyList<TwinCheck> Mismatches { get; init; } = [];
}

public sealed record SaveSetSummary
{
    public required string Label { get; init; }
    public required string Generation { get; init; }
    public int Directories { get; init; }
    public int Files { get; init; }
    public int Generations { get; init; }
    public IReadOnlyList<string> TitleIds { get; init; } = [];
    public TwinSummary Twins { get; init; } = new();
}

public sealed record FileDiff
{
    public required string TitleId { get; init; }

    /// <summary>Logical save directory name (without the <c>sdimg_</c> prefixes).</summary>
    public required string Directory { get; init; }

    /// <summary>Path inside the save directory.</summary>
    public required string Path { get; init; }

    public bool IsBackup { get; init; }

    /// <summary>Generation (ZIP occurrence index) of the before/after copy; null when absent.</summary>
    public int? BeforeGeneration { get; init; }

    public int? AfterGeneration { get; init; }

    public FileDiffStatus Status { get; init; }

    public long? BeforeLength { get; init; }
    public long? AfterLength { get; init; }
    public string? BeforeSha256 { get; init; }
    public string? AfterSha256 { get; init; }

    public ByteDiffResult? Bytes { get; init; }

    public SemanticDiffResult? Semantic { get; init; }

    /// <summary>Why a file was skipped or collapsed (churn reason, mirrored backup).</summary>
    public string? Note { get; init; }

    public string DisplayPath =>
        $"{TitleId}/{(IsBackup ? "sdimg_sce_bu_" : "sdimg_")}{Directory}/{Path}";

    public bool IsChange => Status is FileDiffStatus.Changed or FileDiffStatus.Added or FileDiffStatus.Removed;
}

public sealed record DiffTotals
{
    public int Files { get; init; }
    public int Changed { get; init; }
    public int ChurnOnly { get; init; }
    public int Added { get; init; }
    public int Removed { get; init; }
    public int Unchanged { get; init; }
    public int MirrorsMain { get; init; }
    public long ChangedBytes { get; init; }
    public long IgnoredBytes { get; init; }
    public int SemanticChanges { get; init; }
    public int SemanticIgnored { get; init; }

    /// <summary>Files whose semantic decode failed (the byte diff is still reported).</summary>
    public int SemanticErrors { get; init; }
}

public sealed record SaveDiffReport
{
    public const string FormatId = "savetoolkit-savediff/1";

    public string Format { get; init; } = FormatId;
    public required SaveSetSummary Before { get; init; }
    public required SaveSetSummary After { get; init; }
    public required DiffTotals Totals { get; init; }

    /// <summary>Every compared file pair, unchanged ones included.</summary>
    public required IReadOnlyList<FileDiff> Files { get; init; }

    public IReadOnlyList<string> IgnoreRules { get; init; } = [];

    public bool HasChanges => Files.Any(f => f.IsChange);
}
