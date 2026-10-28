namespace SaveToolkit.Sdk.Views;

/// <summary>Hard publication limits, mirrored by the browser. Split larger views into lazy sections.</summary>
public static class SpatialViewLimits
{
    public const int MaxItems = 512;
    public const int MaxEdges = 2048;
    public const int MaxTextLength = 4096;
    public const int MaxIdLength = 256;
    public const int MaxPathParts = 32;
    public const double MaxCoordinate = 1e12;
}
