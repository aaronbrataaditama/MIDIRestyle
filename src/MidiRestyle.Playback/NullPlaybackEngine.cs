using MidiRestyle.Core.Io;

namespace MidiRestyle.Playback;

/// <summary>
/// A complete, honest no-op engine: it accepts every call, reports that it is not playing, and says
/// why it cannot make sound.
/// </summary>
/// <remarks>
/// <para>
/// <b>No MIDI output device is a normal state, not an error.</b> A headless CI machine, a machine
/// with no soft synth installed, and Linux - where DryWetMIDI ships no native at all - must all
/// leave the app fully functional minus audio. This type exists so that nothing above the playback
/// boundary has to null-check an engine or platform-check a machine: ask
/// <see cref="PlaybackEngineFactory"/> for an engine and you always get one.
/// </para>
/// <para>
/// It is deliberately not silent about being null. <see cref="Reason"/> is always a non-empty
/// sentence naming the cause, so the status bar can tell the user why the transport is greyed out
/// rather than leaving them to guess.
/// </para>
/// </remarks>
public sealed class NullPlaybackEngine : IPlaybackEngine
{
    /// <summary>The reason used when a caller does not supply one.</summary>
    public const string DefaultReason =
        "No MIDI output device is available, so playback is disabled. Everything else works.";

    private PlaybackSequences? _sequences;

    /// <summary>Creates a null engine.</summary>
    /// <param name="reason">
    /// Why there is no playback, as a user-facing sentence. Null or blank falls back to
    /// <see cref="DefaultReason"/> - a null engine that cannot say why is the one thing this type
    /// must never be.
    /// </param>
    public NullPlaybackEngine(string? reason = null) =>
        Reason = string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason;

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string Reason { get; }

    /// <inheritdoc />
    public string? DeviceName => null;

    /// <inheritdoc />
    public bool IsLoaded => _sequences is not null;

    /// <inheritdoc />
    public bool IsPlaying => false;

    /// <inheritdoc />
    public PlaybackSide ActiveSide { get; private set; } = PlaybackSide.Original;

    /// <inheritdoc />
    public TimeSpan Position { get; private set; }

    /// <inheritdoc />
    public TimeSpan Duration => TimeSpan.Zero;

    /// <inheritdoc />
    public IReadOnlyList<int> StopChannels => _sequences?.RestyledChannels ?? [];

    /// <summary>
    /// Never raised: nothing here has a playhead to move. Subscribing is still allowed - and
    /// deliberately silent - so a host can wire the same handler up regardless of which engine it
    /// was handed. The accessors are empty rather than backed by a field precisely because a stored
    /// handler that is never invoked would be a leak and a lie at once.
    /// </summary>
    public event EventHandler<PlayheadEventArgs>? PlayheadMoved
    {
        add => _ = value;
        remove => _ = value;
    }

    /// <summary>
    /// Records the sequences so <see cref="IsLoaded"/> and <see cref="StopChannels"/> answer
    /// honestly, and does nothing else with them.
    /// </summary>
    public void Load(PlaybackSequences sequences)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        _sequences = sequences;
        Position = TimeSpan.Zero;
    }

    /// <inheritdoc />
    public void Unload() => _sequences = null;

    /// <summary>Does nothing. There is no device to play through.</summary>
    public void Play()
    {
        // Intentionally empty. Callers must not have to ask whether playback is real.
    }

    /// <summary>Does nothing. Nothing is sounding, so nothing needs silencing.</summary>
    public void Pause()
    {
        // Intentionally empty.
    }

    /// <summary>Rewinds the reported position. There is nothing to silence.</summary>
    public void Stop() => Position = TimeSpan.Zero;

    /// <summary>
    /// Records the requested position, clamped at zero. <see cref="Duration"/> is zero here, so the
    /// value is only ever reflected back - but reflecting it back beats silently ignoring it, and it
    /// keeps a host that drives the transport blind to which engine it has.
    /// </summary>
    public void Seek(TimeSpan position) =>
        Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;

    /// <summary>Records the selected side. Exactly one is active here too.</summary>
    public void SwitchTo(PlaybackSide side) => ActiveSide = side;

    /// <inheritdoc />
    public PlaybackSide Toggle()
    {
        SwitchTo(ActiveSide == PlaybackSide.Original ? PlaybackSide.Restyled : PlaybackSide.Original);
        return ActiveSide;
    }

    /// <summary>Nothing to release, and safe to call any number of times.</summary>
    public void Dispose() => _sequences = null;
}
