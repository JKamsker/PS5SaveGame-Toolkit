using System.Collections.Concurrent;
using SaveToolkit.Core.Storage;
using Xunit;

namespace SaveToolkit.Testing;

/// <summary>
/// Access to the decrypted sample dumps in <c>artifacts/PPSAxxxxx_saves_dec*.zip</c>. The folder is found
/// via the <c>SAVETOOLKIT_ARTIFACTS</c> environment variable or by walking up from the test binary
/// (works from git worktrees under <c>.worktrees/</c>). Tests skip when a sample is unavailable.
/// </summary>
public static class SampleSaves
{
    private static readonly ConcurrentDictionary<string, Lazy<DiskSaveSet>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "SaveToolkit.Tests", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static string? ArtifactsDirectory { get; } = Locate();

    public static string? FindArchive(string titleId)
    {
        if (ArtifactsDirectory is null)
        {
            return null;
        }

        return Directory.EnumerateFiles(ArtifactsDirectory, $"{titleId}_saves_dec*.zip").OrderBy(p => p.Length).FirstOrDefault();
    }

    /// <summary>Imports the sample for <paramref name="titleId"/> once per test process, or skips the test.</summary>
    public static DiskSaveSet Load(string titleId)
    {
        var archive = FindArchive(titleId);
        if (archive is null)
        {
            Assert.Skip($"Sample {titleId} not found. Put {titleId}_saves_dec.zip into artifacts/ or set SAVETOOLKIT_ARTIFACTS.");
        }

        return Cache.GetOrAdd(titleId, _ => new Lazy<DiskSaveSet>(() =>
            SaveSetImporter.Import([new ImportSource(Path.GetFileName(archive), () => File.OpenRead(archive))], Path.Combine(TempRoot, titleId)))).Value;
    }

    private static string? Locate()
    {
        if (Environment.GetEnvironmentVariable("SAVETOOLKIT_ARTIFACTS") is { Length: > 0 } fromEnv && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "artifacts");
            if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*_saves_dec*.zip").Any())
            {
                return candidate;
            }
        }

        return null;
    }
}
