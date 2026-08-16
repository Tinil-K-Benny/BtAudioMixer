using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BtAudioMixer.Core.Output
{
    /// <summary>
    /// Wraps a sample provider with resampling and/or channel conversion, only when
    /// the source format actually differs from the target. Ported from
    /// WindowsDualAudioManager's Core.Output.SampleFormatConverter.
    /// </summary>
    public static class SampleFormatConverter
    {
        public static ISampleProvider Convert(ISampleProvider source, WaveFormat targetFormat)
        {
            ISampleProvider result = source;

            if (result.WaveFormat.Channels != targetFormat.Channels)
            {
                result = ConvertChannelCount(result, targetFormat.Channels);
            }

            if (result.WaveFormat.SampleRate != targetFormat.SampleRate)
            {
                result = new WdlResamplingSampleProvider(result, targetFormat.SampleRate);
            }

            return result;
        }

        private static ISampleProvider ConvertChannelCount(ISampleProvider source, int targetChannels)
        {
            return (source.WaveFormat.Channels, targetChannels) switch
            {
                (1, 2) => new MonoToStereoSampleProvider(source),
                (2, 1) => new StereoToMonoSampleProvider(source),
                _ => source
            };
        }
    }
}
