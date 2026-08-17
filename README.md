# Bluetooth Audio Mixer

Mix your **phone's Bluetooth audio** and your **PC's system audio** into a single stream, each with its own independent volume, and send the blend to your real Bluetooth headphones — so nothing on your phone ever mutes what's playing on your PC, and nothing on your PC ever mutes your phone.

## Table of contents

- [The problem](#the-problem)
- [The idea](#the-idea)
- [Features](#features)
- [Requirements](#requirements)
- [How it works under the hood](#how-it-works-under-the-hood)
- [Volume, codec, latency, and buffering](#volume-codec-latency-and-buffering)
- [Performance](#performance)
- [Compatibility](#compatibility)
- [Setup](#setup)
- [Using the app](#using-the-app)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Building the installer](#building-the-installer)
- [Project structure](#project-structure)
- [Configuration and logs](#configuration-and-logs)
- [Contributing](#contributing)
- [Credits](#credits)
- [License](#license)

## The problem

Bluetooth's audio profile (A2DP) only carries **one active stream at a time**. Multipoint earbuds paired to both your phone and your PC will happily switch between them — but the moment the second source starts playing, the first one gets muted. Whatever you're doing on the PC — gaming, watching a video, on a call — a phone notification, podcast, or incoming call cuts it off, and vice versa. Anyone routinely using the same earbuds for both devices at once runs into this; gaming while waiting on a phone notification is just one common case of it.

## The idea

Bypass multipoint entirely. Connect the earbuds to the **PC only**. Bring the phone's audio **into** the PC over Bluetooth using Windows' built-in A2DP-sink support, mix it with system audio in real time — independent gain per source, soft-clipped so a loud mix never distorts — and send one combined stream out to the earbuds. From the earbuds' point of view there's only ever one source, so nothing can mute anything.

```
 PHONE ──(Bluetooth A2DP)──► AudioPlaybackConnection ──► Virtual Cable ──► capture ──┐
                                                                                      │
 PC / system audio ─────────────────────────────────────────────────────► capture ──┤
                                                                                      ▼
                                                                              ┌───────────────┐
                                                                              │     MIXER     │
                                                                              │ per-source gain│
                                                                              │  sum + soft-clip│
                                                                              └───────┬───────┘
                                                                                      ▼
                                                                          Real Bluetooth Earbuds
```

## Features

- **Independent volume** for phone audio and system audio, adjusted live while mixing, shown as a percentage next to each slider and persisted between runs.
- **Real-time mixing** via WASAPI loopback capture on two arbitrary render devices simultaneously (not just "the" system default — each channel captures a specific device you pick), resampled to a common 48kHz/stereo float format, summed with NAudio's `MixingSampleProvider`, and soft-clipped (`tanh` saturation) so two loud sources together saturate gracefully instead of hard-clipping into harsh distortion.
- **Bluetooth phone connection built in** — no separate helper app required. Talks to `Windows.Media.Audio.AudioPlaybackConnection` directly to accept the phone's A2DP stream, with Connect/Disconnect buttons and a three-state status indicator (see [Using the app](#using-the-app)). Connecting only prepares the link — audio doesn't start flowing until you click Start Mixing, so nothing plays before you're ready for it.
- **System tray** — closing the window minimizes to tray instead of quitting; double-click the tray icon (or minimizing the window) to hide/restore; the tray context menu offers Show, Start/Stop Mixing, and Exit. Only Exit actually terminates the process.
- **Single-instance guard** — a named `Mutex` prevents a second copy from launching. Two instances would each independently grab the same WASAPI devices, and whichever won the race is what you'd actually hear — the other instance's sliders would silently control nothing you can hear. Launching a second copy just shows a message box and exits.
- **Virtual-cable detection** — recognizes common virtual audio cable products (VB-CABLE, Virtual Audio Cable, Voicemeeter) by their endpoint name and warns you on startup if none is installed, since the phone's raw audio needs somewhere silent to land.
- **Automatic default-device switching (opt-in)** — the "Set 'Phone lands on' as the Windows default output while mixing" checkbox switches the Windows-wide default playback device to your virtual cable the moment you click **Start Mixing**, and restores your previous default the moment you click **Stop Mixing** (or disconnect, or close the app), so you don't have to do it by hand every session (see [Setup](#setup) step 7). It's scoped to mixing rather than the whole app session on purpose — leaving the system default hijacked while not mixing would silence every other app on your PC that just uses "the default device," not only phone audio. Requires one-time confirmation the first time you enable it, since it's a system-wide change; uses the same undocumented `IPolicyConfig` COM interface most third-party audio-switcher utilities rely on, since Windows has no public API for setting the default device.
- **Config persistence** — remembers your device selections (phone Bluetooth device, phone-lands-on device, system source, output device) and both volume levels between runs, stored as JSON under `%AppData%\BtAudioMixer\config.json`.
- **MMCSS thread boosting** — the audio capture/render callback threads register with Windows' Multimedia Class Scheduler Service ("Pro Audio" task, critical priority) for glitch-resistant real-time scheduling, the same mechanism Windows' own audio engine uses.
- **Buffering telemetry** — an internal ring buffer per capture channel tracks underruns and overruns and logs them, so a crackling mix leaves a diagnosable trail instead of a silent mystery.
- **Structured file logging** — every run appends timestamped `INFO`/`WARN`/`ERROR` lines to a persistent log file, independent of the in-app Activity Log (which only shows the current session).

## Requirements

- Windows 10 version 2004+ or Windows 11 (needed for `AudioPlaybackConnection`, the API that lets Windows act as a Bluetooth A2DP sink).
- A Bluetooth adapter that supports the A2DP **Sink** role. Not all adapters do — some only support Source (sending audio out). Built-in Intel adapters are generally reliable; some cheaper USB/MediaTek combo adapters are not. If your phone's "Media audio" toggle for the PC won't stay enabled, this is the most likely cause and isn't fixable in software.
- A virtual audio cable (e.g. [VB-CABLE](https://vb-audio.com/Cable/), free) so the phone's raw audio has a silent device to land on instead of playing out loud twice.
- .NET 8 SDK, if building from source.
- Windows 10 SDK (for `MakeAppx.exe` / `SignTool.exe`), only needed once to register the sparse package — see [Setup](#setup).

## How it works under the hood

`AudioPlaybackConnection.OpenAsync()` — the WinRT API that accepts the phone's incoming stream — is gated behind **Package Identity** and returns `DeniedBySystem` (HRESULT `0x8007139F`) when called from a plain unpackaged `.exe`. This app registers itself as a **sparse package with an external location**, which grants Package Identity without requiring a full MSIX install or locking the app into the package store. `Program.cs` checks for Package Identity at startup (`Windows.ApplicationModel.Package.Current`) and shows a setup prompt instead of a cryptic Bluetooth error if it's missing. See [Setup](#setup) below.

Once mixing is running, `MixerEngine` opens two independent `WasapiLoopbackCapture` sessions (one per source device), each feeding a lock-free single-producer/single-consumer ring buffer (`SpscRingBuffer`) that decouples the audio driver's callback thread from the mixer's read thread. Each channel is resampled to a common format, gain-scaled (`VolumeSampleProvider`), summed via `MixingSampleProvider`, soft-clipped, and rendered to the output device with `WasapiOut` in shared mode (event-driven by default, falling back to timer-driven if event-sync `Init` fails on a given device).

Exclusive WASAPI mode was tried for the output device to stop Windows' own native Bluetooth A2DP render session from also competing for the same earbuds — it locked the device successfully but produced no audible output on the test hardware (the driver accepted the format handshake without actually rendering), a known WASAPI exclusive-mode risk. Shared mode is what reliably renders; the "raw audio leaking around the mixer" problem is instead solved by making sure the **system-wide default output device** isn't the earbuds (see step 7 in [Setup](#setup)).

## Volume, codec, latency, and buffering

- **Phone-side volume is not bypassed.** A2DP transmits audio already scaled by the *source* device's own volume level — the phone applies its own volume before encoding and sending the stream, the same as it would to a pair of wired headphones. The app's **Phone volume** slider is an *additional* gain stage on top of whatever the phone already sent, not a substitute for it. Turning the phone's own volume all the way down means no amount of app-slider gain brings it back (0 × anything is still 0); turning both up together is what the soft-clipper exists to protect against.
- **Codec negotiation (SBC / AAC / etc.) is entirely out of the app's hands.** A2DP codec selection happens between the phone and Windows' own Bluetooth stack/driver during pairing and connection, before any audio reaches this app. `Windows.Media.Audio.AudioPlaybackConnection` hands this app already-decoded PCM — there's no API surface to query or choose the codec from here, so the app can't tell you (or control) whether a given session negotiated SBC, AAC, or something else.
- **Latency:** the mixer's own target buffer is **40ms** (`MixerEngine`'s `targetLatencyMs` default), used both as the requested `WasapiOut` render latency and as the floor for each capture channel's ring buffer sizing. The actual capture ring buffer is sized to `max(devicePeriod + 20ms, devicePeriod × 3)` in `CaptureChannel` — effectively **~120ms** of headroom at default settings, larger if a given device's own default WASAPI period exceeds 40ms. This is the app's *own* processing budget, not an end-to-end measurement — total perceived latency (phone tap → your ears) also includes the Bluetooth radio/codec latency on the phone-to-PC hop, which A2DP is inherently not low-latency about (commonly 100–300ms depending on codec and hardware) and which this app has no control over. No end-to-end latency has been measured for this project; if you benchmark it, a PR to fill this in would be welcome.

## Performance

CPU and memory footprint haven't been formally benchmarked yet. Architecturally: both capture threads and the render thread register with MMCSS ("Pro Audio" task) for real-time scheduling and use event-driven WASAPI callbacks rather than polling, which is the same approach Windows' own audio engine uses to stay efficient — but that's a design intent, not a measured number. If you've run it and checked Task Manager, numbers (and the hardware you saw them on) are welcome as a PR to this section.

## Compatibility

- **Tested hardware:** _not yet documented here — see [Contributing](#contributing)._
- **iPhone / iOS as the source device:** not verified by the maintainer. A2DP is a standard, platform-agnostic Bluetooth profile and iOS does support pairing with a PC as an audio destination in principle, so this should work the same way an Android phone does — but the exact pairing UX and reliability on iOS hasn't been tested against this app, and iOS doesn't have the same explicit "Media audio" per-device toggle that Android's Bluetooth settings expose (see [Troubleshooting](#troubleshooting)), so the failure mode if something's wrong may look different. Reports from anyone who's tried it are welcome.

## Setup

### Option A — Installer (no build tools required)

1. Download the latest `BtAudioMixer-Setup.zip` from [Releases](../../releases), and extract it anywhere.
2. Run `BtAudioMixer.Installer.exe` from the extracted folder. Windows SmartScreen will likely warn "Windows protected your PC" since the installer is self-signed rather than commercially signed — click **More info → Run anyway**. It also prompts for admin (UAC) — that's expected, needed to trust the certificate and register the app; nothing else on your system is touched.
3. Once it reports "Install Complete," continue at [step 5 below](#first-time-configuration) — don't launch it from the extracted folder, only from the Start Menu from now on.

### Option B — Build from source

1. **Install a virtual audio cable** (e.g. [VB-CABLE](https://vb-audio.com/Cable/)) if you don't already have one.
2. **Build the project** (see [Building from source](#building-from-source)). By default this produces a Debug build.
3. **Register the sparse package**, run from the repo root (a UAC prompt appears once, to install and trust a self-signed certificate — this is expected and required):
   ```powershell
   .\SparsePackage\Register-SparsePackage.ps1
   ```
   By default the script looks for `BtAudioMixer.exe` in `bin\Debug\net8.0-windows10.0.19041.0`. If you built Release instead, pass the output folder explicitly:
   ```powershell
   .\SparsePackage\Register-SparsePackage.ps1 -ExeDir "bin\Release\net8.0-windows10.0.19041.0\win-x64"
   ```
   This only needs to be run once per machine (or again after a version bump in `SparsePackage\AppxManifest.xml`) — not on every rebuild.
4. **Launch the app via Start Menu search** ("Bluetooth Audio Mixer") — not by double-clicking the `.exe` directly. Only the packaged launch path carries Package Identity; running the raw `.exe` will show a "Setup Required" prompt instead of trying (and failing) to connect.

### First-time configuration

5. **Pair your phone** with the PC as a normal Bluetooth device first, via Windows Settings.
6. In the app: pick your phone from the **Phone (Bluetooth)** dropdown and click **Connect**.
7. **Set the Windows default playback device to your virtual cable** (Settings → System → Sound → Output), or check **"Set 'Phone lands on' as the Windows default output while mixing"** in the app to have it do this automatically for you whenever you click Start Mixing (see step 8). This matters more than it sounds — the actual Bluetooth A2DP audio decode is handled by a Windows system service that follows the *system-wide default device*, not any per-app routing you set for this app specifically. If the default stays on your real speakers/earbuds, the phone's raw audio will leak straight through, bypassing the mixer and its volume sliders entirely.
8. Back in the app, set:
   - **Phone lands on** → your virtual cable
   - **System audio device** → whatever your PC normally plays through
   - **Earbuds (output)** → your real Bluetooth headphones
9. Click **Start Mixing**. Adjust the two volume sliders live.

## Using the app

- **Activity Log** at the bottom of the window shows this session's events (connection state changes, mixer start/stop, errors) — it doesn't persist across restarts. For a persistent history, see [Configuration and logs](#configuration-and-logs).
- **Phone status dot** reflects app-level state, not just the raw Bluetooth connection state, since "connected" and "audible" aren't the same thing here:
  | Dot | Text | Meaning |
  |---|---|---|
  | Red | Not connected | No `AudioPlaybackConnection` prepared |
  | Orange | Connected, not mixing | Phone is paired/prepared but audio isn't flowing yet |
  | Green | Connected, mixing | Audio is actually flowing through the mix |
- **Connect / Disconnect** — Connect prepares the Bluetooth link but doesn't start streaming audio until you click Start Mixing (so nothing is audible in between). Disconnect closes the link immediately and stops mixing too if it was running, since a phone capture channel with no phone behind it isn't doing anything useful.
- **Audio source dropdowns lock while mixing** — Phone lands on / System audio device / Output, plus Refresh, are disabled while the mixer is running, to stop a mid-session device swap from pointing the pipeline at a device it never opened. Stop mixing to change them.
- **Stopping the mixer only cuts the mixed output** — if the phone is connected, "Stop Mixing" tears down the phone/system capture and render pipeline, not the Bluetooth link itself; your PC's normal audio keeps playing normally (it was never routed through the mixer to begin with — loopback capture is a non-destructive tap, not a hijack). If you've enabled the auto default-device switch, it also restores your original Windows default output the instant you stop, so nothing else on your PC that relies on "the default device" goes silent.
- **Closing the window** minimizes to the system tray rather than quitting — the mixer keeps running in the background. Use the tray icon's context menu (or double-click it to reopen the window) to stop mixing or fully exit.
- **Refresh** re-enumerates render devices, useful after plugging in a new virtual cable or re-pairing Bluetooth hardware without restarting the app.

## Troubleshooting

**`DeniedBySystem` / `0x8007139F` when connecting.** Almost always Package Identity — make sure you registered the sparse package and launched via Start Menu, not the raw `.exe`. If that's already right, confirm your phone's Bluetooth entry for this PC has **Media audio** enabled (Android often splits Bluetooth into separate "Phone calls" and "Media audio" toggles per device — only the calls one being on produces this exact error). See `Docs/troubleshooting_denied_by_system.md` in the maintainer's local working copy for the full debugging log, including a WinRT API lifecycle bug (`StartAsync()` must be called *before* `OpenAsync()`, not after) that also produces this same error.

**Phone's "Media audio" toggle won't stay on / reverts after a few seconds.** Usually means your Bluetooth adapter doesn't support the A2DP Sink role at the driver level — a hardware/driver limitation, not something fixable in this app. Try updating the adapter's driver first; if that doesn't help, the adapter likely can't do sink mode at all.

**Volume slider doesn't seem to change anything.** Check the Mixer section's status dot actually says "Running" — the sliders are no-ops while stopped. If it is running and still silent, make sure you don't have two copies of the app running (the single-instance guard should prevent this, but confirm via Task Manager) — with two instances, one holds the real output device and the other's sliders control a session you can't hear.

**Volume slider has no effect even though the mixer is running and it's the only instance.** Check whether the raw phone audio is bypassing the mixer entirely — see step 7 in [Setup](#setup). Windows' native Bluetooth audio handling doesn't respect per-app output routing the way most apps do; only changing the *system-wide* default output device reliably redirects it.

**Registering the sparse package fails to find `makeappx.exe` / `signtool.exe`.** Install the Windows 10 SDK component via the Visual Studio Installer (Individual Components → "Windows 10 SDK"). The script searches `C:\Program Files (x86)\Windows Kits\10\bin` for the newest versioned subfolder.

**App is already running / won't launch a second time.** That's the single-instance guard working as intended — check the system tray, the app is likely already there.

## Building from source

This repository's root **is** the `BtAudioMixer` project folder — cloning it gives you `Core/`, `UI/`, `Program.cs`, `SparsePackage/`, and the `.csproj` directly, with no top-level solution file.

```powershell
git clone <this-repo> BtAudioMixer
cd BtAudioMixer
dotnet build BtAudioMixer.csproj -c Release
```

Publish a self-contained single-file build:
```powershell
dotnet publish BtAudioMixer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
Note: a self-contained build run directly from `publish\` won't have Package Identity (see [How it works](#how-it-works-under-the-hood)) — it's useful for testing the mixer/UI, but Bluetooth connection requires the sparse-package-registered build launched via Start Menu.

## Building the installer

`Installer/` is a separate small project (`BtAudioMixer.Installer.csproj`) that end users run instead of the manual sparse-package registration steps — it copies the app to `%LocalAppData%\Programs\BtAudioMixer` and runs the same `Register-SparsePackage.ps1` logic against that location, using a bundled copy of `makeappx.exe`/`signtool.exe` (via the redistributable `Microsoft.Windows.SDK.BuildTools` NuGet package) so end users don't need the Windows SDK installed.

To build the full distributable:
```powershell
.\Build-Distribution.ps1
```
This publishes both `BtAudioMixer.exe` and `BtAudioMixer.Installer.exe` as self-contained single-file builds into `dist\` (gitignored):
```
dist/
├── BtAudioMixer.Installer.exe   ← what end users run
├── App/                          the app payload the installer copies into place
│   ├── BtAudioMixer.exe
│   └── SparsePackage/
└── Tools/                        bundled makeappx.exe / signtool.exe
```
Zip the *contents* of `dist\` (not the folder itself) and attach it to a [GitHub Release](../../releases) — that's `BtAudioMixer-Setup.zip` referenced in [Setup](#setup) above.

> **Note on the maintainer's working copy:** locally, this project folder sits alongside a `BtAudioMixer.sln` (referencing this project plus an xUnit test project, `BtAudioMixer.Core.Tests`), a `Docs/` folder with planning and debugging notes, and a folder of reference projects credited below. None of that lives inside this git repository — it's one level up in the maintainer's checkout, used for whole-solution builds and testing but not published here.

## Project structure

```
BtAudioMixer/                (repository root)
├── Core/
│   ├── Bluetooth/       AudioPlaybackConnectionManager — wraps Windows.Media.Audio.AudioPlaybackConnection
│   ├── Capture/         CaptureChannel — loopback-captures one arbitrary render device; RingBufferWaveProvider
│   ├── Mixing/          MixerEngine — mixes two capture channels with independent gain + soft clip; SoftClipSampleProvider
│   ├── Buffering/       SpscRingBuffer — lock-free buffer between a capture callback and the render pull
│   ├── Devices/         AudioDeviceRepository (enumeration), AudioDevice, VirtualCableDetector
│   ├── Diagnostics/     FileAppLogger, LatencyTelemetry (underrun/overrun tracking)
│   ├── Output/          SampleFormatConverter — resampling/channel matching to the mixer's common format
│   ├── Platform/        MmcssThreadBooster (MMCSS thread priority), DefaultAudioDeviceSwitcher (IPolicyConfig COM interop)
│   └── AppConfiguration.cs   Persisted settings (JSON under %AppData%\BtAudioMixer)
├── UI/                  MainWindow (WPF) + tray icon (WinForms NotifyIcon), App.xaml
├── SparsePackage/       Package-identity registration (AppxManifest.xml, Assets, Register/Unregister PowerShell scripts)
├── Installer/           BtAudioMixer.Installer.csproj — end-user setup exe (see Building the installer)
├── Program.cs           Entry point — single-instance guard + Package Identity check
├── BtAudioMixer.csproj
├── Build-Distribution.ps1   Builds the dist\ folder that ships in GitHub Releases
├── LICENSE
└── .gitignore
```

## Configuration and logs

- **Settings:** `%AppData%\BtAudioMixer\config.json` — device selections and volume levels, written on close and read on next launch.
- **Log file:** `%AppData%\BtAudioMixer\error_log.txt` — timestamped `INFO`/`WARN`/`ERROR` lines appended across runs, useful for diagnosing issues after the fact since the in-app Activity Log doesn't persist.
- Deleting `config.json` resets the app to defaults; it's recreated automatically on next save.

## Contributing

Contributions are welcome — bug reports, PRs, and testing on hardware/setups the maintainer hasn't tried are all useful, especially the open items in [Compatibility](#compatibility) and [Performance](#performance) (tested hardware list, iPhone/iOS as a source, CPU/memory numbers, screenshots or a demo clip). Open an issue or PR on this repo.

## Credits

- [NAudio](https://github.com/naudio/NAudio) — WASAPI capture/render, resampling, and mixing primitives.
- Buffering, MMCSS thread-priority, and device-enumeration patterns adapted from [WindowsDualAudioManager](https://github.com/MaheshSharan/WindowsDualAudioManager).
- `AudioPlaybackConnection` call-pattern (device selector, connect/open/start lifecycle) adapted from [AudioPlaybackConnector2](https://github.com/N0ahTM/AudioPlaybackConnector2)'s C++/WinRT implementation, ported to C#.

## License

[MIT](LICENSE) — see the `LICENSE` file. Use it, fork it, ship it.
