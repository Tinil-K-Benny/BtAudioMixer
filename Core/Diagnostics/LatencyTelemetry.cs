using System.Collections.Concurrent;
using System.Threading;

namespace BtAudioMixer.Core.Diagnostics
{
    public sealed class LatencyTelemetry
    {
        private sealed class ChannelState
        {
            public long UnderrunCount;
            public long OverrunCount;
        }

        private readonly ConcurrentDictionary<string, ChannelState> _channels = new();
        private readonly FileAppLogger _logger;

        public LatencyTelemetry(FileAppLogger logger)
        {
            _logger = logger;
        }

        public void ReportUnderrun(string channelId, double shortfallMilliseconds)
        {
            var channel = GetOrAddChannel(channelId);
            Interlocked.Increment(ref channel.UnderrunCount);
            _logger.LogWarning("LatencyTelemetry", $"Underrun on channel '{channelId}': {shortfallMilliseconds:F1}ms shortfall.");
        }

        public void ReportOverrun(string channelId, int discardedBytes)
        {
            var channel = GetOrAddChannel(channelId);
            Interlocked.Increment(ref channel.OverrunCount);
            _logger.LogWarning("LatencyTelemetry", $"Overrun on channel '{channelId}': discarded {discardedBytes} bytes.");
        }

        public void RemoveChannel(string channelId)
        {
            _channels.TryRemove(channelId, out _);
        }

        private ChannelState GetOrAddChannel(string channelId)
        {
            return _channels.GetOrAdd(channelId, static _ => new ChannelState());
        }
    }
}
