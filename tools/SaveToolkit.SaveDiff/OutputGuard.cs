namespace SaveToolkit.SaveDiff;

/// <summary>
/// The tool is read-only towards samples and the repository: reports may go to stdout or to a file
/// outside any <c>artifacts</c> folder and outside any git working tree.
/// </summary>
internal static class OutputGuard
{
    public static string Check(string path)
    {
        var full = Path.GetFullPath(path);
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(full)!); dir is not null; dir = dir.Parent)
        {
            if (dir.Name.Equals("artifacts", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException($"Refusing to write into an artifacts folder ({dir.FullName}).");
            }

            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                throw new UsageException($"Refusing to write inside a git working tree ({dir.FullName}); write to stdout or a folder outside the repo.");
            }
        }

        return full;
    }
}
