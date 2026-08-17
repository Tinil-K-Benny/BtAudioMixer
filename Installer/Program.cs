using System.Diagnostics;

namespace BtAudioMixer.Installer
{
    internal static class Program
    {
        private const string AppFolderName = "BtAudioMixer";

        [STAThread]
        private static void Main(string[] args)
        {
            string installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", AppFolderName);

            bool uninstall = args.Contains("-uninstall", StringComparer.OrdinalIgnoreCase);

            try
            {
                if (uninstall)
                {
                    RunUninstall(installDir);
                    System.Windows.Forms.MessageBox.Show(
                        "Bluetooth Audio Mixer has been uninstalled.",
                        "Uninstall Complete",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Information);
                }
                else
                {
                    RunInstall(installDir);
                    System.Windows.Forms.MessageBox.Show(
                        "Bluetooth Audio Mixer is installed.\n\nLaunch it from the Start Menu (search \"Bluetooth Audio Mixer\") — not from this folder.",
                        "Install Complete",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Setup failed:\n\n{ex.Message}",
                    "Setup Error",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private static void RunInstall(string installDir)
        {
            string payloadDir = AppContext.BaseDirectory;
            string appPayloadDir = Path.Combine(payloadDir, "App");

            if (!Directory.Exists(appPayloadDir))
            {
                throw new InvalidOperationException($"Setup payload not found at '{appPayloadDir}'. This installer must be run from its extracted folder, alongside the 'App' and 'Tools' subfolders.");
            }

            Directory.CreateDirectory(installDir);
            CopyDirectory(appPayloadDir, installDir);

            string scriptPath = Path.Combine(installDir, "SparsePackage", "Register-SparsePackage.ps1");
            string makeAppx = Path.Combine(payloadDir, "Tools", "makeappx.exe");
            string signTool = Path.Combine(payloadDir, "Tools", "signtool.exe");

            RunPowerShellScript(scriptPath,
                $"-ExeDir \"{installDir}\" -MakeAppxPath \"{makeAppx}\" -SignToolPath \"{signTool}\"");
        }

        private static void RunUninstall(string installDir)
        {
            string scriptPath = Path.Combine(installDir, "SparsePackage", "Unregister-SparsePackage.ps1");
            if (File.Exists(scriptPath))
            {
                RunPowerShellScript(scriptPath, string.Empty);
            }

            if (Directory.Exists(installDir))
            {
                Directory.Delete(installDir, recursive: true);
            }
        }

        private static void RunPowerShellScript(string scriptPath, string arguments)
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start PowerShell.");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
            }

            foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                File.Copy(filePath, filePath.Replace(sourceDir, destinationDir), overwrite: true);
            }
        }
    }
}
