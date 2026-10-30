using System.Text.Json;

namespace SaveToolkit.Sdk;

/// <summary>Finite serializer depth covering 128 spec edges, union option wrappers and host response wrappers.</summary>
public static class MetadataJson
{
    /// <summary>Maximum JSON container depth. Spec graph depth is independently limited to 128.</summary>
    public const int MaxDepth = 512;
    /// <summary>Legacy general JSON settings, with sufficient finite depth for admitted metadata.</summary>
    public static JsonSerializerOptions Options { get; } = new() { MaxDepth = MaxDepth };
}
