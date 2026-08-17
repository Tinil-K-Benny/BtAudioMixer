using NAudio.Wave;

namespace BtAudioMixer.Core.Mixing
{
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
