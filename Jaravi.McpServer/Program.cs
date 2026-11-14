using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;
using Jaravi.Engine;
using Jaravi.Engine.Processes;
using Jaravi.Engine.Teams;
using Jaravi.McpServer;
using Jaravi.McpServer.Cli;
using Jaravi.McpServer.Hub;

// --help / --version are answered before ASP.NET exists: booting a web server
// to answer a help request is what left the last external client hanging.
if (CommandLine.TryHandleInfoFlags(args, out var infoExit))
    return infoExit;

// `hub` runs the per-user daemon; `hub status|stop` manage it from a shell.
var hubMode = args.Length > 0 && args[0] == "hub" && (args.Length == 1 || args[1].StartsWith('-'));
if (args.Length > 1 && args[0] == "hub" && !hubMode)
    return await HubCommand.RunAsync(args[1..]);

// CLI verbs (run, agents, doctor, team…) are answered without ASP.NET too. This is
// the path for an agent that has a shell but no MCP registration — clients read
// their MCP config only at startup, so a server discovered mid-session is
// unreachable over MCP until a restart. See CliRunner.
if (!hubMode && CliRunner.IsCliVerb(args))
    return await CliRunner.RunAsync(args);

// Stdio when an MCP client spawned us (stdin is a pipe), HTTP for a human at a
// terminal; --stdio / --http override. See CommandLine.ResolveMode.
var useStdio = !hubMode && CommandLine.ResolveMode(args) == RunMode.Stdio;

// An agent's shell tool also runs commands with stdin redirected, which is the
// very signal used to infer "an MCP client spawned us". So a bare `jaravi-mcp`
// typed by an agent becomes a server waiting forever on a pipe that will never
// carry JSON-RPC: first contact with Jaravi is a hang, with nothing said. A real
// MCP client ignores stderr, so this line costs a client nothing and tells the
// caller who was never a client exactly what to run instead.
if (useStdio && CommandLine.ModeWasInferred(args))
    Console.Error.WriteLine(
        "jaravi-mcp: no command given, so this is now an MCP stdio server waiting on stdin"
        + " and will not return. For the shell CLI run 'jaravi-mcp --help', or start here:"
        + " 'jaravi-mcp install' then 'jaravi-mcp agents'.");

// An MCP client's jaravi-mcp is a relay to the user's hub: one world shared by
// every agent and every client, instead of one private engine per client window.
// If the hub cannot come up, this client is still served — by a private engine,
// exactly as before the hub existed — and stderr says why.
if (useStdio && CommandLine.HubPreferred(args))
{
    try
    {
        var hub = await HubLauncher.EnsureAsync(TimeSpan.FromSeconds(20));
        return await new McpRelay(hub).RunAsync(CancellationToken.None);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine($"jaravi-mcp: hub unavailable ({ex.Message}); serving this client from a private engine. "
            + "Teams need the hub: run 'jaravi-mcp hub' in a terminal to see what stops it.");
    }
}

FileStream? hubLock = null;
string? hubToken = null;
if (hubMode)
{
    hubLock = HubFile.TryAcquireLock();
    if (hubLock is null)
    {
        Console.Error.WriteLine($"jaravi-mcp hub: another hub is already running for this user ({HubFile.Read()?.Url ?? "starting"}).");
        return 0;
    }
    hubToken = HubFile.NewToken();
    if (args.Contains("--log-file"))
        HubCommand.RedirectConsoleToLog(); // detached: no console to write to
}

// ContentRoot pinned to the exe location so agents.json/appsettings.json load
// no matter which working directory the parent spawns us from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// stdout is sacred in EVERY mode: it either carries JSON-RPC or nothing at all.
// Routing logs to stderr unconditionally means a misconfigured client can never
// be fed ASP.NET log lines where it expects a protocol frame.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

if (useStdio || hubMode)
{
    // The parent's log pane should not be flooded with ASP.NET lifecycle chatter
    // ("Application started", "Hosting environment"…). Keep the framework quiet
    // and let Jaravi's own messages through.
    builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
    builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
}

// Port resolution, single source of truth (env wins so a second instance / test
// can pick a free port): JARAVI_URL > ASPNETCORE_URLS > config "Urls" > default.
// If the chosen port is busy we fall back to an ephemeral one and log it, rather
// than crashing — a stdio instance keeps its MCP channel alive; an HTTP instance
// still serves, and the actual URL is printed for clients to discover.
// The hub always binds 127.0.0.1: it runs agents with auto-approval.
var desiredUrl = hubMode
    ? Environment.GetEnvironmentVariable("JARAVI_HUB_URL") ?? "http://127.0.0.1:5210"
    : Environment.GetEnvironmentVariable("JARAVI_URL")
      ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
      ?? builder.Configuration["Urls"]
      ?? "http://localhost:5210";
var firstUrl = desiredUrl.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
var boundUrl = IsPortInUse(new Uri(firstUrl).Port) ? "http://127.0.0.1:0" : firstUrl;
builder.WebHost.UseUrls(boundUrl);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JaraviJson.Options.PropertyNamingPolicy;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
    foreach (var converter in JaraviJson.Options.Converters)
        o.SerializerOptions.Converters.Add(converter);
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
    o.SerializerOptions.PropertyNameCaseInsensitive = true);

// ---- config: user dir (%APPDATA%\jaravi, editable) over package defaults ----
// As a global dotnet tool the install store is immutable; the package ships
// read-only defaults and the first run seeds an editable copy for the user.
var userConfigDir = JaraviConfig.UserConfigDir;
JaraviConfig.SeedUserConfig(userConfigDir);

builder.Configuration.AddJsonFile(Path.Combine(userConfigDir, "appsettings.json"), optional: true);
builder.Configuration.AddEnvironmentVariables(); // env vars keep the last word

var agentsFile = JaraviConfig.ResolveAgentsFile(userConfigDir);

// ---- engine composition root ----------------------------------------------
// Same recipe the CLI uses — one composition root, so both hosts resolve the
// identical registry, Scope Gate roots and limits.
var engineOptions = JaraviConfig.ResolveEngineOptions(builder.Configuration, defaultToCwd: !hubMode);
builder.Services.AddJaraviEngine(engineOptions, agentsFile);

// ---- teams ----------------------------------------------------------------
// The hub keeps teams in a durable store and gives spawned members their own MCP
// connection back to it. A private engine keeps a throwaway store: its members
// can be messaged live, but their own MCP connections would reach the hub, not it.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SessionIdentityMap>();
if (useStdio)
    builder.Services.AddSingleton<ICallerContext, ProcessCallerContext>();
else
    builder.Services.AddSingleton<ICallerContext, HttpCallerContext>();
builder.Services.AddJaraviTeams(
    hubMode ? Path.Combine(userConfigDir, "hub.db") : Path.Combine(Path.GetTempPath(), "jaravi", $"local-{Environment.ProcessId}.db"),
    engineOptions,
    hubMode ? JaraviConfig.SelfRelayFor : null);

// OpenAPI: the REST surface must be explorable without wrestling JSON-RPC
// framing — an external consumer looked for /swagger and found nothing.
if (!useStdio)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new()
    {
        Title = "Jaravi REST API",
        Version = CommandLine.Version,
        Description = "Control and telemetry for Jaravi sub-agent sessions and teams. "
                    + "The same engine is exposed to agents over MCP (/mcp).",
    }));
}

// ServerInstructions is the only chance to explain what Jaravi IS before a boss
// agent decides whether to touch it — clients inject it as a system message.
// Without it, agents saw eleven bare tools that spawn other AI agents and
// refused or ignored them; see JaraviInstructions for the reasoning.
var mcpBuilder = builder.Services.AddMcpServer(options => options.ServerInstructions = JaraviInstructions.Text)
    .WithTools<JaraviTools>()
    .WithTools<TeamTools>()
    .WithResources<JaraviResources>()
    // JaraviPrompts is static, so WithPrompts<T>() can't take it (CS0718 — C# forbids
    // a static type as a generic argument); FromAssembly discovers it by attribute.
    // The assembly is passed explicitly: the overload otherwise falls back to the
    // *calling* assembly, so moving this registration into a helper in another
    // assembly would silently publish zero prompts.
    .WithPromptsFromAssembly(typeof(JaraviPrompts).Assembly);
if (useStdio)
    mcpBuilder.WithStdioServerTransport();
else
    mcpBuilder.WithHttpTransport();

var app = builder.Build();

app.Logger.LogInformation("Agent registry: {AgentsFile} | Allowed roots: {Roots}",
    agentsFile, engineOptions.AllowedRoots.Count == 0 ? "(each caller's own directory)" : string.Join("; ", engineOptions.AllowedRoots));

app.Services.GetRequiredService<TeamService>().Start();

var instances = new InstanceRegistry(userConfigDir);
var repoRoot = hubMode
    ? "(hub — all projects)"
    : engineOptions.AllowedRoots.Count > 0 ? engineOptions.AllowedRoots[0] : Directory.GetCurrentDirectory();

// On startup the ephemeral fallback has resolved the real port, so this is where
// we announce the Control Center URL — loudly on stderr (stdout is the MCP pipe),
// and register the instance so sibling dashboards can discover it.
app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = (app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
        ?? boundUrl).Replace("[::]", "127.0.0.1").Replace("0.0.0.0", "127.0.0.1");

    app.Logger.LogInformation("Telemetry/REST listening on {Url}", url);
    instances.Register(url, repoRoot);
    if (hubMode)
        HubFile.Write(new HubInfo(Environment.ProcessId, url, hubToken!, CommandLine.Version, DateTimeOffset.UtcNow));

    // Unmissable banner even when logs are filtered — this is the URL the user
    // opens. ASCII-only so it renders in any terminal / code page.
    Console.Error.WriteLine();
    Console.Error.WriteLine(hubMode ? "  ===== Jaravi Hub =====" : "  ===== Jaravi Control Center =====");
    Console.Error.WriteLine($"    open:  {url}{(hubMode ? $"/?token={hubToken}" : "")}");
    Console.Error.WriteLine($"    repo:  {repoRoot}");
    Console.Error.WriteLine("  ================================");
    Console.Error.WriteLine();
});
app.Lifetime.ApplicationStopping.Register(() =>
{
    instances.Unregister();
    if (hubMode) HubFile.DeleteIfOwned(Environment.ProcessId);
});

if (hubMode)
    app.UseHubAuth(hubToken!);

app.UseWebSockets();

// ---- MCP endpoint (boss agents connect here) -------------------------------
if (!useStdio)
{
    app.MapMcp("/mcp");

    // /swagger (UI) and /swagger/v1/swagger.json (the document an external
    // consumer asked for, to script against REST without JSON-RPC framing).
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "Jaravi REST API"));
}

// ---- observer telemetry (Dashboard) ----------------------------------------
app.Map("/ws/events", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var bus = context.RequestServices.GetRequiredService<IEventBus>();
    await EventWebSocketHandler.HandleAsync(socket, bus, context.RequestAborted);
});

// ---- REST snapshot API (Dashboard bootstrap + control) ----------------------
var api = app.MapGroup("/api");

api.MapGet("/agents", (IAgentRegistry registry) => registry.GetAll());

api.MapGet("/instances", () => instances.List());

api.MapGet("/sessions", (ISessionManager sessions) => sessions.ListSnapshots());

api.MapGet("/sessions/{id}", (string id, ISessionManager sessions) =>
    Results.Ok(sessions.GetSnapshot(id)));

api.MapGet("/sessions/{id}/logs", (string id, ILogStore logs, long? sinceSeq, int? tail, string? grep, int maxLines = 200) =>
    logs.Read(id, new LogQuery { SinceSeq = sinceSeq, Tail = tail, Grep = grep, MaxLines = maxLines }));

api.MapGet("/sessions/{id}/summary", (string id, ISessionManager sessions) => sessions.GetSummary(id));

api.MapPost("/sessions", async (HttpContext ctx, ISessionManager sessions, ICallerContext caller, CancellationToken ct) =>
{
    SpawnRequest? request;
    try
    {
        request = await System.Text.Json.JsonSerializer.DeserializeAsync<SpawnRequest>(
            ctx.Request.Body, JaraviJson.Options, ct);
    }
    catch (System.Text.Json.JsonException ex)
    {
        return Results.BadRequest(new { error = "Invalid spawn request: " + ex.Message });
    }
    if (request is null) return Results.BadRequest(new { error = "Empty spawn request body." });
    // The caller's own directory is its Scope Gate grant, exactly as an MCP caller's is.
    return Results.Ok(await sessions.SpawnAsync(request with { CallerRoot = caller.CallerRoot }, ct));
});

api.MapPost("/sessions/{id}/kill", async (string id, ISessionManager sessions, CancellationToken ct) =>
    Results.Ok(await sessions.KillAsync(id, "killed via REST", ct)));

api.MapPost("/sessions/{id}/input", async (string id, InputRequest input, ISessionManager sessions, CancellationToken ct) =>
{
    await sessions.SendInputAsync(id, input.Text, input.Keys, ct);
    return Results.Ok(new { ok = true });
});

api.MapTeamApi();

if (hubMode)
{
    api.MapGet("/hub", () => new { pid = Environment.ProcessId, version = CommandLine.Version, store = Path.Combine(userConfigDir, "hub.db") });
    api.MapPost("/hub/stop", (IHostApplicationLifetime lifetime) =>
    {
        _ = Task.Delay(200).ContinueWith(_ => lifetime.StopApplication());
        return Results.Ok(new { stopping = true });
    });
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "jaravi-mcp-server" }));

// Liveness is not readiness, and conflating them cost real sessions. /healthz
// answers from a literal, so it stays 200 even when the engine behind it is
// wedged — and the CLI used exactly that to decide an instance was worth
// attaching to. It would attach, then die on the first real call.
//
// /readyz touches the engine: resolving the registry proves the DI singleton
// actually built, and listing sessions proves the session manager answers. A
// wedged instance does not reply here, so a short-timeout probe rejects it and
// the caller moves on to the next instance or starts its own engine.
app.MapGet("/readyz", (IAgentRegistry registry, ISessionManager sessions) =>
    Results.Ok(new
    {
        status = "ready",
        service = "jaravi-mcp-server",
        hub = hubMode,
        agents = registry.GetAll().Count,
        sessions = sessions.ListSnapshots().Count,
    }));

// ---- Web Control Center: single embedded page, no wwwroot on disk ----------
var dashboardHtml = new Lazy<string>(LoadEmbeddedDashboard);
app.MapGet("/", () => Results.Content(dashboardHtml.Value, "text/html; charset=utf-8"));

// Domain errors → clean HTTP status codes.
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (SessionNotFoundException ex) { await WriteProblem(context, StatusCodes.Status404NotFound, ex.Message); }
    catch (ProfileNotFoundException ex) { await WriteProblem(context, StatusCodes.Status404NotFound, ex.Message); }
    catch (ScopeGateException ex) { await WriteProblem(context, StatusCodes.Status403Forbidden, ex.Message); }
    catch (Jaravi.Core.Teams.NotATeamMemberException ex) { await WriteProblem(context, StatusCodes.Status401Unauthorized, ex.Message); }
    catch (JaraviException ex) { await WriteProblem(context, StatusCodes.Status400BadRequest, ex.Message); }
    catch (Microsoft.AspNetCore.Http.BadHttpRequestException ex) { await WriteProblem(context, StatusCodes.Status400BadRequest, ex.Message); }
});

app.Run();
hubLock?.Dispose();
return 0;

static async Task WriteProblem(HttpContext context, int status, string message)
{
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = message });
}

static string LoadEmbeddedDashboard()
{
    var asm = typeof(Program).Assembly;
    var name = asm.GetManifestResourceNames()
        .FirstOrDefault(n => n.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
    if (name is null) return "<h1>Jaravi</h1><p>Dashboard resource missing.</p>";
    using var stream = asm.GetManifestResourceStream(name)!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static bool IsPortInUse(int port)
{
    try
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return false;
    }
    catch (SocketException)
    {
        return true;
    }
}

internal sealed record InputRequest(string? Text, string[]? Keys);
