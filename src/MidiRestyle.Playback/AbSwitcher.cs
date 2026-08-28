using System.Diagnostics;
using MidiRestyle.Core.Output;

namespace MidiRestyle.Playback;

/// <summary>
/// Drives two sequences as an A/B pair: exactly one is ever started, and switching between them
/// preserves the playhead.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why one at a time.</b> Two sequences running concurrently drift, because each keeps its own
/// clock, and both want the single output device. The alternative - one merged sequence with muted
/// channel groups - doubles the channel budget, which is already the binding constraint on
/// microtonal output. So: one device, two players, one started.
/// </para>
/// <para>
/// <b>The switch, in order.</b> Read the running side's time; stop it; send
/// <see cref="PitchBendEncoder.StopSequence"/> to every channel; move the other side to that same
/// time; start it if the first was running. The stop sequence is CC123 <em>and</em> a bend reset to
/// 8192 on each channel, and neither substitutes for the other: without CC123 notes hang, and
/// without the bend reset the next sequence inherits a stale pitch wheel and plays detuned.
/// </para>
/// <para>
/// This type knows nothing about MIDI devices or files. Everything platform-bound is behind
/// <see cref="ISequencePlayer"/> and <see cref="IMidiSink"/>, which is what lets the switch
/// semantics be tested on a machine with no MIDI device.
/// </para>
/// </remarks>
public sealed class AbSwitcher : IDisposable
{
    private readonly ISequencePlayer _original;
    private readonly ISequencePlayer _restyled;
    private readonly IMidiSink _sink;
    private bool _disposed;

    /// <summary>Creates a switcher over two already-built sequences.</summary>
    /// <param name="original">The unmodified side.</param>
    /// <param name="restyled">The transformed side.</param>
    /// <param name="sink">Where the stop sequence goes.</param>
    /// <param name="stopChannels">
    /// Every channel the stop sequence must reach. That is the allocated pitch-bend channels, plus
    /// any other channel either side sounds on - a note left hanging on an unlisted channel is just
    /// as stuck, and a stale bend left on one detunes whatever plays there next.
    /// </param>
    /// <param name="restyledTuning">
    /// Each allocated channel and the bend it holds, for re-establishing tuning after a seek. Empty
    /// for a 12-TET target, which needs none. See <see cref="Retune"/> for why a seek needs this.
    /// </param>
    public AbSwitcher(
        ISequencePlayer original,
        ISequencePlayer restyled,
        IMidiSink sink,
        IEnumerable<int> stopChannels,
        IEnumerable<(int Channel, double BendCents)>? restyledTuning = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(restyled);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(stopChannels);

        _original = original;
        _restyled = restyled;
        _sink = sink;
        StopChannels = [.. stopChannels.Distinct().Order()];
        RestyledTuning = restyledTuning is null ? [] : [.. restyledTuning];
    }

    /// <summary>Each allocated channel and the bend it holds. Empty for a 12-TET target.</summary>
    public IReadOnlyList<(int Channel, double BendCents)> RestyledTuning { get; }

    /// <summary>Which side is selected. Only ever one; the other is always stopped.</summary>
    public PlaybackSide ActiveSide { get; private set; } = PlaybackSide.Original;

    /// <summary>The selected side's player.</summary>
    public ISequencePlayer Active => ActiveSide == PlaybackSide.Original ? _original : _restyled;

    /// <summary>The unselected side's player. Guaranteed stopped.</summary>
    public ISequencePlayer Inactive => ActiveSide == PlaybackSide.Original ? _restyled : _original;

    /// <summary>Every channel the stop sequence is sent to, ascending.</summary>
    public IReadOnlyList<int> StopChannels { get; }

    /// <summary>Whether the active side's clock is running.</summary>
    public bool IsPlaying => Active.IsRunning;

    /// <summary>The active side's playhead.</summary>
    public TimeSpan Position => Active.CurrentTime;

    /// <summary>The longer of the two sides. In practice both are the same length by construction.</summary>
    public TimeSpan Duration => _original.Duration > _restyled.Duration
        ? _original.Duration
        : _restyled.Duration;

    /// <summary>
    /// How long the last <see cref="SwitchTo"/> took, wall clock. The target is under 30 ms; this is
    /// exposed so it can be measured rather than assumed.
    /// </summary>
    public TimeSpan LastSwitchGap { get; private set; }

    /// <summary>Starts or resumes the active side.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Active.Start();
    }

    /// <summary>
    /// Halts the active side, keeping its position, and sends the stop sequence. Deliberately
    /// unconditional: the stop sequence is a handful of bytes, and sending it to an already-silent
    /// synth is harmless, whereas skipping it because we believe nothing is sounding is exactly how
    /// a note hangs.
    /// </summary>
    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Active.Stop();
        SendStopSequence();
    }

    /// <summary>Pauses, then rewinds <em>both</em> sides so either can be started from the top.</summary>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Pause();
        _original.MoveToTime(TimeSpan.Zero);
        _restyled.MoveToTime(TimeSpan.Zero);
    }

    /// <summary>
    /// Moves both sides to <paramref name="position"/>, clamped to the sequence. Both, so a later
    /// switch lands in the same musical place without needing to seek at switch time.
    /// </summary>
    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TimeSpan target = Clamp(position);
        _original.MoveToTime(target);
        _restyled.MoveToTime(target);

        // A seek lands the restyled side on whatever bend state the synth happens to hold. Restore
        // it explicitly rather than relying on the player's own controller replay - see Retune.
        Retune();
    }

    /// <summary>Switches to the side that is not active, and returns it.</summary>
    public PlaybackSide Toggle()
    {
        SwitchTo(ActiveSide == PlaybackSide.Original ? PlaybackSide.Restyled : PlaybackSide.Original);
        return ActiveSide;
    }

    /// <summary>
    /// Switches to <paramref name="side"/>, preserving the playhead and the running state. A no-op
    /// when that side is already active - re-selecting the current side must not restart it.
    /// </summary>
    public void SwitchTo(PlaybackSide side)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (side == ActiveSide)
        {
            return;
        }

        long startedAt = Stopwatch.GetTimestamp();

        ISequencePlayer leaving = Active;
        ISequencePlayer arriving = Inactive;

        bool wasRunning = leaving.IsRunning;
        TimeSpan at = leaving.CurrentTime;

        leaving.Stop();
        SendStopSequence();

        arriving.MoveToTime(Clamp(at));
        ActiveSide = side;

        // Before sounding anything: the stop sequence just reset every channel's bend to centre, so
        // the restyled side must have its tuning put back or it plays 12-TET from here.
        if (side == PlaybackSide.Restyled)
        {
            Retune();
        }

        if (wasRunning)
        {
            arriving.Start();
        }

        LastSwitchGap = Stopwatch.GetElapsedTime(startedAt);
    }

    /// <summary>
    /// <summary>
    /// Re-establishes the restyled side's bend range and bend on every allocated channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Necessary because the stop sequence deliberately resets every channel to centre, and because
    /// seeking cannot be trusted to restore it. DryWetMIDI's <c>Playback</c> replays tracked
    /// controllers in <em>ascending controller number</em>, so an authored RPN handshake comes back
    /// as <c>CC6, CC38, ... CC100, CC101</c> - the data entry before the RPN-null, with no
    /// re-selection of RPN 0/0 first. It therefore applies to whichever RPN the synth currently
    /// points at.
    /// </para>
    /// <para>
    /// On a fresh synth that is RPN 0/0 with a GM default of +/-2 semitones, which happens to equal
    /// the encoder's default range - so it looks correct <b>by luck</b>. A synth left pointing at a
    /// different RPN, or a project using a range other than +/-2, would be silently mistuned from
    /// the seek point onward. This removes the luck.
    /// </para>
    /// <para>
    /// Deliberately <em>not</em> the full setup sequence: that carries bank select and a program
    /// change, and this class has no source-channel state to supply them - it would send
    /// <c>Program 0</c> and reset every instrument in the piece to piano.
    /// </para>
    /// </remarks>
    private void Retune()
    {
        foreach ((int channel, double bendCents) in RestyledTuning)
        {
            foreach (ChannelEvent channelEvent in PitchBendEncoder.RetuneSequence(channel, bendCents))
            {
                _sink.Send(channelEvent);
            }
        }
    }

    /// <summary>
    /// Sends CC123 plus a bend reset to 8192 on every channel in <see cref="StopChannels"/>. The
    /// sequence itself comes from <see cref="PitchBendEncoder.StopSequence"/> - the same component
    /// the exporter uses - rather than being hand-rolled here.
    /// </summary>
    private void SendStopSequence()
    {
        foreach (ChannelEvent channelEvent in PitchBendEncoder.StopSequence(StopChannels))
        {
            _sink.Send(channelEvent);
        }
    }

    private TimeSpan Clamp(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        TimeSpan duration = Duration;
        return position > duration ? duration : position;
    }

    /// <summary>
    /// Silences the synth, then disposes both players. Both are unmanaged-backed; leaking one holds
    /// the port against every other application on the machine.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _original.Stop();
        _restyled.Stop();
        SendStopSequence();

        _original.Dispose();
        _restyled.Dispose();
    }
}

/// <summary>
/// Rate-limits playhead notifications to a fixed ceiling, default 60 a second.
/// </summary>
/// <remarks>
/// <para>
/// DryWetMIDI raises playback events on a background thread, and a dense file is tens of thousands
/// of events. Notifying per event would flood whatever is listening - and in the app, flood the UI
/// thread's dispatcher queue - with updates nobody can see. A screen refresh is the only rate that
/// matters, so everything faster than that is dropped.
/// </para>
/// <para>
/// The clock is injectable so the ceiling can be asserted deterministically against simulated time
/// instead of against a sleep.
/// </para>
/// </remarks>
public sealed class PlayheadThrottle
{
    /// <summary>The default ceiling, in notifications per second.</summary>
    public const int DefaultHertz = 60;

    private readonly Func<TimeSpan> _clock;
    private readonly Lock _gate = new();
    private TimeSpan _last;

    /// <summary>Creates a throttle.</summary>
    /// <param name="minimumInterval">
    /// The shortest gap between two notifications. Defaults to 1/<see cref="DefaultHertz"/> second.
    /// </param>
    /// <param name="clock">
    /// A monotonic elapsed-time source. Defaults to a <see cref="Stopwatch"/> started now; tests
    /// pass a simulated one.
    /// </param>
    public PlayheadThrottle(TimeSpan? minimumInterval = null, Func<TimeSpan>? clock = null)
    {
        TimeSpan interval = minimumInterval ?? TimeSpan.FromSeconds(1.0 / DefaultHertz);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero, nameof(minimumInterval));

        MinimumInterval = interval;

        if (clock is null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            _clock = () => stopwatch.Elapsed;
        }
        else
        {
            _clock = clock;
        }

        _last = _clock();
    }

    /// <summary>The shortest gap between two notifications.</summary>
    public TimeSpan MinimumInterval { get; }

    /// <summary>
    /// Whether a notification should be raised now. Thread-safe: playback events arrive on a
    /// background thread and a timer may be asking at the same moment.
    /// </summary>
    public bool TryEmit()
    {
        TimeSpan now = _clock();

        lock (_gate)
        {
            if (now - _last < MinimumInterval)
            {
                return false;
            }

            _last = now;
            return true;
        }
    }

    /// <summary>Forgets the last notification time, so the next <see cref="TryEmit"/> passes.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _last = _clock() - MinimumInterval;
        }
    }
}
