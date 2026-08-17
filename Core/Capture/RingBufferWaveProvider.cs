using BtAudioMixer.Core.Buffering;
using BtAudioMixer.Core.Diagnostics;
using BtAudioMixer.Core.Platform;
using NAudio.Wave;
using System.Threading;

namespace BtAudioMixer.Core.Capture
{
    public sealed class RingBufferWaveProvider : IWaveProvider
    {
        private readonly SpscRingBuffer _ringBuffer;
        private readonly LatencyTelemetry _telemetry;
        private readonly string _channelId;
        private readonly MmcssThreadBooster _threadBooster;
        private int _threadBoostAttempted;

        public RingBufferWaveProvider(
            SpscRingBuffer ringBuffer,
            WaveFormat waveFormat,
            LatencyTelemetry telemetry,
            string channelId,
            MmcssThreadBooster threadBooster)
        {
            _ringBuffer = ringBuffer;
            WaveFormat = waveFormat;
            _telemetry = telemetry;
            _channelId = channelId;
            _threadBooster = threadBooster;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (Interlocked.CompareExchange(ref _threadBoostAttempted, 1, 0) == 0)
            {
                _threadBooster.TryBoostCurrentThread();
            }

            int bytesRead = _ringBuffer.Read(buffer, offset, count);

            if (bytesRead < count)
            {
                int shortfallBytes = count - bytesRead;
                Array.Clear(buffer, offset + bytesRead, shortfallBytes);

                double shortfallMilliseconds = (double)shortfallBytes / WaveFormat.AverageBytesPerSecond * 1000.0;
                _telemetry.ReportUnderrun(_channelId, shortfallMilliseconds);
            }

            return count;
        }
    }
}
