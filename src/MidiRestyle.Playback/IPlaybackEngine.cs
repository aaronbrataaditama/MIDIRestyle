using MidiRestyle.Core.Io;
using MidiRestyle.Core.Output;

namespace MidiRestyle.Playback;

/// <summary>Which of the two A/B sequences is the one the device is currently sounding.</summary>
public enum PlaybackSide
{
    /// <summary>The file as loaded, with no pitch remapping.</summary>
    Original,

    /// <summary>The same file with the scale transform applied.</summary>
    Restyled,
}

/// <summary>
/// A playhead position, reported at most ~60 times a second.
/// </summary>
/// <param name="Position">Where the playhead is, from the start of the sequence.</param>
/// <param name="Duration">The active sequence's total length.</param>
/// <param name="Side">Which side is sounding.</param>
public sealed record PlayheadEventArgs(TimeSpan Position, TimeSpan Duration, PlaybackSide Side);

/// <summary>
/// A sink for the individual MIDI channel messages the engine sends <em>outside</em> the sequence -
/// which in practice means the stop sequence and nothing else.
/// </summary>
/// <remarks>
/// The seam exists so <see cref="AbSwitcher"/> can be tested against the exact
/// <see cref="ChannelEvent"/> stream it emits, on a machine with no MIDI device at all. The real
/// implementation translates each event into DryWetMIDI's own type and hands it to the output
/// device; that translation is the entire platform-bound part.
/// </remarks>
public interface IMidiSink
{
    /// <summary>Sends one channel-voice message.</summary>
    void Send(ChannelEvent channelEvent);
}

/// <summary>
/// One of the two A/B sequences, reduced to the five operations the switch actually needs.
/// </summary>
/// <remarks>
/// This mirrors the subset of DryWetMIDI's <c>Playback</c> that <see cref="AbSwitcher"/> uses.
/// <c>Playback</c> is a concrete class with no virtual members and no interface of its own, so it
/// cannot be faked directly - hence this seam. Keeping it to five members keeps the real adapter
/// small enough that the part no test can reach is obviously trivial.
/// </remarks>
public interface ISequencePlayer : IDisposable
{
    /// <summary>Whether this sequence's clock is running.</summary>
    bool IsRunning { get; }

    /// <summary>The sequence's total length.</summary>
    TimeSpan Duration { get; }

    /// <summary>Where this sequence's playhead currently sits.</summary>
    TimeSpan CurrentTime { get; }

    /// <summary>Moves the playhead, whether running or not.</summary>
    void MoveToTime(TimeSpan time);

    /// <summary>Starts (or resumes) from the current position.</summary>
    void Start();

    /// <summary>Halts the clock, keeping the current position.</summary>
    void Stop();
}

/// <summary>
/// Plays the original and restyled sequences on a MIDI output device, one at a time, and switches
/// between them without losing the playhead.
/// </summary>
/// <remarks>
/// <para>
/// <b>Playback is driven by the exported bytes.</b> The input is <see cref="PlaybackSequences"/> -
/// the very byte streams <c>MidiFileExporter</c> writes to disk - so "preview and export cannot
/// diverge" holds by construction rather than by test. Nothing here rebuilds a sequence from the
/// domain model.
/// </para>
/// <para>
/// <b>Threading.</b> <see cref="PlayheadMoved"/> is raised on a background thread, never the UI
/// thread. <b>Consumers must marshal it themselves</b> - in the Avalonia app that means
/// <c>Dispatcher.UIThread.Post</c>. This assembly deliberately has no UI dependency, so it cannot
/// and will not do that for you. If you would rather not handle an event at all, ignore
/// <see cref="PlayheadMoved"/> and poll <see cref="Position"/> from your own ~60 Hz timer; it is
/// safe to read from any thread.
/// </para>
/// <para>
/// <b>No device is a normal state.</b> Never null-check or platform-check the engine: ask the
/// factory for one and it hands back <see cref="NullPlaybackEngine"/> when there is nothing to play
/// through, with <see cref="IsAvailable"/> false and <see cref="Reason"/> saying why.
/// </para>
/// </remarks>
public interface IPlaybackEngine : IDisposable
{
    /// <summary>Whether this engine can actually make sound.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Always a non-empty, user-facing sentence: which device is in use, or why none is.
    /// </summary>
    string Reason { get; }

    /// <summary>The output device's name, or null when there is none.</summary>
    string? DeviceName { get; }

    /// <summary>Whether a sequence has been loaded and is ready to play.</summary>
    bool IsLoaded { get; }

    /// <summary>Whether the active sequence's clock is running.</summary>
    bool IsPlaying { get; }

    /// <summary>Which side is currently selected. Only ever one.</summary>
    PlaybackSide ActiveSide { get; }

    /// <summary>The active side's playhead. Safe to read from any thread; poll it at ~60 Hz.</summary>
    TimeSpan Position { get; }

    /// <summary>The loaded sequence's length, or <see cref="TimeSpan.Zero"/> when nothing is loaded.</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Every channel the stop sequence reaches: the allocated channels, plus any other channel
    /// either side sounds on. Empty until something is loaded.
    /// </summary>
    IReadOnlyList<int> StopChannels { get; }

    /// <summary>
    /// Raised at most ~60 times a second, <b>on a background thread</b>. Consumers must marshal to
    /// their own UI thread; see the remarks on <see cref="IPlaybackEngine"/>.
    /// </summary>
    event EventHandler<PlayheadEventArgs>? PlayheadMoved;

    /// <summary>
    /// Loads both sides. Stops anything already playing (with the full stop sequence) and keeps the
    /// playhead where it was, clamped to the new duration, so a mid-playback rebuild re-seeks rather
    /// than jumping to the top. Does not start playing.
    /// </summary>
    void Load(PlaybackSequences sequences);

    /// <summary>
    /// Discards whatever is loaded, silencing first.
    /// </summary>
    /// <remarks>
    /// Necessary because stopping is not forgetting. Loading a new file used to leave the previous
    /// sequences loaded, so the next Play replayed the file the user had just closed - the transport
    /// looked idle and the engine was still holding a whole piece. Anything that invalidates the
    /// loaded content must call this, not merely <c>Stop</c>.
    /// </remarks>
    void Unload();

    /// <summary>Starts or resumes the active side. A no-op when nothing is loaded.</summary>
    void Play();

    /// <summary>Halts the clock, keeps the position, and sends the stop sequence.</summary>
    void Pause();

    /// <summary>Halts the clock, sends the stop sequence, and rewinds both sides to the start.</summary>
    void Stop();

    /// <summary>Moves the playhead on both sides, so a later switch lands in the same place.</summary>
    void Seek(TimeSpan position);

    /// <summary>
    /// Switches to <paramref name="side"/>: stop the running side, send the stop sequence to every
    /// channel, seek the other side to the same time, and start it if the first side was running.
    /// A no-op when that side is already active.
    /// </summary>
    void SwitchTo(PlaybackSide side);

    /// <summary>Switches to whichever side is not active, and returns the new one.</summary>
    PlaybackSide Toggle();
}
