using System.Threading.Channels;
using Jaravi.Core.Abstractions;
using Jaravi.Core.Events;

namespace Jaravi.Engine;

/// <summary>
/// In-process pub/sub over bounded channels. A slow subscriber (e.g. a stalled
/// Dashboard) drops its own oldest events; the engine is never back-pressured.
/// </summary>
public sealed class ChannelEventBus : IEventBus
{
    private const int SubscriberCapacity = 4096;

    private readonly object _gate = new();
    private readonly List<Subscription> _subscribers = [];

    public void Publish(JaraviEvent evt)
    {
        lock (_gate)
        {
            foreach (var sub in _subscribers)
                sub.Writer.TryWrite(evt); // DropOldest channel: TryWrite always succeeds
        }
    }

    public IEventSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<JaraviEvent>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        var sub = new Subscription(channel, this);
        lock (_gate) _subscribers.Add(sub);
        return sub;
    }

    private void Remove(Subscription sub)
    {
        lock (_gate) _subscribers.Remove(sub);
    }

    private sealed class Subscription(Channel<JaraviEvent> channel, ChannelEventBus bus) : IEventSubscription
    {
        public ChannelWriter<JaraviEvent> Writer => channel.Writer;
        public ChannelReader<JaraviEvent> Reader => channel.Reader;

        public void Dispose()
        {
            bus.Remove(this);
            channel.Writer.TryComplete();
        }
    }
}
