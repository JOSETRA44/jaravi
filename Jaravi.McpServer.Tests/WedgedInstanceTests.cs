using System.Net;
using Jaravi.McpServer.Cli;

namespace Jaravi.McpServer.Tests;

/// <summary>
/// Tests that drive the CLI end to end, or redirect Console, or move the process
/// working directory, cannot run beside each other: all three are process-global.
/// Two such classes in parallel leak one test's stdout into the other's buffer,
/// which fails whichever one happens to be parsing it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessGlobalCollection
{
    public const string Name = "process-global state";
}

/// <summary>
/// The failure that made agents stop trying.
///
/// A live Jaravi can keep answering /healthz long after its engine has stopped
/// answering anything else — the engine is built by a DI singleton factory, so one
/// blocked construction wedges every request behind it while Kestrel itself stays
/// perfectly healthy. The CLI probed /healthz, concluded the instance was fine,
/// attached to it, and then died on the first real call with an unhandled
/// TaskCanceledException: thirty lines of stack trace and exit 127 — which every
/// shell reads as "command not found".
///
/// An agent that sees that concludes Jaravi is not installed and does the work
/// itself. Both halves are pinned here: do not adopt a wedged instance, and never
/// leave through an unhandled exception if one is adopted anyway.
/// </summary>
[Collection(ProcessGlobalCollection.Name)]
public class WedgedInstanceTests
{
    /// <summary>
    /// A server that is alive but not serving: /healthz answers instantly, and
    /// everything touching the engine hangs. This is the real shape of the bug,
    /// not an approximation of it.
    /// </summary>
    private sealed class WedgedServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _loop;

        public string Url { get; }
        public bool ReadyzIsImplemented { get; init; } = true;

        public WedgedServer(int port, bool readyzImplemented = true)
        {
            ReadyzIsImplemented = readyzImplemented;
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }

                var path = context.Request.Url?.AbsolutePath ?? "/";

                if (path == "/healthz")
                {
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                    continue;
                }

                if (path == "/readyz" && !ReadyzIsImplemented)
                {
                    // An older server that predates the readiness endpoint.
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                // Everything that needs the engine: accepted, never answered.
                _ = context;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stopping.Cancel();
            _listener.Abort();
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
            _listener.Close();
            _stopping.Dispose();
        }
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static Jaravi.Engine.EngineOptions Options() =>
        new() { AllowedRoots = [Environment.CurrentDirectory] };

    private static string AgentsFile() =>
        Path.Combine(Environment.CurrentDirectory, "agents.json");

    [Fact]
    public async Task A_wedged_instance_is_not_adopted()
    {
        // /readyz never answers, so the probe times out and the instance is
        // refused. Probing /healthz instead — what it used to do — adopted this
        // very server, because /healthz answers from a literal and knows nothing
        // about the engine behind it.
        await using var server = new WedgedServer(FreePort());

        var failure = await Assert.ThrowsAsync<Core.JaraviException>(() =>
            JaraviClientFactory.ResolveAsync(
                server.Url, Environment.CurrentDirectory, Options(), AgentsFile(), CancellationToken.None));

        // An explicit --url is an instruction, so refusing loudly is correct here;
        // what must never happen is handing back a client bound to a dead engine.
        Assert.Contains("No Jaravi instance answered", failure.Message);
    }

    [Fact]
    public async Task A_server_without_readyz_is_still_adopted()
    {
        // Readiness probing must not refuse to talk to an older but working peer:
        // a 404 means the endpoint is new, not that the server is broken.
        await using var server = new WedgedServer(FreePort(), readyzImplemented: false);

        var resolution = await JaraviClientFactory.ResolveAsync(
            server.Url, Environment.CurrentDirectory, Options(), AgentsFile(), CancellationToken.None);

        await using var client = resolution.Client;

        Assert.IsType<RestJaraviClient>(client);
    }

    [Fact]
    public async Task A_transport_failure_exits_through_the_contract_not_a_stack_trace()
    {
        // The instance passes the readiness probe and then stops answering — a
        // server killed or wedged between the probe and the call. The caller must
        // get one line and a documented exit code. 127 is not in the contract, and
        // it is the code a shell uses for "command not found".
        await using var server = new WedgedServer(FreePort(), readyzImplemented: false);

        var error = new StringWriter();
        var previous = Console.Error;
        Console.SetError(error);
        try
        {
            var exit = await CliRunner.RunAsync(
                ["run", "--agent", "echo-demo", "--task", "ping", "--url", server.Url, "--quiet"]);

            Assert.Equal(CliRunner.ExitCode.Error, exit);
            Assert.DoesNotContain("Unhandled exception", error.ToString());
            Assert.DoesNotContain("   at ", error.ToString());
            Assert.Contains("jaravi:", error.ToString());
        }
        finally
        {
            Console.SetError(previous);
        }
    }
}
