using BtAudioMixer.UI;
using System.Windows;

namespace BtAudioMixer
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // AudioPlaybackConnection.OpenAsync() is a WinRT API gated behind Package
            // Identity. Without it, every ConnectAsync() call returns DeniedBySystem
            // (HRESULT 0x8007139F). Reading Package.Current throws a COMException when
            // the process is unpackaged — we catch that to give a clear setup message
            // instead of a cryptic error later during Bluetooth connection.
            if (!HasPackageIdentity())
            {
                // Need a message pump for MessageBox; create it minimally.
                var app = new System.Windows.Application();
                MessageBox.Show(
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

