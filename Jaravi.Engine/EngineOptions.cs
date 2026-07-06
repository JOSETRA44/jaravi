namespace Jaravi.Engine;

public sealed class EngineOptions
{
    /// <summary>Roots under which sub-agent workdirs are allowed (Scope Gate). Empty = reject all spawns.</summary>
    public List<string> AllowedRoots { get; set; } = [];

    public int MaxConcurrentSessions { get; set; } = 8;

    /// <summary>Absolute cap of log lines returned per read, regardless of what a caller asks for.</summary>
    public int MaxReadLines { get; set; } = 500;

    /// <summary>Ring buffer capacity per session.</summary>
    public int LogBufferCapacity { get; set; } = 10_000;

    /// <summary>Watchdog tick for timeout/idle detection. Small values only make sense in tests.</summary>
    public int WatchdogIntervalMs { get; set; } = 5000;
}
