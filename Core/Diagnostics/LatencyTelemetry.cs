using System.Collections.Concurrent;
using System.Threading;

namespace BtAudioMixer.Core.Diagnostics
{
    /// <summary>
    /// Tracks per-channel buffering health. Ported from WindowsDualAudioManager's
    /// Core.Diagnostics.LatencyTelemetry, trimmed to what this project's pipeline
    /// actually reports (no per-sample peak/RMS tap is wired up here).
    /// </summary>
    public sealed class LatencyTelemetry
    {
        private sealed class ChannelState
        {
            public double BufferedMilliseconds;
            public long UnderrunCount;
            public long OverrunCount;
        }

        private readonly ConcurrentDictionary<string, ChannelState> _channels = new();
        private readonly IAppLogger _logger;

        public LatencyTelemetry(IAppLogger logger)
        {
            _logger = logger;
        }

        public void ReportBufferedMilliseconds(string channelId, double bufferedMilliseconds)
        {
            GetOrAddChannel(channelId).BufferedMilliseconds = bufferedMilliseconds;
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
