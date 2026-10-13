// src/Wc3.Commands/SoundConvert.cs
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Wc3.Commands;

/// <summary>The sample rate, channel count and byte size of an audio clip, before or after a convert.</summary>
public sealed record SoundInfo(int SampleRate, int Channels, long Bytes);

/// <summary>
/// Decodes, resamples and downmixes audio through NAudio. Shared by the sound optimiser so the
/// decode and encode live in one place rather than inline in the command. NAudio's AudioFileReader
/// handles .wav natively and .mp3 through the Windows codecs, the same route the Studio's
/// AudioPlayer uses, and WdlResamplingSampleProvider does the resample with no codec dependency.
/// </summary>
public static class SoundConvert
{
    /// <summary>
    /// Reads <paramref name="path"/> (any audio AudioFileReader can open), optionally folds it to
    /// mono, resamples it to <paramref name="targetRate"/> and returns 16-bit PCM WAV bytes. The
    /// <paramref name="before"/> info describes the source file, <paramref name="after"/> the WAV
    /// produced.
    /// </summary>
    public static byte[] ToOptimizedWav(
        string path, int targetRate, bool mono, out SoundInfo before, out SoundInfo after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (targetRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetRate), targetRate, "sample rate must be positive");

        using var reader = new AudioFileReader(path);
        before = new SoundInfo(reader.WaveFormat.SampleRate, reader.WaveFormat.Channels, new FileInfo(path).Length);

        // Fold to mono first, so the resampler runs on the smaller signal.
        ISampleProvider samples = reader;
        if (mono) samples = Downmix(samples);
        if (samples.WaveFormat.SampleRate != targetRate)
            samples = new WdlResamplingSampleProvider(samples, targetRate);

        using var ms = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(ms, samples.ToWaveProvider16());
        var bytes = ms.ToArray();
        after = new SoundInfo(targetRate, mono ? 1 : reader.WaveFormat.Channels, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Folds any channel count to mono. Stereo is averaged directly, more than two channels keep
    /// the first pair and average them, since a WC3 sound is at most stereo in practice.
    /// </summary>
    private static ISampleProvider Downmix(ISampleProvider source)
    {
        switch (source.WaveFormat.Channels)
        {
            case 1:
                return source;
            case 2:
                return new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f };
            default:
                var firstPair = new MultiplexingSampleProvider(new[] { source }, 2);
                return new StereoToMonoSampleProvider(firstPair) { LeftVolume = 0.5f, RightVolume = 0.5f };
        }
    }
}
