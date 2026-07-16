// src/Wc3.Studio/AudioPlayer.cs
using NAudio.Wave;

namespace Wc3.Studio;

/// <summary>
/// Plays a single map sound at a time. NAudio's MediaFoundationReader needs a file, so
/// the bytes are staged to a temp file (Windows codecs handle mp3/wav/wma; flac/ogg only
/// if a system codec is installed). One clip plays at a time; Stop/Dispose tears down the
/// device and deletes the temp. Never throws to the caller beyond the initial Play.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private MediaFoundationReader? _reader;
    private string? _tempFile;

    /// <summary>Raised (on a background thread) when the clip finishes on its own.</summary>
    public event EventHandler? PlaybackStopped;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>Stops any current clip and starts playing the given bytes (ext picks the temp suffix).</summary>
    public void Play(byte[] bytes, string extension)
    {
        Stop();
        var suffix = string.IsNullOrEmpty(extension) ? ".bin" : extension;
        _tempFile = Path.Combine(Path.GetTempPath(), "wc3ctl_audio_" + Guid.NewGuid().ToString("N") + suffix);
        File.WriteAllBytes(_tempFile, bytes);

        _reader = new MediaFoundationReader(_tempFile);
        _output = new WaveOutEvent();
        _output.PlaybackStopped += OnStopped;
        _output.Init(_reader);
        _output.Play();
    }

    public void Stop()
    {
        if (_output is not null)
            _output.PlaybackStopped -= OnStopped;
        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
        DeleteTemp();
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        DeleteTemp();
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteTemp()
    {
        if (_tempFile is null) return;
        try { if (File.Exists(_tempFile)) File.Delete(_tempFile); } catch { /* temp cleanup best-effort */ }
        _tempFile = null;
    }

    public void Dispose() => Stop();
}
