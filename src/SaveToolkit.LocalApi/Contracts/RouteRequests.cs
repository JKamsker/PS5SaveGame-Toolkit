using System.Text.Json;
using System.Text.Json.Serialization;
using SaveToolkit.Sdk;

// These request bodies were declared beside their ASP.NET endpoints; the namespace is kept for
// wire/OpenAPI schema stability now that both hosts share them.
namespace SaveToolkit.Api.Endpoints;

/// <summary>
/// HTTP action-planning request. Omitting or sending null for Scope selects document scope; the
/// response contains the normalized scope used for planning.
/// </summary>
public sealed record PlanActionRequest(string ActionId, Dictionary<string, JsonElement>? Parameters,
    ActionScope? Scope = null, long? Revision = null);
public sealed record ApplyActionPlanRequest(string PlanId, [property: JsonRequired] long Revision);
public sealed record RevertChangeRequest(string ValueId, [property: JsonRequired] long Revision);

public sealed record OpenGroupRequest(string ProposalToken, string? ReleaseCleanDocumentId = null);
