namespace Jaravi.Core;

/// <summary>Base for domain errors that map cleanly to MCP tool errors / HTTP responses.</summary>
public class JaraviException(string message) : Exception(message);

public sealed class ProfileNotFoundException(string profileId)
    : JaraviException($"Agent profile '{profileId}' is not registered.");

public sealed class SessionNotFoundException(string sessionId)
    : JaraviException($"Session '{sessionId}' does not exist.");

/// <summary>Engine-level security boundary: spawn outside allowed roots.</summary>
public sealed class ScopeGateException(string workdir)
    : JaraviException($"Workdir '{workdir}' is outside the allowed roots (scope gate).");

/// <summary>Two sessions declared overlapping exclusive path claims.</summary>
public sealed class ClaimConflictException(string path, string holderSessionId)
    : JaraviException($"Path claim '{path}' conflicts with active session '{holderSessionId}'.")
{
    public string Path { get; } = path;
    public string HolderSessionId { get; } = holderSessionId;
}

/// <summary>Pipelines only read from finished sessions — their output is immutable.</summary>
public sealed class PipelineSourceNotTerminalException(string sessionId)
    : JaraviException($"Pipeline source session '{sessionId}' has not finished — its output is not yet stable.");
