using System.Threading.Channels;
using Jaravi.Core.Events;

namespace Jaravi.Core.Abstractions;

/// <summary>
/// In-process pub/sub for telemetry. Publishing never blocks: slow subscribers
/// drop their oldest events rather than back-pressuring the engine.
/// </summary>
public interface IEventBus
{
    void Publish(JaraviEvent evt);
    IEventSubscription Subscribe();
}

public interface IEventSubscription : IDisposable
{
    ChannelReader<JaraviEvent> Reader { get; }
}
