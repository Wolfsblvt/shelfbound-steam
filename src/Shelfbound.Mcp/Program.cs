using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shelfbound.Mcp;
using Shelfbound.Mcp.Logging;
using Shelfbound.Storage;
using Shelfbound.Storage.Config;

var builder = Host.CreateApplicationBuilder(args);

// MCP over stdio uses stdout for the protocol — all logging MUST go to stderr. The sink redacts
// secret-bearing query values before writing, and Steam's named HTTP client suppresses URI-bearing
// framework Information logs. See docs/project/mcp-design.md.
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new RedactingStderrLoggerProvider());
builder.Logging.AddSteamWebApiLogging();

builder.Services.AddSteamWebApiClient();
builder.Services.AddSingleton<SnapshotContext>();
builder.Services.AddSingleton<IUserDataStore>(_ => new JsonUserDataStore(ShelfboundPaths.ProfilesDirectory));

builder.Services
    .AddMcpServer(options => options.ServerInstructions =
        """
        Shelfbound exposes the user's real Steam library plus their saved taste and context.
        - Save what the user states: when they give an opinion, status, completion, played-elsewhere,
          or what a category means, immediately call the matching tool (record_game_opinion,
          record_game_status, set_game_completion, set_category_definition, remember). Save only what
          the user explicitly says — never guesses.
        - Recall before recommending: call get_profile_status and get_remembered. If the profile is
          sparse, offer a short taste onboarding (ask about a few suggested games and general
          preferences), then save them. Ask what any undefined categories mean and save the meaning.
        - Mind the library scope: get_library_summary returns 'scope'. If it is 'installedOnly', only
          installed games are present — never tell the user they don't own a game just because a search
          returned nothing; say it isn't installed, and that the full owned library needs a Steam Web
          API key (shelfbound setup --steam-api-key-stdin).
        """)
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

IHost host = builder.Build();

// Load (scan + optional enrich) the library snapshot before serving tools.
await host.Services.GetRequiredService<SnapshotContext>().InitializeAsync();

await host.RunAsync();
