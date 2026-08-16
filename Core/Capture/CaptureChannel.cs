using BtAudioMixer.Core.Buffering;
using BtAudioMixer.Core.Diagnostics;
using BtAudioMixer.Core.Output;
using BtAudioMixer.Core.Platform;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BtAudioMixer.Core.Capture
{
    /// <summary>
    /// Owns one WASAPI loopback capture of a specific render device (not necessarily
    /// the system default — this is what lets two sources be captured at once, unlike
    /// WindowsDualAudioManager's LoopbackCaptureService which only ever captures
    /// "the" default endpoint). Exposes the captured audio as an ISampleProvider
    /// already resampled to the mixer's common format and gain-scaled, ready to feed
    /// into a MixingSampleProvider.
    /// </summary>
    public sealed class CaptureChannel : IDisposable
    {
        private const int RingBufferSafetyMarginMs = 20;

        private readonly MMDevice _device;
        private readonly LatencyTelemetry _telemetry;
        private readonly MmcssThreadBooster _threadBooster;
        private readonly IAppLogger _logger;
        private WasapiLoopbackCapture? _capture;
        private SpscRingBuffer? _ringBuffer;
        private VolumeSampleProvider? _volumeProvider;

        public string ChannelId { get; }
        public ISampleProvider Output { get; private set; } = null!;

        public float Volume
        {
            get => _volumeProvider?.Volume ?? 1f;
            set
            {
                if (_volumeProvider is not null)
                {
                    _volumeProvider.Volume = Math.Clamp(value, 0f, 2f);
                }
            }
        }

        public CaptureChannel(string channelId, MMDevice device, WaveFormat mixFormat, float initialVolume,
            int targetLatencyMs, LatencyTelemetry telemetry, MmcssThreadBooster threadBooster, IAppLogger logger)
        {
            ChannelId = channelId;
            _device = device;
            _telemetry = telemetry;
            _threadBooster = threadBooster;
            _logger = logger;

            _capture = new WasapiLoopbackCapture(device)
            {
                ShareMode = AudioClientShareMode.Shared
            };

            var captureFormat = _capture.WaveFormat;

            int devicePeriodMs = targetLatencyMs;
            try
            {
                int deviceDefaultPeriodMs = (int)(device.AudioClient.DefaultDevicePeriod / 10000);
                if (deviceDefaultPeriodMs > devicePeriodMs)
                {
                    devicePeriodMs = deviceDefaultPeriodMs;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("CaptureChannel", $"Could not query device period for '{device.FriendlyName}': {ex.Message}.");
            }

            int ringBufferMs = Math.Max(devicePeriodMs + RingBufferSafetyMarginMs, devicePeriodMs * 3);
            int ringBufferCapacityBytes = captureFormat.AverageBytesPerSecond * ringBufferMs / 1000;
            _ringBuffer = new SpscRingBuffer(ringBufferCapacityBytes);

            var ringBufferProvider = new RingBufferWaveProvider(_ringBuffer, captureFormat, telemetry, channelId, threadBooster);
            ISampleProvider chain = ringBufferProvider.ToSampleProvider();
            chain = SampleFormatConverter.Convert(chain, mixFormat);

            _volumeProvider = new VolumeSampleProvider(chain)
            {
                Volume = Math.Clamp(initialVolume, 0f, 2f)
            };
            Output = _volumeProvider;

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
        }

        public void Start() => _capture?.StartRecording();

        public void Stop() => _capture?.StopRecording();

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _ringBuffer is null)
            {
                return;
            }

            int written = _ringBuffer.Write(e.Buffer, 0, e.BytesRecorded);
            if (written < e.BytesRecorded)
            {
                _telemetry.ReportOverrun(ChannelId, e.BytesRecorded - written);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception is not null)
            {
                _logger.LogError("CaptureChannel", $"Capture stopped unexpectedly for '{_device.FriendlyName}'.", e.Exception);
            }
        }

        public void Dispose()
        {
            if (_capture is null)
            {
                return;
            }

            var capture = _capture;
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            _capture = null;

            try
            {
                capture.StopRecording();
            }
            catch (Exception ex)
            {
                _logger.LogError("CaptureChannel", $"Error stopping capture for '{_device.FriendlyName}'.", ex);
            }

            capture.Dispose();
            _telemetry.RemoveChannel(ChannelId);
        }
    }
}
