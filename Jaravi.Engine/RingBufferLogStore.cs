using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Models;

namespace Jaravi.Engine;

/// <summary>
/// Bounded in-memory log storage per session. Reads are always capped — this is
/// the mechanism that makes flooding the boss agent's context impossible.
/// </summary>
public sealed class RingBufferLogStore(EngineOptions options) : ILogStore
{
    private readonly ConcurrentDictionary<string, SessionLog> _logs = new();

    public LogEntry Append(string sessionId, LogStream stream, string text)
    {
        var log = _logs.GetOrAdd(sessionId, _ => new SessionLog(options.LogBufferCapacity));
        return log.Append(stream, text);
    }

    public IReadOnlyList<LogEntry> Read(string sessionId, LogQuery query)
    {
        if (!_logs.TryGetValue(sessionId, out var log)) return [];
        return log.Read(query, hardCap: options.MaxReadLines);
    }

    public long GetLineCount(string sessionId) =>
        _logs.TryGetValue(sessionId, out var log) ? log.TotalCount : 0;

    private sealed class SessionLog(int capacity)
    {
        private readonly object _gate = new();
        private readonly Queue<LogEntry> _buffer = new(capacity);
        private long _nextSeq = 1;
        private long _totalCount;

        public long TotalCount { get { lock (_gate) return _totalCount; } }

        public LogEntry Append(LogStream stream, string text)
        {
            lock (_gate)
            {
                var entry = new LogEntry(_nextSeq++, DateTimeOffset.UtcNow, stream, text);
                if (_buffer.Count >= capacity) _buffer.Dequeue();
                _buffer.Enqueue(entry);
                _totalCount++;
                return entry;
            }
        }

        public IReadOnlyList<LogEntry> Read(LogQuery query, int hardCap)
        {
            LogEntry[] snapshot;
            lock (_gate) snapshot = [.. _buffer];

            IEnumerable<LogEntry> result = snapshot;

            if (query.SinceSeq is { } since)
                result = result.Where(e => e.Seq > since);

            if (!string.IsNullOrEmpty(query.Grep))
            {
                Regex? regex = null;
                try { regex = new Regex(query.Grep, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)); }
                catch (ArgumentException) { /* invalid pattern → literal match */ }

                result = regex is not null
                    ? result.Where(e => SafeIsMatch(regex, e.Text))
                    : result.Where(e => e.Text.Contains(query.Grep, StringComparison.OrdinalIgnoreCase));
            }

            var list = result.ToList();

            if (query.Tail is { } tail && list.Count > tail)
                list = list[^tail..];

            var cap = Math.Min(Math.Max(1, query.MaxLines), hardCap);
            if (list.Count > cap)
                list = list[..cap];

            return list;
        }

        private static bool SafeIsMatch(Regex regex, string text)
        {
            try { return regex.IsMatch(text); }
            catch (RegexMatchTimeoutException) { return false; }
        }
    }
}
