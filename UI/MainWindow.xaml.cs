using BtAudioMixer.Core;
using BtAudioMixer.Core.Bluetooth;
using BtAudioMixer.Core.Devices;
using BtAudioMixer.Core.Diagnostics;
using BtAudioMixer.Core.Mixing;
using BtAudioMixer.Core.Platform;
using System.ComponentModel;
using System.Windows;
using Windows.Media.Audio;

namespace BtAudioMixer.UI
{
    public partial class MainWindow : Window
    {
        private sealed record BtDeviceItem(string Id, string Name)
        {
            public override string ToString() => Name;
        }

        private readonly IAppLogger _logger = new FileAppLogger();
        private readonly AudioDeviceRepository _deviceRepository;
        private readonly MmcssThreadBooster _threadBooster;
        private readonly LatencyTelemetry _telemetry;
        private readonly AudioPlaybackConnectionManager _btManager;
        private readonly MixerEngine _mixer;
        private readonly AppConfiguration _config;
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
        private readonly System.Windows.Forms.ToolStripMenuItem _trayToggleMixItem;
        private bool _isExiting;

        public MainWindow()
        {
            InitializeComponent();

            _config = AppConfiguration.Load(_logger);
            _deviceRepository = new AudioDeviceRepository(_logger);
            _threadBooster = new MmcssThreadBooster(_logger);
            _telemetry = new LatencyTelemetry(_logger);
            _btManager = new AudioPlaybackConnectionManager(_logger);
            _btManager.StateChanged += (_, state) => Dispatcher.Invoke(() => OnPhoneStateChanged(state));
            _mixer = new MixerEngine(_telemetry, _threadBooster, _logger);

            PhoneVolumeSlider.Value = _config.PhoneVolume;
            SystemVolumeSlider.Value = _config.SystemVolume;

            (_notifyIcon, _trayToggleMixItem) = CreateTrayIcon();

            Loaded += async (_, _) => await RefreshAllDevicesAsync();
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
        }

        private (System.Windows.Forms.NotifyIcon, System.Windows.Forms.ToolStripMenuItem) CreateTrayIcon()
        {
            var notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "Bluetooth Audio Mixer",
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true
            };

            notifyIcon.DoubleClick += (_, _) => ShowWindow();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (_, _) => ShowWindow());
            contextMenu.Items.Add("-");
            var toggleMixItem = new System.Windows.Forms.ToolStripMenuItem("Start Mixing");
            toggleMixItem.Click += (_, _) => StartStopButton_Click(this, new RoutedEventArgs());
            contextMenu.Items.Add(toggleMixItem);
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, (_, _) =>
            {
                _isExiting = true;
                Close();
            });
            notifyIcon.ContextMenuStrip = contextMenu;

            return (notifyIcon, toggleMixItem);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_isExiting)
            {
                OnClosing();
                return;
            }

            // Minimize to tray instead of closing, matching AudioPlaybackConnector2 and
            // WindowsDualAudioManager's tray-first UX — only the tray menu's Exit truly quits.
            e.Cancel = true;
            Hide();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private async void RefreshDevicesButton_Click(object sender, RoutedEventArgs e) => await RefreshAllDevicesAsync();

        private async System.Threading.Tasks.Task RefreshAllDevicesAsync()
        {
            var renderDevices = _deviceRepository.GetRenderDevices();

            PhoneSourceCombo.ItemsSource = renderDevices;
            SystemSourceCombo.ItemsSource = renderDevices;
            OutputCombo.ItemsSource = renderDevices;

            SelectById(PhoneSourceCombo, renderDevices, _config.PhoneSourceDeviceId);
            SelectById(SystemSourceCombo, renderDevices, _config.SystemSourceDeviceId);
            SelectById(OutputCombo, renderDevices, _config.OutputDeviceId);

            ReportVirtualCableStatus(renderDevices);

            try
            {
                var btDevices = await AudioPlaybackConnectionManager.ListCandidateDevicesAsync();
                var items = btDevices.Select(d => new BtDeviceItem(d.Id, d.Name)).ToList();
                PhoneDeviceCombo.ItemsSource = items;
                var match = items.FirstOrDefault(d => d.Id == _config.PhoneBluetoothDeviceId);
                if (match is not null)
                {
                    PhoneDeviceCombo.SelectedItem = match;
                }
            }
            catch (Exception ex)
            {
                Log($"Could not enumerate Bluetooth devices: {ex.Message}");
            }
        }

        private void ReportVirtualCableStatus(List<AudioDevice> renderDevices)
        {
            var cables = VirtualCableDetector.FindVirtualCables(renderDevices);

            if (cables.Count == 0)
            {
                Log("No virtual audio cable detected. You'll need one so the phone's audio has a silent device to land on " +
                    "instead of playing out loud — get VB-CABLE free from vb-audio.com/Cable, or Virtual Audio Cable from ntonyx.com.");
            }
            else if (cables.Count == 1)
            {
                Log($"One virtual cable detected ({cables[0].Name}) — enough for the phone source. For system audio too " +
                    "(so you don't hear it twice, once from real speakers and once in the mix), add a second cable: " +
                    "VB-CABLE's free A+B pack, or a second line via your existing cable's control panel if it supports one.");
            }
            else
            {
                Log($"{cables.Count} virtual cables detected ({string.Join(", ", cables.Select(c => c.Name))}) — enough for both sources.");
            }
        }

        private static void SelectById(System.Windows.Controls.ComboBox combo, List<AudioDevice> devices, string? id)
        {
            var match = devices.FirstOrDefault(d => d.Id == id) ?? devices.FirstOrDefault(d => d.IsDefault);
            if (match is not null)
            {
                combo.SelectedItem = match;
            }
        }

        private async void ConnectPhoneButton_Click(object sender, RoutedEventArgs e)
        {
            if (PhoneDeviceCombo.SelectedItem is not BtDeviceItem device)
            {
                Log("Select a phone to connect first.");
                return;
            }

            try
            {
                await _btManager.ConnectAsync(device.Id);
                _config.PhoneBluetoothDeviceId = device.Id;
                Log($"Connected to '{device.Name}'. Now assign this app's output device to your phone-source device once, in Windows Settings > System > Sound > Volume mixer.");
            }
            catch (Exception ex)
            {
                Log($"Connect failed: {ex.Message}");
            }
        }

        private void OnPhoneStateChanged(AudioPlaybackConnectionState state)
        {
            Log($"Phone connection state: {state}");
            bool connected = state == AudioPlaybackConnectionState.Opened;
            PhoneStatusDot.Fill = connected ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Gray;
            PhoneStatusText.Text = state.ToString();
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mixer.IsRunning)
            {
                _mixer.Stop();
                StartStopButton.Content = "Start Mixing";
                _trayToggleMixItem.Text = "Start Mixing";
                MixerStatusDot.Fill = System.Windows.Media.Brushes.Gray;
                MixerStatusText.Text = "Stopped";
                Log("Mixer stopped.");
                return;
            }

            if (PhoneSourceCombo.SelectedItem is not AudioDevice phoneSource ||
                SystemSourceCombo.SelectedItem is not AudioDevice systemSource ||
                OutputCombo.SelectedItem is not AudioDevice output)
            {
                Log("Select a phone-source device, system-source device, and output device first.");
                return;
            }

            try
            {
                var phoneMmDevice = _deviceRepository.GetDevice(phoneSource.Id);
                var systemMmDevice = _deviceRepository.GetDevice(systemSource.Id);
                var outputMmDevice = _deviceRepository.GetDevice(output.Id);

                _mixer.Start(phoneMmDevice, systemMmDevice, outputMmDevice,
                    (float)PhoneVolumeSlider.Value, (float)SystemVolumeSlider.Value);

                _config.PhoneSourceDeviceId = phoneSource.Id;
                _config.SystemSourceDeviceId = systemSource.Id;
                _config.OutputDeviceId = output.Id;

                StartStopButton.Content = "Stop Mixing";
                _trayToggleMixItem.Text = "Stop Mixing";
                MixerStatusDot.Fill = System.Windows.Media.Brushes.LimeGreen;
                MixerStatusText.Text = "Running";
                Log($"Mixing '{phoneSource.Name}' + '{systemSource.Name}' -> '{output.Name}'.");
            }
            catch (Exception ex)
            {
                Log($"Failed to start mixer: {ex.Message}");
            }
        }

        private void PhoneVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _mixer.PhoneVolume = (float)e.NewValue;
            _config.PhoneVolume = (float)e.NewValue;
            if (PhoneVolumeLabel is not null)
            {
                PhoneVolumeLabel.Text = $"{e.NewValue:P0}";
            }
        }

        private void SystemVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _mixer.SystemVolume = (float)e.NewValue;
            _config.SystemVolume = (float)e.NewValue;
            if (SystemVolumeLabel is not null)
            {
                SystemVolumeLabel.Text = $"{e.NewValue:P0}";
            }
        }

        private void Log(string message) => StatusText.AppendText(message + Environment.NewLine);

        private void OnClosing()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _config.Save(_logger);
            _mixer.Dispose();
            _btManager.Dispose();
            _threadBooster.Dispose();
            _deviceRepository.Dispose();
        }
    }
}
