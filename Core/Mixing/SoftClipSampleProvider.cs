using NAudio.Wave;

namespace BtAudioMixer.Core.Mixing
{
    /// <summary>
    /// Soft-clips the summed mix so two loud sources together can't hard-clip into
    /// harsh digital distortion — tanh saturates gracefully above the [-1, 1] range
    /// instead of chopping the waveform flat. Only engages once the signal actually
    /// exceeds unity; quiet mixes pass through unchanged.
    /// </summary>
    public sealed class SoftClipSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;

        public SoftClipSampleProvider(ISampleProvider source) => _source = source;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);

            for (int i = offset; i < offset + samplesRead; i++)
            {
                float sample = buffer[i];
                if (sample > 1f || sample < -1f)
                {
                    buffer[i] = MathF.Tanh(sample);
                }
            }

            return samplesRead;
        }
    }
}
