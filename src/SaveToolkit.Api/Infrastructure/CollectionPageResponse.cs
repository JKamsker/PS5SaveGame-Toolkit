using SaveToolkit.Api.Contracts;
using SaveToolkit.Core;
using SaveToolkit.LocalApi.Protocol;

namespace SaveToolkit.Api.Infrastructure;

internal static class CollectionPageResponse
{
    internal const int MaximumBytes = CollectionPageSerializer.MaximumBytes;

    internal static IResult Create(QueryablePageDto page, bool throwOnLimit = false)
    {
        if (CollectionPageSerializer.TrySerialize(page) is { } content)
            return Results.Bytes(content, "application/json");
        if (throwOnLimit) throw new BadHttpRequestException(CollectionPageSerializer.LimitMessage,
            StatusCodes.Status413PayloadTooLarge);
        return Results.Json(new ErrorDto(CollectionPageSerializer.LimitMessage),
            statusCode: StatusCodes.Status413PayloadTooLarge, options: ToolkitJson.Options);
    }
}
