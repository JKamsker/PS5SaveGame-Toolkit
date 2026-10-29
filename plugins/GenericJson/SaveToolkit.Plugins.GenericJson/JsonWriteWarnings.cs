namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonWriteWarnings
{
    internal const string ConsoleRisk = "Not validated on console. The game may reject or overwrite the edited save.";
    internal const string Raw = ConsoleRisk
        + " External integrity and game-specific dependencies are unknown. JSON validity does not establish game compatibility.";
}
