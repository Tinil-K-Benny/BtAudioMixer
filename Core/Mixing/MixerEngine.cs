using BtAudioMixer.Core.Capture;
using BtAudioMixer.Core.Diagnostics;
using BtAudioMixer.Core.Platform;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BtAudioMixer.Core.Mixing
{
    public sealed class MixerEngine : IDisposable
    {
        public static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        private readonly LatencyTelemetry _telemetry;
        private readonly MmcssThreadBooster _threadBooster;
        private readonly FileAppLogger _logger;
        private readonly int _targetLatencyMs;

        private CaptureChannel? _phoneChannel;
        private CaptureChannel? _systemChannel;
        private IWavePlayer? _outputPlayer;
        private MixingSampleProvider? _mixer;

        public bool IsRunning { get; private set; }

        public float PhoneVolume
        {
            get => _phoneChannel?.Volume ?? 1f;
            set { if (_phoneChannel is not null) _phoneChannel.Volume = value; }
        }

        public float SystemVolume
        {
            get => _systemChannel?.Volume ?? 1f;
            set { if (_systemChannel is not null) _systemChannel.Volume = value; }
        }

        public MixerEngine(LatencyTelemetry telemetry, MmcssThreadBooster threadBooster, FileAppLogger logger, int targetLatencyMs = 40)
        {
            _telemetry = telemetry;
            _threadBooster = threadBooster;
            _logger = logger;
            _targetLatencyMs = targetLatencyMs;
        }

        public void Start(MMDevice phoneSourceDevice, MMDevice systemSourceDevice, MMDevice outputDevice,
            float initialPhoneVolume, float initialSystemVolume)
        {
            if (IsRunning)
            {
                return;
            }

            _phoneChannel = new CaptureChannel("phone", phoneSourceDevice, MixFormat, initialPhoneVolume,
                _targetLatencyMs, _telemetry, _threadBooster, _logger);
            _systemChannel = new CaptureChannel("system", systemSourceDevice, MixFormat, initialSystemVolume,
                _targetLatencyMs, _telemetry, _threadBooster, _logger);

            _mixer = new MixingSampleProvider(MixFormat) { ReadFully = true };
            _mixer.AddMixerInput(_phoneChannel.Output);
            _mixer.AddMixerInput(_systemChannel.Output);

            ISampleProvider finalChain = new SoftClipSampleProvider(_mixer);

            _outputPlayer = CreateWavePlayer(outputDevice, finalChain.ToWaveProvider());

            _phoneChannel.Start();
            _systemChannel.Start();
            _outputPlayer.Play();

            IsRunning = true;
        }

        private IWavePlayer CreateWavePlayer(MMDevice device, IWaveProvider waveProvider)
        {
            try
            {
                var player = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: _targetLatencyMs);
                player.Init(waveProvider);
                return player;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("MixerEngine", $"Event-driven WASAPI init failed for '{device.FriendlyName}', falling back to timer-driven mode: {ex.Message}");
                var fallback = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: false, latency: _targetLatencyMs);
                fallback.Init(waveProvider);
                return fallback;
            }
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            try
            {
                _outputPlayer?.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError("MixerEngine", "Error stopping output player.", ex);
            }

            _outputPlayer?.Dispose();
            _outputPlayer = null;

            _phoneChannel?.Dispose();
            _phoneChannel = null;
            _systemChannel?.Dispose();
            _systemChannel = null;
            _mixer = null;

            IsRunning = false;
        }

        public void Dispose() => Stop();
    }
}
