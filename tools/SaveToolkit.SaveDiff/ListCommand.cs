using System.Globalization;
using System.Text;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff;

/// <summary>Inventory of one save set: what to record in a capture note and what generations exist.</summary>
internal static class ListCommand
{
    private sealed record ListedFile(string Path, long Length, string Sha256);

    private sealed record ListedDirectory(string TitleId, string RawName, string Name, bool IsBackup, int Generation, string? MainTitle, string? SubTitle, IReadOnlyList<ListedFile> Files);

    public static int Run(CliArguments args, TextWriter stdout)
    {
        args.ExpectPositionals(1, "savediff list <save>");
        var json = CliApp.WantsJson(args);
        var output = CliApp.OutputPath(args, args.Positionals);
        using var loaded = SaveSetLoader.Load(args.Positionals[0]);
        var directories = loaded.Set.Directories.Select(d => new ListedDirectory(
            d.TitleId,
            d.RawName,
            d.Name,
            d.IsBackup,
            d.Generation,
            d.ParamSfo?.MainTitle,
            d.ParamSfo?.SubTitle,
            [.. d.Files.Concat(d.SystemFiles).Cast<Core.Storage.DiskSaveFile>().Select(f => new ListedFile(f.PathInDirectory, f.Length, f.Sha256))])).ToList();

        var source = CliApp.DisplayPath(loaded.Source);
        CliApp.Emit(output, stdout, json ? DiffJson.Serialize(new { source, directories }) + "\n" : Render(source, directories));
        return CliApp.ExitSame;
    }

    private static string Render(string source, IReadOnlyList<ListedDirectory> directories)
    {
        var w = new StringBuilder();
        w.Append(CultureInfo.InvariantCulture, $"{source}: {directories.Count} save directories, {directories.Select(d => d.Generation).DefaultIfEmpty(-1).Max() + 1} generation(s)\n");
        foreach (var d in directories.OrderBy(d => d.Generation).ThenBy(d => d.TitleId, StringComparer.Ordinal).ThenBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.IsBackup))
        {
            w.Append(CultureInfo.InvariantCulture, $"g{d.Generation} {d.TitleId}/{d.RawName}{(d.IsBackup ? " (backup)" : "")}{(d.MainTitle is { } t ? $" \"{t}\"" : "")}{(d.SubTitle is { Length: > 0 } st ? $" / \"{st}\"" : "")}\n");
            foreach (var f in d.Files)
            {
                w.Append(CultureInfo.InvariantCulture, $"    {f.Length,12}  {f.Sha256[..16]}  {f.Path}\n");
            }
        }

        return w.ToString();
    }
}
