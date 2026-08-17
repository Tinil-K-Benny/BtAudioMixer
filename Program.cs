using BtAudioMixer.UI;
using System.Threading;
using System.Windows;

namespace BtAudioMixer
{
    internal static class Program
    {
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

            if (!HasPackageIdentity())
            {
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

        private static bool HasPackageIdentity()
        {
            try
            {
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

