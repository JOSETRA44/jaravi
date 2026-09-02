using Jaravi.Core;
using Jaravi.Core.Abstractions;
using Jaravi.Engine;
using Jaravi.Engine.Processes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jaravi.McpServer;

/// <summary>
/// The one place that knows how a Jaravi engine is assembled from disk.
///
/// Two very different hosts need an identical engine: the web/MCP server
/// (<see cref="Program"/>) and the CLI (<see cref="Cli.CliRunner"/>). Before this
/// existed the recipe lived inline in Program.cs, which meant anything that was
/// not the web host could not build an engine at all — the reason a shell-only
/// agent had no way to reach Jaravi. Keep the recipe here and both hosts are
/// guaranteed to resolve the same agents.json, the same Scope Gate roots and the
/// same limits.
/// </summary>
public static class JaraviConfig
{
    /// <summary>Editable per-user config. The tool store is immutable, so this is where overrides live.</summary>
    public static string UserConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "jaravi");

    /// <summary>
    /// First run seeds an editable copy of the package defaults. Best-effort: a
    /// read-only or unavailable profile directory must not stop the server.
    /// </summary>
    public static void SeedUserConfig(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var agentsTarget = Path.Combine(dir, "agents.json");
            var agentsDefault = Path.Combine(AppContext.BaseDirectory, "agents.json");
            if (!File.Exists(agentsTarget) && File.Exists(agentsDefault))
                File.Copy(agentsDefault, agentsTarget);

            var settingsTarget = Path.Combine(dir, "appsettings.json");
            if (!File.Exists(settingsTarget))
                File.WriteAllText(settingsTarget,
                    "{\n  // Overrides de usuario para jaravi-mcp (gana sobre los defaults del paquete).\n" +
                    "  \"Engine\": {\n    \"AllowedRoots\": []\n  }\n}\n");
        }
        catch (IOException) { /* config dir unavailable → package defaults still work */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// agents.json resolution: JARAVI_AGENTS env → ./agents.json (project-local) →
    /// user config dir (only when running from the immutable tool store) → package default.
    /// </summary>
    public static string ResolveAgentsFile(string userConfigDir)
    {
        var inToolStore = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}.store{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

        string?[] candidates =
        [
            Environment.GetEnvironmentVariable("JARAVI_AGENTS"),
            Path.Combine(Directory.GetCurrentDirectory(), "agents.json"),
            inToolStore ? Path.Combine(userConfigDir, "agents.json") : null,
            Path.Combine(AppContext.BaseDirectory, "agents.json"),
        ];

        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            ?? throw new JaraviException(
                "No agents.json found (checked JARAVI_AGENTS, working directory, user config and package defaults).");
    }

    /// <summary>
    /// Configuration exactly as the web host layers it, for hosts that have no
    /// WebApplicationBuilder to do it for them: package defaults, then the user's
    /// editable overrides, then environment variables (which keep the last word).
    /// </summary>
    public static IConfigurationRoot BuildConfiguration(string userConfigDir) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(userConfigDir, "appsettings.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();

    /// <summary>
    /// Scope Gate roots come from config; an empty list would reject every spawn,
    /// so we fall back to the current directory — the same rule in both hosts.
    /// </summary>
    public static EngineOptions ResolveEngineOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection("Engine").Get<EngineOptions>() ?? new EngineOptions();
        if (options.AllowedRoots.Count == 0)
            options.AllowedRoots.Add(Directory.GetCurrentDirectory());
        return options;
    }

    /// <summary>Registers the full engine graph. Identical for the web host and the CLI.</summary>
    public static IServiceCollection AddJaraviEngine(
        this IServiceCollection services, EngineOptions engineOptions, string agentsFile)
    {
        services.AddSingleton(engineOptions);
        services.AddSingleton<IAgentRegistry>(_ => JsonAgentRegistry.LoadFromFile(agentsFile));
        services.AddSingleton<IAgentProcessFactory, PipeProcessFactory>();
        services.AddSingleton<ILogStore, RingBufferLogStore>();
        services.AddSingleton<IEventBus, ChannelEventBus>();
        services.AddSingleton<ISessionManager, SessionManager>();
        return services;
    }
}
