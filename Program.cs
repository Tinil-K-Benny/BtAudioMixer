using BtAudioMixer.UI;
using System.Threading;
using System.Windows;

namespace BtAudioMixer
{
    internal static class Program
    {
        // Two running instances independently open WASAPI capture/render sessions
        // against the same devices; whichever grabbed them first is what you actually
        // hear, while the other's sliders silently affect nothing you can hear. A
        // named Mutex is the standard .NET single-instance guard — same job as
        // AudioPlaybackConnector2's SingleInstanceGuard, just via the BCL primitive
        // instead of a hand-rolled one.
        private const string SingleInstanceMutexName = "BtAudioMixer.SingleInstance.9f3b6e2a";

        [STAThread]
        private static void Main()
        {
            using var singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                var app = new System.Windows.Application();
                System.Windows.MessageBox.Show(
                    "Bluetooth Audio Mixer is already running. Check your system tray.",
                    "Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // AudioPlaybackConnection.OpenAsync() is a WinRT API gated behind Package
            // Identity. Without it, every ConnectAsync() call returns DeniedBySystem
            // (HRESULT 0x8007139F). Reading Package.Current throws a COMException when
            // the process is unpackaged — we catch that to give a clear setup message
            // instead of a cryptic error later during Bluetooth connection.
            if (!HasPackageIdentity())
            {
                // Need a message pump for MessageBox; create it minimally.
                var app = new System.Windows.Application();
                System.Windows.MessageBox.Show(
                    "BtAudioMixer needs a Package Identity to open Bluetooth audio connections.\n\n" +
                    "Run the one-time setup script (no admin required):\n\n" +
                    "  1. Build the project (Ctrl+Shift+B in Visual Studio).\n" +
                    "  2. Open PowerShell in the project folder and run:\n\n" +
                    "       .\\SparsePackage\\Register-SparsePackage.ps1\n\n" +
                    "  3. Relaunch BtAudioMixer.exe.\n\n" +
                    "This only needs to be done once per machine (or after a clean rebuild).",
                    "Setup Required — Package Identity Missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var mainApp = new App();
            mainApp.InitializeComponent();
            mainApp.Run();
        }

        /// <summary>
        /// Returns true if this process has a Windows Package Identity (i.e. it was
        /// launched as a registered sparse-package or MSIX app). Returns false when
        /// running as a plain, unpackaged .exe.
        /// </summary>
        private static bool HasPackageIdentity()
        {
            try
            {
                // This property throws COMException / InvalidOperationException when
                // called from an unpackaged process.
                var _ = Windows.ApplicationModel.Package.Current;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

