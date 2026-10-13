// tests/Wc3.Tests/SoundOptimizeCommandTests.cs
using NAudio.Wave;
using Wc3.Commands;
using Xunit;

namespace Wc3.Tests;

/// <summary>Hermetic coverage of <see cref="SoundOptimizeCommand"/> and the <see cref="SoundConvert"/>
/// helper it wraps. A small .wav is generated in memory with NAudio, optimised, and the produced
/// .wav is read back to prove the rate, channel count and byte size it reports. No corpus files.</summary>
public class SoundOptimizeCommandTests
{
    /// <summary>A real .wav on disk, a 440 Hz tone of the given rate, channels and length.</summary>
    private static string NewWav(int rate, int channels, int milliseconds)
    {
        var path = Path.Combine(Path.GetTempPath(), $"wc3sound-{Guid.NewGuid():N}.wav");
        var format = new WaveFormat(rate, 16, channels);
        using var writer = new WaveFileWriter(path, format);
        int frames = rate * milliseconds / 1000;
        for (int i = 0; i < frames; i++)
            for (int c = 0; c < channels; c++)
                writer.WriteSample(0.2f * MathF.Sin(2 * MathF.PI * 440 * i / rate));
        return path;
    }

    private static string NewOut() =>
        Path.Combine(Path.GetTempPath(), $"wc3sound-out-{Guid.NewGuid():N}.wav");

    [Fact]
    public void Resamples_to_the_target_rate_and_downmixes_to_mono()
    {
        var src = NewWav(44100, 2, 200);
        var outPath = NewOut();
        try
        {
            var r = SoundOptimizeCommand.ExecutePath(src, 22050, mono: true, outPath);

            Assert.Equal(44100, r.SampleRateBefore);
            Assert.Equal(2, r.ChannelsBefore);
            Assert.Equal(new FileInfo(src).Length, r.BytesBefore);
            Assert.Equal(22050, r.SampleRateAfter);
            Assert.Equal(1, r.ChannelsAfter);
            Assert.Equal(new FileInfo(outPath).Length, r.BytesAfter);

            // The output must be a real, readable 16-bit PCM .wav at the new rate.
            using var read = new WaveFileReader(outPath);
            Assert.Equal(22050, read.WaveFormat.SampleRate);
            Assert.Equal(1, read.WaveFormat.Channels);
            Assert.Equal(16, read.WaveFormat.BitsPerSample);
            Assert.True(read.Length > 0);
        }
        finally
        {
            File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public void Keeps_stereo_when_mono_is_not_requested()
    {
        var src = NewWav(44100, 2, 200);
        var outPath = NewOut();
        try
        {
            var r = SoundOptimizeCommand.ExecutePath(src, 22050, mono: false, outPath);

            Assert.Equal(22050, r.SampleRateAfter);
            Assert.Equal(2, r.ChannelsAfter);

            using var read = new WaveFileReader(outPath);
            Assert.Equal(2, read.WaveFormat.Channels);
        }
        finally
        {
            File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public void A_same_rate_wav_passes_through_as_a_valid_wav()
    {
        var src = NewWav(22050, 1, 100);
        var outPath = NewOut();
        try
        {
            var r = SoundOptimizeCommand.ExecutePath(src, 22050, mono: false, outPath);

            Assert.Equal(22050, r.SampleRateAfter);
            Assert.Equal(1, r.ChannelsAfter);

            using var read = new WaveFileReader(outPath);
            Assert.Equal(22050, read.WaveFormat.SampleRate);
        }
        finally
        {
            File.Delete(src);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public void Missing_input_throws_file_not_found()
    {
        Assert.Throws<FileNotFoundException>(() =>
            SoundOptimizeCommand.ExecutePath("Z:\\nowhere\\missing.wav", 22050, false, NewOut()));
    }

    [Fact]
    public void A_non_positive_rate_is_rejected()
    {
        var src = NewWav(22050, 1, 50);
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SoundOptimizeCommand.ExecutePath(src, 0, false, NewOut()));
        }
        finally { File.Delete(src); }
    }
}
