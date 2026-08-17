namespace BtAudioMixer.Core.Devices
{
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
