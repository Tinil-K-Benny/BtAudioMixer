namespace BtAudioMixer.Core.Devices
{
    /// <summary>
    /// Recognizes render devices that are virtual audio cables by name — the common
    /// products (VB-CABLE, Muzychenko's Virtual Audio Cable, Voicemeeter) all put a
    /// telltale word in their endpoint's friendly name. Detection only: there is no
    /// Windows API to create a virtual sound card in software, so provisioning a
    /// missing one always requires an elevated driver install the user has to
    /// consent to — this just tells them what they already have.
    /// </summary>
    public static class VirtualCableDetector
    {
        private static readonly string[] Keywords =
        {
            "virtual audio cable", "vb-audio", "vb-cable", "cable input", "cable output", "voicemeeter"
        };

        public static List<AudioDevice> FindVirtualCables(IEnumerable<AudioDevice> renderDevices)
        {
            return renderDevices
                .Where(d => Keywords.Any(k => d.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }
}
