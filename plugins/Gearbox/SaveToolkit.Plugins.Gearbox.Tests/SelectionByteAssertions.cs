using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>Complete live bytes, independent of the session's clean/dirty export delta.</summary>
internal static class SelectionByteAssertions
{
    internal static Dictionary<string, byte[]> Capture(ISaveDocument document, IReadOnlyList<string> expectedPaths)
    {
        Assert.NotEmpty(expectedPaths);
        var outputs = document.Serialize();
        Assert.Equal(expectedPaths.Order(StringComparer.Ordinal), outputs.Select(output => output.RelativePath).Order(StringComparer.Ordinal));
        Assert.All(outputs, output => Assert.False(output.Content.IsEmpty));
        return outputs.ToDictionary(output => output.RelativePath, output => output.Content.ToArray(), StringComparer.Ordinal);
    }

    internal static void Equal(ISaveDocument document, IReadOnlyDictionary<string, byte[]> expected) =>
        Equal(expected, Capture(document, expected.Keys.ToArray()));

    internal static void Equal(IReadOnlyDictionary<string, byte[]> expected, IReadOnlyDictionary<string, byte[]> actual)
    {
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in expected)
        {
            Assert.NotEmpty(bytes);
            Assert.Equal(bytes, actual[path]);
        }
    }
}
