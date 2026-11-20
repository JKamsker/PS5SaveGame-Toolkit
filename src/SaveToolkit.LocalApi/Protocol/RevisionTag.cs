using System.Globalization;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Sessions;

namespace SaveToolkit.LocalApi.Protocol;

/// <summary>The strong document revision entity tag exposed by document responses.</summary>
internal static class RevisionTag
{
    public static string Format(long revision) => $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";

    /// <summary>The revision carried by a document response value, if any.</summary>
    public static long? Of(object? value) => value switch
    {
        DocumentDto dto => dto.State.Revision,
        SessionEditResult edit => edit.State.Revision,
        ActionPreview preview => preview.Revision,
        _ => null,
    };

    public static string StaleMessage => new StaleViewException().Message;

    /// <summary>The 409 body for a JSON history revision that no longer matches the document.</summary>
    public static SessionEditResult StaleHistory(DocumentState state) => new()
    {
        Stale = true, State = state,
        Errors = [new SaveToolkit.Sdk.FieldError(string.Empty, StaleMessage)],
    };
}
