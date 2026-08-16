namespace BtAudioMixer.Core.Diagnostics
{
    public interface IAppLogger
    {
        void LogInformation(string category, string message);

        void LogWarning(string category, string message);

        void LogError(string category, string message, Exception? exception = null);
    }
}
