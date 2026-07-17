// src/Wc3.Studio/AudioPlayer.cs
using NAudio.Wave;

namespace Wc3.Studio;

/// <summary>
/// Plays a single map sound at a time. NAudio's MediaFoundationReader needs a file, so
/// the bytes are staged to a temp file (Windows codecs handle mp3/wav/wma; flac/ogg only
/// if a system codec is installed). One clip plays at a time; Stop/Dispose tears down the
/// device and deletes the temp. Never throws to the caller beyond the initial Play.
///
/// Thread-safety: Play/Stop (UI thread) and the natural-finish callback (which NAudio may
/// raise on a background thread when no SynchronizationContext is captured) are serialized
/// under <see cref="_gate"/>. A finish callback that arrives after the clip it belongs to
/// has already been replaced/stopped is ignored (guarded on sender identity), so it can
/// never tear down a newer clip.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly object _gate = new();
    private WaveOutEvent? _output;
    private MediaFoundationReader? _reader;
    private string? _tempFile;

    /// <summary>Raised when the clip finishes on its own (not on a manual Stop).</summary>
    public event EventHandler? PlaybackStopped;

    public bool IsPlaying
    {
        get { lock (_gate) return _output?.PlaybackState == PlaybackState.Playing; }
    }

    /// <summary>Stops any current clip and starts playing the given bytes (ext picks the temp suffix).</summary>
    public void Play(byte[] bytes, string extension)
    {
        lock (_gate)
        {
            Teardown();
            var suffix = string.IsNullOrEmpty(extension) ? ".bin" : extension;
            _tempFile = Path.Combine(Path.GetTempPath(), "wc3ctl_audio_" + Guid.NewGuid().ToString("N") + suffix);
            File.WriteAllBytes(_tempFile, bytes);

            try
            {
                _reader = new MediaFoundationReader(_tempFile);
                // WAVE_MAPPER (-1): route to the current default output device. The NAudio
                // default, DeviceNumber = 0, opens the *first* enumerated device, which
                // throws "BadDeviceId calling waveOutOpen" whenever device 0 isn't the
                // usable default (disabled/absent) — the common cause of that error.
                _output = new WaveOutEvent { DeviceNumber = -1 };
                _output.PlaybackStopped += OnStopped;
                _output.Init(_reader);
                _output.Play();
            }
            catch (NAudio.MmException mm) when (mm.Result == NAudio.MmResult.BadDeviceId)
            {
                // WAVE_MAPPER still failed → the box genuinely has no usable playback
                // endpoint (headless/remote session, or audio disabled). Surface that
                // plainly instead of winmm's raw "BadDeviceId calling waveOutOpen".
                Teardown();
                throw new InvalidOperationException("no audio output device is available", mm);
            }
            catch
            {
                Teardown();   // a failed Play must leave no open device, reader, or temp file
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_gate) Teardown();
    }

    /// <summary>Full device + temp teardown. The caller must hold <see cref="_gate"/>.</summary>
    private void Teardown()
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
        lock (_gate)
        {
            // A manual Stop/Play unsubscribes before stopping, so reaching here means a
            // natural finish. If it isn't the current output, a newer clip has already
            // taken over — ignore this stale callback so we don't tear the new clip down.
            if (!ReferenceEquals(sender, _output))
                return;
            Teardown();
        }
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
