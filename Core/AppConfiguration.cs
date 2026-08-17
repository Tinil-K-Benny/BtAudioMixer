using BtAudioMixer.Core.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BtAudioMixer.Core
{
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
        public bool AutoSwitchDefaultDevice { get; set; }

        public static AppConfiguration Load(FileAppLogger logger)
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

        public void Save(FileAppLogger logger)
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
