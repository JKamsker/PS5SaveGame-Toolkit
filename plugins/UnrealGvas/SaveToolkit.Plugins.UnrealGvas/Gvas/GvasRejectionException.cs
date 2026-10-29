namespace SaveToolkit.Plugins.UnrealGvas.Gvas;

/// <summary>
/// A proven layout violation must escape speculative custom-native struct parsing.
/// The public parser translates this to the SDK's sealed SaveFormatException.
/// </summary>
internal sealed class GvasRejectionException(string message) : Exception(message);
