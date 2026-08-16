using System.Runtime.InteropServices;

namespace BtAudioMixer.Core.Platform
{
    /// <summary>
    /// Registers a thread with Windows' Multimedia Class Scheduler Service (MMCSS)
    /// for glitch-resistant real-time audio scheduling. Ported from
    /// WindowsDualAudioManager's Core.Platform.MmcssThreadBooster.
    /// </summary>
    public sealed class MmcssThreadBooster : IDisposable
    {
        private const string ProAudioTaskName = "Pro Audio";

        [DllImport("avrt.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, ref uint taskIndex);

        [DllImport("avrt.dll", SetLastError = true)]
        private static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

        [DllImport("avrt.dll", SetLastError = true)]
        private static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, AvThreadPriority priority);

        private enum AvThreadPriority
        {
            Critical = 4
        }

        private IntPtr _avrtHandle = IntPtr.Zero;
        private readonly Diagnostics.IAppLogger _logger;

        public MmcssThreadBooster(Diagnostics.IAppLogger logger)
        {
            _logger = logger;
        }

        public bool TryBoostCurrentThread()
        {
            uint taskIndex = 0;
            _avrtHandle = AvSetMmThreadCharacteristicsW(ProAudioTaskName, ref taskIndex);

            if (_avrtHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogWarning("MmcssThreadBooster", $"AvSetMmThreadCharacteristicsW failed (Win32 error {error}); continuing without MMCSS scheduling.");
                return false;
            }

            if (!AvSetMmThreadPriority(_avrtHandle, AvThreadPriority.Critical))
            {
                int error = Marshal.GetLastWin32Error();
                _logger.LogWarning("MmcssThreadBooster", $"AvSetMmThreadPriority failed (Win32 error {error}); thread is MMCSS-registered but not at critical priority.");
            }

            return true;
        }

        public void Dispose()
        {
            if (_avrtHandle != IntPtr.Zero)
            {
                AvRevertMmThreadCharacteristics(_avrtHandle);
                _avrtHandle = IntPtr.Zero;
            }
        }
    }
}
