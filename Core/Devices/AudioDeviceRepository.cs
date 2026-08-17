using BtAudioMixer.Core.Diagnostics;
using NAudio.CoreAudioApi;

namespace BtAudioMixer.Core.Devices
{
    public sealed class AudioDeviceRepository : IDisposable
    {
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private readonly FileAppLogger _logger;

        public AudioDeviceRepository(FileAppLogger logger)
        {
            _logger = logger;
            _deviceEnumerator = new MMDeviceEnumerator();
        }

        public List<AudioDevice> GetRenderDevices()
        {
            var devices = new List<AudioDevice>();
            string? defaultDeviceId = GetDefaultDeviceId();

            foreach (var device in _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(new AudioDevice
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    IsDefault = device.ID == defaultDeviceId
                });
            }

            return devices;
        }

        public string? GetDefaultDeviceId()
        {
            try
            {
                return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AudioDeviceRepository", $"Could not resolve the default render endpoint: {ex.Message}");
                return null;
            }
        }

        public MMDevice GetDevice(string deviceId)
        {
            return _deviceEnumerator.GetDevice(deviceId);
        }

        public void Dispose()
        {
            _deviceEnumerator.Dispose();
        }
    }
}
