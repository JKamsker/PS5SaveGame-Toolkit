using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using SaveToolkit.Api.Endpoints;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Presets;

const long maxUploadBytes = 8L * 1024 * 1024 * 1024;
const int maxFormValues = 100_000;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUploadBytes);
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = maxUploadBytes;
    o.ValueCountLimit = maxFormValues;
});
builder.Services.ConfigureHttpJsonOptions(o => ToolkitJson.Configure(o.SerializerOptions));
// Route binding must reach our error handler in Production as well as Development.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.Configure<RouteOptions>(o => o.ConstraintMap[SpaPathConstraint.Name] = typeof(SpaPathConstraint));

builder.Services.AddToolkitOptions();
builder.Services.AddSingleton(_ => PluginCatalog.Discover());
builder.Services.AddSingleton(sp => new ReferencePackStore(
    sp.GetRequiredService<PluginCatalog>().Plugins,
    sp.GetRequiredService<IOptions<ToolkitOptions>>().Value.ToPackOptions()));
builder.Services.AddSingleton(sp => new PresetStore(
    sp.GetRequiredService<IOptions<ToolkitOptions>>().Value.ToPresetOptions()));
builder.Services.AddSingleton(sp => new WorkspaceStore(
    sp.GetRequiredService<PluginCatalog>(),
    sp.GetRequiredService<IOptions<ToolkitOptions>>().Value.ToWorkspaceOptions(),
    sp.GetRequiredService<ReferencePackStore>()));
builder.Services.AddSingleton(sp => new WorkspaceSessions(sp.GetRequiredService<WorkspaceStore>()));
builder.Services.AddHostedService<WorkspaceSweeper>();
builder.Services.AddExceptionHandler<ToolkitExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddToolkitOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(async status =>
{
    if (status.HttpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        await ApiProblem.Create(status.HttpContext.Response.StatusCode).ExecuteAsync(status.HttpContext);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<PresetRequestBodyLimitMiddleware>();
app.MapToolkitApi();
app.MapPresetApi();
app.MapReferenceApi();
app.MapVersionApi();
app.MapOpenApi();
// Only browser navigation falls back to the SPA; HEAD must retain API method rejection.
app.MapFallbackToFile(SpaPathConstraint.Pattern, "index.html")
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

app.Run();

/// <summary>Entry point marker for WebApplicationFactory in tests.</summary>
public partial class Program;
