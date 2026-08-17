using BtAudioMixer.Core.Diagnostics;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BtAudioMixer.Core.Bluetooth
{
    public sealed class AudioPlaybackConnectionManager : IDisposable
    {
        private readonly FileAppLogger _logger;
        private AudioPlaybackConnection? _connection;

        public event EventHandler<AudioPlaybackConnectionState>? StateChanged;

        public AudioPlaybackConnectionManager(FileAppLogger logger)
        {
            _logger = logger;
        }

        public bool IsConnected => _connection is not null;

        public static async Task<IReadOnlyList<DeviceInformation>> ListCandidateDevicesAsync()
        {
            string selector = AudioPlaybackConnection.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);
            return devices.ToList();
        }

        public async Task ConnectAsync(string deviceId)
        {
            bool wasConnected = _connection is not null;
            Disconnect();

            if (wasConnected)
            {
                await Task.Delay(500);
            }

            var connection = AudioPlaybackConnection.TryCreateFromId(deviceId)
                ?? throw new InvalidOperationException($"Could not create an AudioPlaybackConnection for device '{deviceId}'.");

            connection.StateChanged += OnStateChanged;
            _connection = connection;

            await connection.StartAsync();

            _logger.LogInformation("AudioPlaybackConnectionManager", $"Prepared connection to '{deviceId}'.");
        }

        public async Task OpenAsync()
        {
            if (_connection is null)
            {
                throw new InvalidOperationException("Call ConnectAsync before OpenAsync.");
            }

            if (_connection.State == AudioPlaybackConnectionState.Opened)
            {
                return;
            }

            var openResult = await _connection.OpenAsync();
            if (openResult.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                _logger.LogError("AudioPlaybackConnectionManager",
                    $"OpenAsync failed: {openResult.Status}, ExtendedError=0x{openResult.ExtendedError:X8} ({openResult.ExtendedError})");
                throw new InvalidOperationException($"Failed to open Bluetooth audio connection: {openResult.Status}");
            }

            _logger.LogInformation("AudioPlaybackConnectionManager", "Bluetooth audio stream opened.");
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
                _connection.Dispose();
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
