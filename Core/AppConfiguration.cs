using BtAudioMixer.Core.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BtAudioMixer.Core
{
    /// <summary>
    /// Persisted settings: which devices to use for each role, and each source's
    /// last volume, so the mixer comes back up the same way it was left.
    /// </summary>
    public class AppConfiguration
    {
        private const string ConfigFileName = "config.json";

        private static string ConfigFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BtAudioMixer",
            ConfigFileName);

        public string? PhoneBluetoothDeviceId { get; set; }
        public string? PhoneSourceDeviceId { get; set; }
        public string? SystemSourceDeviceId { get; set; }
        public string? OutputDeviceId { get; set; }
        public float PhoneVolume { get; set; } = 1.0f;
        public float SystemVolume { get; set; } = 1.0f;

        public static AppConfiguration Load(IAppLogger logger)
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    return JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
                }
            }
            catch (Exception ex)
            {
                logger.LogError("AppConfiguration", "Failed to load configuration; falling back to defaults.", ex);
            }

            return new AppConfiguration();
        }

        public void Save(IAppLogger logger)
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigFilePath);
                if (directory is not null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                logger.LogError("AppConfiguration", "Failed to save configuration.", ex);
            }
        }
    }
}
