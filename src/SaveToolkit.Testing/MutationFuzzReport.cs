using System.Text.Json;

namespace SaveToolkit.Testing;

/// <summary>Optional metadata-only reports; neither input bytes nor raw worker output is persisted here.</summary>
internal static class MutationFuzzReport
{
    internal const string DirectoryVariable = "SAVETOOLKIT_FUZZ_REPORT_DIRECTORY";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    internal static void Write(MutationFuzzResult result)
    {
        if (Environment.GetEnvironmentVariable(DirectoryVariable) is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        var name = string.Concat(result.Plugin.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'));
        var suffix = result.Failures.Count > 0 ? "failure" : "result";
        File.WriteAllText(Path.Combine(directory, $"{name}-{result.Seed:X8}-{suffix}.json"),
            JsonSerializer.Serialize(result, SerializerOptions));
    }
}
