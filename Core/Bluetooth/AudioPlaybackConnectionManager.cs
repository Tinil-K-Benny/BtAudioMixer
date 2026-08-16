using BtAudioMixer.Core.Diagnostics;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BtAudioMixer.Core.Bluetooth
{
    /// <summary>
    /// Wraps Windows.Media.Audio.AudioPlaybackConnection — the WinRT API that lets
    /// this process accept an incoming A2DP stream from a paired phone and play it
    /// as a normal Windows audio session (the "PC as Bluetooth speaker" piece, plan
    /// §3). Ported from AudioPlaybackConnector2's C++/WinRT
    /// core/AudioConnectionService + DeviceDiscoveryService, kept standalone here
    /// per request rather than shelling out to that app.
    /// </summary>
    public sealed class AudioPlaybackConnectionManager : IDisposable
    {
        private readonly IAppLogger _logger;
        private AudioPlaybackConnection? _connection;

        public event EventHandler<AudioPlaybackConnectionState>? StateChanged;

        public AudioPlaybackConnectionManager(IAppLogger logger)
        {
            _logger = logger;
        }

        public bool IsConnected => _connection is not null;

        /// <summary>
        /// Lists paired devices capable of connecting as an A2DP source to this PC —
        /// i.e. candidate phones. Same selector AudioPlaybackConnector2 uses.
        /// </summary>
        public static async Task<IReadOnlyList<DeviceInformation>> ListCandidateDevicesAsync()
        {
            string selector = AudioPlaybackConnection.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);
            return devices.ToList();
        }

        /// <summary>
        /// Opens and starts an AudioPlaybackConnection to <paramref name="deviceId"/>.
        /// Once started, the phone's audio plays as this process's own audio session —
        /// route it to a specific capture device via Windows' per-app volume mixer
        /// (Settings > System > Sound > Volume mixer) so MixerEngine can pick it up
        /// without it also playing out the system default speakers.
        /// </summary>
        public async Task ConnectAsync(string deviceId)
        {
            Disconnect();

            var connection = AudioPlaybackConnection.TryCreateFromId(deviceId)
                ?? throw new InvalidOperationException($"Could not create an AudioPlaybackConnection for device '{deviceId}'.");

            connection.StateChanged += OnStateChanged;
            _connection = connection;

            await connection.StartAsync();

            var openResult = await connection.OpenAsync();
            if (openResult.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                _logger.LogError("AudioPlaybackConnectionManager",
                    $"OpenAsync failed for '{deviceId}': {openResult.Status}, ExtendedError=0x{openResult.ExtendedError:X8} ({openResult.ExtendedError})");
                Disconnect();
                throw new InvalidOperationException($"Failed to open Bluetooth audio connection: {openResult.Status}");
            }

            _logger.LogInformation("AudioPlaybackConnectionManager", $"Connected to '{deviceId}'.");
        }

        public void Disconnect()
        {
            if (_connection is null)
            {
                return;
            }

            try
            {
                _connection.StateChanged -= OnStateChanged;
                _connection.Dispose(); // WinRT IClosable.Close() projects to IDisposable.Dispose() in C#
            }
            catch (Exception ex)
            {
                _logger.LogError("AudioPlaybackConnectionManager", "Error closing connection.", ex);
            }

            _connection = null;
        }

        private void OnStateChanged(AudioPlaybackConnection sender, object args)
        {
            StateChanged?.Invoke(this, sender.State);
        }

        public void Dispose() => Disconnect();
    }
}
