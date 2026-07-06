using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;
using Jaravi.Engine;
using Jaravi.Engine.Processes;
using Jaravi.McpServer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JaraviJson.Options.PropertyNamingPolicy;
    foreach (var converter in JaraviJson.Options.Converters)
        o.SerializerOptions.Converters.Add(converter);
});

// ---- engine composition root ----------------------------------------------
var engineOptions = builder.Configuration.GetSection("Engine").Get<EngineOptions>() ?? new EngineOptions();
if (engineOptions.AllowedRoots.Count == 0)
    engineOptions.AllowedRoots.Add(Directory.GetCurrentDirectory());

builder.Services.AddSingleton(engineOptions);
builder.Services.AddSingleton<IAgentRegistry>(_ =>
    JsonAgentRegistry.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "agents.json")));
builder.Services.AddSingleton<IAgentProcessFactory, PipeProcessFactory>();
builder.Services.AddSingleton<ILogStore, RingBufferLogStore>();
builder.Services.AddSingleton<IEventBus, ChannelEventBus>();
builder.Services.AddSingleton<ISessionManager, SessionManager>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<JaraviTools>();

var app = builder.Build();

app.UseWebSockets();

// ---- MCP endpoint (boss agents connect here) -------------------------------
app.MapMcp("/mcp");

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

api.MapGet("/sessions", (ISessionManager sessions) => sessions.ListSnapshots());

api.MapGet("/sessions/{id}", (string id, ISessionManager sessions) =>
    Results.Ok(sessions.GetSnapshot(id)));

api.MapGet("/sessions/{id}/logs", (string id, ILogStore logs, long? sinceSeq, int? tail, string? grep, int maxLines = 200) =>
    logs.Read(id, new LogQuery { SinceSeq = sinceSeq, Tail = tail, Grep = grep, MaxLines = maxLines }));

api.MapGet("/sessions/{id}/summary", (string id, ISessionManager sessions) => sessions.GetSummary(id));

api.MapPost("/sessions", async (SpawnRequest request, ISessionManager sessions, CancellationToken ct) =>
    Results.Ok(await sessions.SpawnAsync(request, ct)));

api.MapPost("/sessions/{id}/kill", async (string id, ISessionManager sessions, CancellationToken ct) =>
    Results.Ok(await sessions.KillAsync(id, "killed via REST", ct)));

api.MapPost("/sessions/{id}/input", async (string id, InputRequest input, ISessionManager sessions, CancellationToken ct) =>
{
    await sessions.SendInputAsync(id, input.Text, input.Keys, ct);
    return Results.Ok(new { ok = true });
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "jaravi-mcp-server" }));

// Domain errors → clean HTTP status codes.
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (SessionNotFoundException ex) { await WriteProblem(context, StatusCodes.Status404NotFound, ex.Message); }
    catch (ProfileNotFoundException ex) { await WriteProblem(context, StatusCodes.Status404NotFound, ex.Message); }
    catch (ScopeGateException ex) { await WriteProblem(context, StatusCodes.Status403Forbidden, ex.Message); }
    catch (JaraviException ex) { await WriteProblem(context, StatusCodes.Status400BadRequest, ex.Message); }
});

app.Run();

static async Task WriteProblem(HttpContext context, int status, string message)
{
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { error = message });
}

internal sealed record InputRequest(string? Text, string[]? Keys);
