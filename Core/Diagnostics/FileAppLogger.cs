using System.Diagnostics;
using System.IO;

namespace BtAudioMixer.Core.Diagnostics
{
    public sealed class FileAppLogger
    {
        private const string LogDirectoryName = "BtAudioMixer";
        private const string LogFileName = "error_log.txt";

        private readonly string _logFilePath;
        private readonly object _writeLock = new();

        public FileAppLogger()
        {
            var appDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDirectory = Path.Combine(appDataDirectory, LogDirectoryName);
            Directory.CreateDirectory(logDirectory);
            _logFilePath = Path.Combine(logDirectory, LogFileName);
        }

        public void LogInformation(string category, string message)
        {
            WriteLine("INFO", category, message, exception: null);
        }

        public void LogWarning(string category, string message)
        {
            WriteLine("WARN", category, message, exception: null);
        }

        public void LogError(string category, string message, Exception? exception = null)
        {
            WriteLine("ERROR", category, message, exception);
        }

        private void WriteLine(string level, string category, string message, Exception? exception)
        {
            var line = exception is null
                ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{category}] {message}"
                : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{category}] {message} :: {exception}";

            lock (_writeLock)
            {
                try
                {
                    File.AppendAllText(_logFilePath, line + Environment.NewLine);
                }
                catch
                {
                }
            }

            Debug.WriteLine(line);
        }
    }
}
