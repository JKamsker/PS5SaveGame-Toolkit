using System.Diagnostics;
using System.Globalization;

namespace SaveToolkit.Testing;

/// <summary>
/// Per-process scratch folder for extracted samples. The root defaults to <c>%TEMP%/SaveToolkit.Tests</c>
/// and can be moved with <c>SAVETOOLKIT_TEST_TEMP</c>. A process deletes its own folder on exit, and
/// folders left behind by processes that no longer run are swept on first use, so repeated test runs
/// cannot accumulate extracted samples.
/// </summary>
public static class TestScratch
{
    public const string RootVariable = "SAVETOOLKIT_TEST_TEMP";

    private static readonly Lazy<string> Folder = new(Create);

    /// <summary>This process's scratch folder (created and registered for cleanup on first access).</summary>
    public static string ProcessFolder => Folder.Value;

    public static string Root =>
        Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Path.GetTempPath(), "SaveToolkit.Tests");

    /// <summary>
    /// Deletes run folders named <c>{pid}-{guid}</c> under <paramref name="root"/> whose process is not alive.
    /// Best effort: folders that cannot be removed are left for a later sweep.
    /// </summary>
    public static int SweepStale(string root, Func<int, bool> isAlive)
    {
        if (!Directory.Exists(root)) return 0;
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            var dash = name.IndexOf('-', StringComparison.Ordinal);
            if (dash <= 0 || !int.TryParse(name.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
            if (isAlive(pid)) continue;
            if (TryDelete(dir)) removed++;
        }

        return removed;
    }

    private static string Create()
    {
        var root = Root;
        SweepStale(root, IsAlive);
        var folder = Path.Combine(root, $"{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(folder);
        return folder;
    }

    private static bool IsAlive(int pid)
    {
        if (pid == Environment.ProcessId) return true;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
