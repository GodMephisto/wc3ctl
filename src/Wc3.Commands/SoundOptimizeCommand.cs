// src/Wc3.Commands/SoundOptimizeCommand.cs
namespace Wc3.Commands;

/// <summary>What a sound optimise changed, before and after, for the report.</summary>
public sealed record SoundOptimizeResult(
    string Path,
    int SampleRateBefore,
    int ChannelsBefore,
    long BytesBefore,
    int SampleRateAfter,
    int ChannelsAfter,
    long BytesAfter);

/// <summary>
/// Shrinks a sound by decoding it, resampling to a target rate and optionally folding it to mono,
/// then writing a smaller 16-bit PCM .wav. The decode and encode live in <see cref="SoundConvert"/>.
/// A WC3 map's imported audio is usually a full-rate stereo file far heavier than the engine needs,
/// and this is the audio half of making a map compact, beside <see cref="TextureOptimizeCommand"/>.
/// </summary>
public static class SoundOptimizeCommand
{
    /// <summary>The default target rate, 22050 Hz, which most WC3 sounds already use.</summary>
    public const int DefaultRate = 22050;

    public static SoundOptimizeResult ExecutePath(string path, int targetRate, bool mono, string outPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException($"sound not found: {path}", path);
        if (string.IsNullOrWhiteSpace(outPath))
            throw new ArgumentException("output path must not be blank", nameof(outPath));

        var bytes = SoundConvert.ToOptimizedWav(path, targetRate, mono, out var before, out var after);
        File.WriteAllBytes(outPath, bytes);

        return new SoundOptimizeResult(outPath,
            before.SampleRate, before.Channels, before.Bytes,
            after.SampleRate, after.Channels, after.Bytes);
    }
}
