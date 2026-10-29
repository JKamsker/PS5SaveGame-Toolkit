namespace SaveToolkit.Sdk.Views;

/// <summary>Hard publication limits, mirrored by the browser. Split larger views into lazy sections.</summary>
public static class SpatialViewLimits
{
    /// <summary>Maximum graph items or map markers per node.</summary>
    public const int MaxItems = 512;
    /// <summary>Maximum prerequisite edges per graph node.</summary>
    public const int MaxEdges = 2048;
    /// <summary>Maximum text-property length in UTF-16 code units.</summary>
    public const int MaxTextLength = 4096;
    /// <summary>Maximum identifier length in UTF-16 code units.</summary>
    public const int MaxIdLength = 256;
    /// <summary>Maximum enclosing navigation path components.</summary>
    public const int MaxPathParts = 32;
    /// <summary>Maximum absolute coordinate or transform component.</summary>
    public const double MaxCoordinate = 1e12;
}
