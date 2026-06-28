using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shelfbound.Mcp;
using Shelfbound.Steam.Web;
using Shelfbound.Storage;
using Shelfbound.Storage.Config;

var builder = Host.CreateApplicationBuilder(args);

// MCP over stdio uses stdout for the protocol — all logging MUST go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddHttpClient<ISteamWebApiClient, SteamWebApiClient>();
builder.Services.AddSingleton<SnapshotContext>();
builder.Services.AddSingleton<IUserDataStore>(_ => new JsonUserDataStore(ShelfboundPaths.ProfilesDirectory));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

IHost host = builder.Build();

// Load (scan + optional enrich) the library snapshot before serving tools.
await host.Services.GetRequiredService<SnapshotContext>().InitializeAsync();

await host.RunAsync();
