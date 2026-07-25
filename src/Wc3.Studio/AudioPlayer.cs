// src/Wc3.Studio/AudioPlayer.cs
using NAudio.Wave;
using NAudio.CoreAudioApi;

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
    private IWavePlayer? _output;
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
                _output = OpenOutput(_reader);   // WaveOut mapper, then WASAPI; clear throw if truly no device
                _output.PlaybackStopped += OnStopped;
                _output.Play();
            }
            catch
            {
                Teardown();   // a failed Play must leave no open device, reader, or temp file
                throw;
            }
        }
    }

    /// <summary>
    /// Opens an initialized output device for <paramref name="reader"/>. Tries the legacy
    /// WaveOut mapper first (WAVE_MAPPER = the current default device; lowest overhead and
    /// works on most boxes). If winmm rejects it — which happens on machines whose default
    /// endpoint the mapper can't resolve, surfacing as "BadDeviceId calling waveOutOpen" —
    /// it consults the modern Core Audio stack and plays via WASAPI on the default render
    /// endpoint instead. Only when Core Audio reports no active render endpoint at all
    /// (headless/remote session, or audio disabled) does it throw a clear, diagnosable
    /// <see cref="InvalidOperationException"/> rather than winmm's raw device error.
    /// </summary>
    private static IWavePlayer OpenOutput(IWaveProvider reader)
    {
        WaveOutEvent? waveOut = null;
        try
        {
            waveOut = new WaveOutEvent { DeviceNumber = -1 };   // -1 = WAVE_MAPPER (default device)
            waveOut.Init(reader);
            return waveOut;
        }
        catch (NAudio.MmException)
        {
            waveOut?.Dispose();   // release the half-open winmm handle before falling back
        }

        // winmm's mapper failed. Ask Core Audio whether a default render endpoint actually
        // exists; if so, WASAPI can drive it even when the legacy mapper won't.
        using var enumerator = new MMDeviceEnumerator();
        var activeEndpoints = enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Count;
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            throw new InvalidOperationException(
                $"no audio output device is available (active render endpoints: {activeEndpoints})");

        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        // Shared mode + push model; NAudio resamples the clip to the endpoint mix format.
        var wasapi = new WasapiOut(device, AudioClientShareMode.Shared, false, 200);
        wasapi.Init(reader);
        return wasapi;
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
