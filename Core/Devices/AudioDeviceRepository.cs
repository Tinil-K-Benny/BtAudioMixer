using BtAudioMixer.Core.Diagnostics;
using NAudio.CoreAudioApi;

namespace BtAudioMixer.Core.Devices
{
    /// <summary>
    /// Enumerates render endpoints. Ported from WindowsDualAudioManager's
    /// Core.Devices.AudioDeviceRepository, trimmed of the hotplug notification
    /// plumbing (IMMNotificationClient / DeviceRemoved / DeviceStateChanged) that
    /// WindowsDualAudioManager's UI subscribed to but nothing here does.
    /// </summary>
    public sealed class AudioDeviceRepository : IDisposable
    {
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private readonly IAppLogger _logger;

        public AudioDeviceRepository(IAppLogger logger)
        {
            _logger = logger;
            _deviceEnumerator = new MMDeviceEnumerator();
        }

        public List<AudioDevice> GetRenderDevices()
        {
            var devices = new List<AudioDevice>();

            string defaultDeviceId;
            try
            {
                defaultDeviceId = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AudioDeviceRepository", $"Could not resolve the default render endpoint: {ex.Message}");
                defaultDeviceId = string.Empty;
            }

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
