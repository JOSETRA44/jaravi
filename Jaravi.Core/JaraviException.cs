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
