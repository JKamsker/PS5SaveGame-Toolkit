using Microsoft.AspNetCore.Http.Features;
using SaveToolkit.Api.Endpoints;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;

const long maxUpload = 8L * 1024 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUpload);
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = maxUpload;
    o.ValueCountLimit = 100_000;
});
builder.Services.ConfigureHttpJsonOptions(o => ToolkitJson.Configure(o.SerializerOptions));

var dataRoot = builder.Configuration["Toolkit:DataRoot"] is { Length: > 0 } configured
    ? configured
    : Path.Combine(Path.GetTempPath(), "SaveToolkit", "workspaces");
builder.Services.AddSingleton(_ => PluginCatalog.Discover());
builder.Services.AddSingleton(sp => new WorkspaceStore(sp.GetRequiredService<PluginCatalog>(), new WorkspaceStoreOptions { DataRoot = dataRoot }));
builder.Services.AddSingleton(sp => new WorkspaceSessions(sp.GetRequiredService<WorkspaceStore>()));
builder.Services.AddHostedService<WorkspaceSweeper>();
builder.Services.AddExceptionHandler<ToolkitExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapToolkitApi();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Entry point marker for WebApplicationFactory in tests.</summary>
public partial class Program;
