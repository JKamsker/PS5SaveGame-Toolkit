namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SourceSizeTests
{
    [Fact]
    public void PluginSourcesStayBelowThePluginLineLimit()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && directory.Name != "BaldursGate3")
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            Assert.Skip("Source checkout is unavailable.");
        }

        var files = Directory.EnumerateFiles(directory.FullName, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
        foreach (var file in files)
        {
            var lines = File.ReadLines(file).Count();
            Assert.True(lines < 400, $"{Path.GetRelativePath(directory.FullName, file)} has {lines} lines; split it by responsibility.");
        }
    }
}
