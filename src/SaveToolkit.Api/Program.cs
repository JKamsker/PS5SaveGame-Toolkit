using Microsoft.AspNetCore.Http.Features;
using SaveToolkit.Api.Endpoints;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;

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

var dataRoot = builder.Configuration["Toolkit:DataRoot"] is { Length: > 0 } configured
    ? configured
    : Path.Combine(Path.GetTempPath(), "SaveToolkit", "workspaces");
builder.Services.AddSingleton(_ => PluginCatalog.Discover());
var packRoot = builder.Configuration["Toolkit:PackRoot"] is { Length: > 0 } packs
    ? packs
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaveToolkit", "reference-packs");
var locales = builder.Configuration["Toolkit:Locales"] is { Length: > 0 } l
    ? l.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : ["en"];
builder.Services.AddSingleton(sp => new ReferencePackStore(
    sp.GetRequiredService<PluginCatalog>().Plugins,
    new ReferencePackOptions { StorageRoot = packRoot, PreferredLocales = locales }));
builder.Services.AddSingleton(sp => new WorkspaceStore(
    sp.GetRequiredService<PluginCatalog>(),
    new WorkspaceStoreOptions { DataRoot = dataRoot },
    sp.GetRequiredService<ReferencePackStore>()));
builder.Services.AddSingleton(sp => new WorkspaceSessions(sp.GetRequiredService<WorkspaceStore>()));
builder.Services.AddHostedService<WorkspaceSweeper>();
builder.Services.AddExceptionHandler<ToolkitExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(async status =>
{
    if (status.HttpContext.Request.Path.StartsWithSegments("/api"))
        await ApiProblem.Create(status.HttpContext.Response.StatusCode).ExecuteAsync(status.HttpContext);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.MapToolkitApi();
app.MapReferenceApi();
app.MapVersionApi();
// Only browser navigation falls back to the SPA; HEAD must retain API method rejection.
app.MapFallbackToFile(SpaPathConstraint.Pattern, "index.html")
    .WithMetadata(new HttpMethodMetadata([HttpMethods.Get]));

app.Run();

/// <summary>Entry point marker for WebApplicationFactory in tests.</summary>
public partial class Program;
