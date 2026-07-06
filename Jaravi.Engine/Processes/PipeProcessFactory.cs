using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;

namespace Jaravi.Engine.Processes;

/// <summary>
/// Pipe-based I/O strategy: redirected stdio. Child CLIs detect no TTY and
/// emit plain, non-interactive output — the deterministic default.
/// PTY (ConPTY) support plugs in behind the same <see cref="IAgentProcessFactory"/> port.
/// </summary>
public sealed class PipeProcessFactory : IAgentProcessFactory
{
    public Task<IAgentProcess> StartAsync(
        ProcessStartSpec spec,
        ChannelWriter<RawOutputLine> output,
        CancellationToken ct = default)
    {
        if (spec.Io == IoMode.Pty)
            throw new NotSupportedException(
                "PTY mode is not implemented yet; set the profile's io to \"pipe\".");

        var psi = new ProcessStartInfo
        {
            FileName = spec.Command,
            WorkingDirectory = spec.Workdir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in spec.Args)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in spec.Env)
            psi.Environment[key] = value;

        var process = new Process { StartInfo = psi };
        process.Start();

        var agentProcess = new PipeAgentProcess(process, output);
        return Task.FromResult<IAgentProcess>(agentProcess);
    }

    private sealed class PipeAgentProcess : IAgentProcess
    {
        private readonly Process _process;

        public PipeAgentProcess(Process process, ChannelWriter<RawOutputLine> output)
        {
            _process = process;
            Pid = process.Id;

            var stdout = PumpAsync(process.StandardOutput, LogStream.Stdout, output);
            var stderr = PumpAsync(process.StandardError, LogStream.Stderr, output);

            Exited = WaitForExitAsync(stdout, stderr, output);
        }

        public int Pid { get; }
        public Task<int> Exited { get; }

        public async ValueTask WriteInputAsync(string text, CancellationToken ct = default)
        {
            await _process.StandardInput.WriteLineAsync(text.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
        }

        public ValueTask SendKeysAsync(IReadOnlyList<string> keys, CancellationToken ct = default) =>
            throw new NotSupportedException("Symbolic keys require PTY mode; this session uses pipes.");

        public void KillTree()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* already exited */ }
        }

        public async ValueTask DisposeAsync()
        {
            KillTree();
            try { await Exited.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            _process.Dispose();
        }

        private static async Task PumpAsync(StreamReader reader, LogStream stream, ChannelWriter<RawOutputLine> output)
        {
            while (await reader.ReadLineAsync() is { } line)
                await output.WriteAsync(new RawOutputLine(stream, line));
        }

        private async Task<int> WaitForExitAsync(Task stdout, Task stderr, ChannelWriter<RawOutputLine> output)
        {
            try
            {
                await _process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr); // drain remaining buffered output
                return _process.ExitCode;
            }
            finally
            {
                output.TryComplete();
            }
        }
    }
}
