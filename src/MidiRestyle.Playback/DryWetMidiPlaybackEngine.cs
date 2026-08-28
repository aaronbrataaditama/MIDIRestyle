using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Multimedia;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Output;
using DomainChannelEvent = MidiRestyle.Core.Output.ChannelEvent;
using DwmChannelEvent = Melanchall.DryWetMidi.Core.ChannelEvent;
using DwmPlayback = Melanchall.DryWetMidi.Multimedia.Playback;

namespace MidiRestyle.Playback;

/// <summary>
/// What a device probe found: a device to play through, or nothing and a reason.
/// </summary>
/// <remarks>
/// The probe hands back the opened device rather than just its name, so the factory never has to
/// re-find it. Looking a device up by name and opening it as two separate steps is a race - a USB
/// interface can be unplugged between them - and the second step would need its own error handling
/// for a failure the probe has already reported on.
/// </remarks>
public sealed record MidiDeviceProbeResult
{
    private MidiDeviceProbeResult(IOutputDevice? device, string? deviceName, string reason)
    {
        Device = device;
        DeviceName = deviceName;
        Reason = reason;
    }

    /// <summary>The device to play through, or null when there is none.</summary>
    public IOutputDevice? Device { get; }

    /// <summary>The device's name, or null when there is none.</summary>
    public string? DeviceName { get; }

    /// <summary>Always a non-empty, user-facing sentence - which device, or why none.</summary>
    public string Reason { get; }

    /// <summary>Whether a device was found.</summary>
    public bool HasDevice => Device is not null;

    /// <summary>No device, and why not.</summary>
    public static MidiDeviceProbeResult None(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new MidiDeviceProbeResult(device: null, deviceName: null, reason);
    }

    /// <summary>A device, opened and ready.</summary>
    public static MidiDeviceProbeResult Found(IOutputDevice device, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);

        return new MidiDeviceProbeResult(device, deviceName, $"Playing through '{deviceName}'.");
    }
}

/// <summary>Finds a MIDI output device, or explains why there is none.</summary>
/// <remarks>
/// The seam that makes engine selection testable. Enumerating real devices is the one thing a test
/// machine cannot control - a build agent has none, and this developer machine has the Microsoft GS
/// Wavetable Synth - so the decision is tested against a fake probe rather than against whatever
/// happens to be installed.
/// </remarks>
public interface IMidiDeviceProbe
{
    /// <summary>Looks for a device. Must never throw: no device is a normal outcome.</summary>
    MidiDeviceProbeResult Probe();
}

/// <summary>
/// The real probe: DryWetMIDI's device enumeration, with every platform failure turned into a
/// stated reason.
/// </summary>
/// <remarks>
/// <para>
/// Three distinct outcomes, all normal. A device is found. No device is installed, and
/// <c>GetAll()</c> returns an empty collection - the headless build agent case. Or the native
/// library that backs device enumeration is not there at all, and the P/Invoke throws
/// <see cref="DllNotFoundException"/> - which is Linux, permanently: DryWetMIDI 8.0.3 ships
/// <c>Native32.dll</c>, <c>Native64.dll</c> and <c>Native64.dylib</c>, and nothing for Linux. That
/// throw was reproduced, not assumed.
/// </para>
/// <para>
/// Every one of those is reported as "no device, here is why" rather than propagating. The whole
/// point of <see cref="NullPlaybackEngine"/> is that a machine with no audio still runs the app.
/// </para>
/// </remarks>
public sealed class OutputDeviceProbe : IMidiDeviceProbe
{
    private readonly string? _preferredDeviceName;

    /// <summary>Creates a probe.</summary>
    /// <param name="preferredDeviceName">
    /// The device to prefer if it is present, matched case-insensitively by name. When absent or not
    /// found, the first enumerated device is used.
    /// </param>
    public OutputDeviceProbe(string? preferredDeviceName = null) =>
        _preferredDeviceName = preferredDeviceName;

    /// <inheritdoc />
    public MidiDeviceProbeResult Probe()
    {
        ICollection<OutputDevice> devices;

        try
        {
            devices = OutputDevice.GetAll();
        }
        catch (DllNotFoundException ex)
        {
            return MidiDeviceProbeResult.None(
                "MIDI playback is not supported on this platform: the native device library is not "
                + $"available ({ex.Message.Trim()}). Everything except audio works.");
        }
        catch (BadImageFormatException ex)
        {
            return MidiDeviceProbeResult.None(
                "MIDI playback is unavailable: the native device library does not match this "
                + $"process architecture ({ex.Message.Trim()}). Everything except audio works.");
        }
        catch (TypeInitializationException ex)
        {
            return MidiDeviceProbeResult.None(
                "MIDI playback is unavailable: the device layer failed to initialise "
                + $"({ex.InnerException?.Message.Trim() ?? ex.Message.Trim()}). Everything except "
                + "audio works.");
        }
        catch (PlatformNotSupportedException ex)
        {
            return MidiDeviceProbeResult.None(
                $"MIDI playback is not supported on this platform ({ex.Message.Trim()}). Everything "
                + "except audio works.");
        }
        catch (MidiDeviceException ex)
        {
            return MidiDeviceProbeResult.None(
                $"MIDI devices could not be enumerated ({ex.Message.Trim()}). Everything except "
                + "audio works.");
        }

        OutputDevice? chosen = Choose(devices);

        return chosen is null
            ? MidiDeviceProbeResult.None(
                "No MIDI output device is installed, so playback is disabled. Everything else, "
                + "including export, works.")
            : MidiDeviceProbeResult.Found(chosen, chosen.Name);
    }

    /// <summary>
    /// Picks one device and disposes the rest. <c>GetAll</c> hands back a live handle for every
    /// device on the machine; keeping the ones we will not use would hold their ports against other
    /// applications for the life of the process.
    /// </summary>
    private OutputDevice? Choose(ICollection<OutputDevice> devices)
    {
        OutputDevice? first = null;
        OutputDevice? preferred = null;

        foreach (OutputDevice device in devices)
        {
            first ??= device;

            if (preferred is null
                && _preferredDeviceName is not null
                && string.Equals(device.Name, _preferredDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                preferred = device;
            }
        }

        OutputDevice? chosen = preferred ?? first;

        // A second pass, so the choice is settled before anything is disposed.
        foreach (OutputDevice device in devices)
        {
            if (!ReferenceEquals(device, chosen))
            {
                device.Dispose();
            }
        }

        return chosen;
    }
}

/// <summary>
/// Hands back the engine that suits the machine: the real one when there is a device, the null one
/// when there is not - and either way it says why.
/// </summary>
public static class PlaybackEngineFactory
{
    /// <summary>
    /// Creates an engine. Never returns null and never throws for want of a device.
    /// </summary>
    /// <param name="probe">
    /// How to find a device. Defaults to <see cref="OutputDeviceProbe"/>; tests pass a fake.
    /// </param>
    /// <param name="settings">
    /// DryWetMIDI playback settings, chiefly the clock's tick generator. Defaults to DryWetMIDI's
    /// own, which on Windows and macOS is the high-precision native timer.
    /// </param>
    public static IPlaybackEngine Create(
        IMidiDeviceProbe? probe = null,
        PlaybackSettings? settings = null)
    {
        MidiDeviceProbeResult result = (probe ?? new OutputDeviceProbe()).Probe();

        if (result.Device is null)
        {
            return new NullPlaybackEngine(result.Reason);
        }

        return new DryWetMidiPlaybackEngine(
            result.Device,
            result.DeviceName ?? "MIDI output",
            result.Reason,
            ownsDevice: true,
            settings);
    }
}

/// <summary>
/// The real engine: two DryWetMIDI <c>Playback</c> instances over one output device, exactly one
/// started at a time.
/// </summary>
/// <remarks>
/// <para>
/// This is the only platform-bound type in the project. All of the switch logic lives in
/// <see cref="AbSwitcher"/> behind <see cref="ISequencePlayer"/> and <see cref="IMidiSink"/>, so
/// what is left here is construction, two thin adapters, and a timer - little enough that the part
/// no test can reach without a real synth is obviously trivial.
/// </para>
/// <para>
/// <b>Playhead notifications are raised on a background thread</b> - a timer thread, and
/// DryWetMIDI's own playback thread - and are rate-limited to ~60 a second by
/// <see cref="PlayheadThrottle"/>. Consumers must marshal to their own UI thread;
/// <c>MidiRestyle.Playback</c> has no UI dependency and will not do it for them.
/// </para>
/// </remarks>
public sealed class DryWetMidiPlaybackEngine : IPlaybackEngine
{
    private readonly IOutputDevice _device;
    private readonly bool _ownsDevice;
    private readonly PlaybackSettings? _settings;
    private readonly DeviceSink _sink;
    private readonly PlayheadThrottle _throttle;
    private readonly Timer _timer;
    private readonly Lock _gate = new();

    private AbSwitcher? _switcher;
    private PlaybackSide _pendingSide = PlaybackSide.Original;
    private bool _disposed;

    /// <summary>Creates an engine over an already-opened device.</summary>
    /// <param name="device">
    /// Where MIDI goes. Typed as DryWetMIDI's own <see cref="IOutputDevice"/> so a test can hand in
    /// a recorder and drive real <c>Playback</c> instances on a machine with no synth.
    /// </param>
    /// <param name="deviceName">The device's name, for display.</param>
    /// <param name="reason">
    /// The user-facing sentence for <see cref="Reason"/>. Blank falls back to naming the device.
    /// </param>
    /// <param name="ownsDevice">
    /// Whether <see cref="Dispose"/> should dispose the device. True when the factory opened it;
    /// false when a caller - a test, or a host sharing one device - owns its lifetime.
    /// </param>
    /// <param name="settings">DryWetMIDI playback settings, chiefly the clock's tick generator.</param>
    public DryWetMidiPlaybackEngine(
        IOutputDevice device,
        string deviceName,
        string? reason = null,
        bool ownsDevice = true,
        PlaybackSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);

        _device = device;
        _ownsDevice = ownsDevice;
        _settings = settings;
        _sink = new DeviceSink(device);
        _throttle = new PlayheadThrottle();

        DeviceName = deviceName;
        Reason = string.IsNullOrWhiteSpace(reason) ? $"Playing through '{deviceName}'." : reason;

        // One timer for the life of the engine, ticking a little faster than 60 Hz so the throttle
        // rather than the timer sets the rate. It runs whether or not anything is playing; the
        // callback is a couple of field reads when it is not, which is cheaper than starting and
        // stopping a timer on every transport command.
        _timer = new Timer(_ => RaisePlayhead(force: false), null, PollInterval, PollInterval);
    }

    private static TimeSpan PollInterval => TimeSpan.FromMilliseconds(10);

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public string Reason { get; }

    /// <inheritdoc />
    public string? DeviceName { get; }

    /// <inheritdoc />
    public bool IsLoaded
    {
        get
        {
            lock (_gate)
            {
                return _switcher is not null;
            }
        }
    }

    /// <inheritdoc />
    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _switcher?.IsPlaying ?? false;
            }
        }
    }

    /// <inheritdoc />
    public PlaybackSide ActiveSide
    {
        get
        {
            lock (_gate)
            {
                return _switcher?.ActiveSide ?? _pendingSide;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                return _switcher?.Position ?? TimeSpan.Zero;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                return _switcher?.Duration ?? TimeSpan.Zero;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<int> StopChannels
    {
        get
        {
            lock (_gate)
            {
                return _switcher?.StopChannels ?? [];
            }
        }
    }

    /// <summary>
    /// How long the last A/B switch took, wall clock. The target is under 30 ms.
    /// </summary>
    public TimeSpan LastSwitchGap
    {
        get
        {
            lock (_gate)
            {
                return _switcher?.LastSwitchGap ?? TimeSpan.Zero;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<PlayheadEventArgs>? PlayheadMoved;

    /// <inheritdoc />
    public void Load(PlaybackSequences sequences)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        ObjectDisposedException.ThrowIf(_disposed, this);

        MidiFile original = Read(sequences.Original, nameof(sequences.Original));
        MidiFile restyled = Read(sequences.Restyled, nameof(sequences.Restyled));

        // Every channel the stop sequence must reach. The allocated pitch-bend channels are the
        // ones the invariant names, but a note hanging on an untouched track's channel is just as
        // stuck, so the union of both sides' channels is used.
        IReadOnlyList<int> stopChannels =
        [
            .. sequences.RestyledChannels
                .Concat(ChannelsUsedBy(original))
                .Concat(ChannelsUsedBy(restyled))
                .Distinct()
                .Order(),
        ];

        lock (_gate)
        {
            TimeSpan resumeAt = _switcher?.Position ?? TimeSpan.Zero;
            PlaybackSide side = _switcher?.ActiveSide ?? _pendingSide;

            _switcher?.Dispose();
            _switcher = null;

            // Each allocated channel and the bend it holds, so a seek can put the tuning back.
            // Without this the restyled side plays 12-TET from the seek point on any synth whose
            // current RPN is not 0/0 - see AbSwitcher.Retune.
            (int Channel, double BendCents)[] tuning = sequences.Allocation is null
                ? []
                : [.. sequences.Allocation.Channels.Select(c => (c.OutputChannel, c.BendCents))];

            AbSwitcher switcher = new(
                CreatePlayer(original),
                CreatePlayer(restyled),
                _sink,
                stopChannels,
                tuning);

            switcher.SwitchTo(side);
            switcher.Seek(resumeAt);

            _switcher = switcher;
            _pendingSide = side;
        }

        // Opens the port and warms the driver, so the first note is not late.
        _device.PrepareForEventsSending();

        RaisePlayhead(force: true);
    }

    /// <inheritdoc />
    public void Play()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _switcher?.Start();
        }

        RaisePlayhead(force: true);
    }

    /// <inheritdoc />
    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _switcher?.Pause();
        }

        RaisePlayhead(force: true);
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _switcher?.Stop();
        }

        RaisePlayhead(force: true);
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _switcher?.Seek(position);
        }

        RaisePlayhead(force: true);
    }

    /// <inheritdoc />
    public void SwitchTo(PlaybackSide side)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingSide = side;
            _switcher?.SwitchTo(side);
        }

        RaisePlayhead(force: true);
    }

    /// <inheritdoc />
    public PlaybackSide Toggle()
    {
        PlaybackSide side;

        lock (_gate)
        {
            if (_disposed)
            {
                return _pendingSide;
            }

            side = _switcher is null
                ? Other(_pendingSide)
                : _switcher.Toggle();

            _pendingSide = side;
        }

        RaisePlayhead(force: true);
        return side;
    }

    /// <summary>
    /// Discards the loaded sequences, silencing every channel first.
    /// </summary>
    /// <remarks>
    /// Disposing the switcher sends the stop sequence, so nothing is left sounding or detuned. Done
    /// outside the lock for that reason - it touches the device. The device itself is kept: it is
    /// this engine's for its lifetime, and reopening a port per file would be both slow and rude to
    /// other applications.
    /// </remarks>
    public void Unload()
    {
        AbSwitcher? switcher;

        lock (_gate)
        {
            switcher = _switcher;
            _switcher = null;
            _pendingSide = PlaybackSide.Original;
        }

        switcher?.Dispose();
    }

    private static PlaybackSide Other(PlaybackSide side) =>
        side == PlaybackSide.Original ? PlaybackSide.Restyled : PlaybackSide.Original;

    /// <summary>
    /// Releases the two <c>Playback</c> objects and, when it owns it, the device. Both are
    /// unmanaged-backed: a leaked <see cref="OutputDevice"/> holds the port against every other
    /// application on the machine. Safe to call twice.
    /// </summary>
    public void Dispose()
    {
        AbSwitcher? switcher;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            switcher = _switcher;
            _switcher = null;
        }

        _timer.Dispose();

        // Outside the lock: disposing the switcher sends the stop sequence, which touches the device.
        switcher?.Dispose();

        if (_ownsDevice && _device is IDisposable disposable)
        {
            disposable.Dispose();
        }

        PlayheadMoved = null;
    }

    // ---- playhead -----------------------------------------------------------------------------

    /// <summary>
    /// Raises <see cref="PlayheadMoved"/> if the throttle allows, or unconditionally when
    /// <paramref name="force"/> is set - which is used after a transport command, so the listener
    /// settles on the real position rather than on whatever the last throttled tick reported.
    /// </summary>
    private void RaisePlayhead(bool force)
    {
        EventHandler<PlayheadEventArgs>? handler = PlayheadMoved;

        if (handler is null || _disposed)
        {
            return;
        }

        if (!force && !_throttle.TryEmit())
        {
            return;
        }

        PlayheadEventArgs args;

        lock (_gate)
        {
            if (_switcher is null)
            {
                return;
            }

            args = new PlayheadEventArgs(_switcher.Position, _switcher.Duration, _switcher.ActiveSide);
        }

        handler(this, args);
    }

    // ---- construction -------------------------------------------------------------------------

    private static MidiFile Read(byte[] bytes, string what)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using MemoryStream stream = new(bytes, writable: false);

        try
        {
            return MidiFile.Read(stream);
        }
        catch (MidiException ex)
        {
            throw new InvalidOperationException(
                $"The {what} playback sequence could not be read back: {ex.Message}. It came from "
                + "MidiFileExporter, so this is a bug rather than bad user input.",
                ex);
        }
    }

    /// <summary>
    /// Builds one <c>Playback</c> and wraps it in the <see cref="ISequencePlayer"/> seam.
    /// </summary>
    /// <remarks>
    /// The four <c>Track*</c> flags are set explicitly rather than left to DryWetMIDI's defaults,
    /// because the A/B switch depends on them. <c>TrackPitchValue</c> is the load-bearing one: after
    /// the switch sends its bend reset and seeks, it is what re-sends the pitch bend that should be
    /// in effect at the new position, so the arriving side is in tune from its first note.
    /// <c>TrackNotes</c> keeps a note that straddles the switch point sounding, so A/B compares the
    /// same instant rather than restarting the phrase.
    /// </remarks>
    private ISequencePlayer CreatePlayer(MidiFile file)
    {
        DwmPlayback playback = file.GetPlayback(_device, _settings);

        playback.InterruptNotesOnStop = true;
        playback.TrackNotes = true;
        playback.TrackProgram = true;
        playback.TrackControlValue = true;
        playback.TrackPitchValue = true;

        return new DryWetMidiSequencePlayer(playback, OnEventPlayed);
    }

    /// <summary>
    /// DryWetMIDI raises this per MIDI event, on its own thread. It exists only to keep the playhead
    /// moving between timer ticks, and every call goes through the throttle - a dense file is tens of
    /// thousands of events and notifying per event is exactly the mistake the throttle prevents.
    /// </summary>
    private void OnEventPlayed(object? sender, MidiEventPlayedEventArgs e) => RaisePlayhead(force: false);

    /// <summary>Every channel that carries a channel-voice event in this file.</summary>
    private static IEnumerable<int> ChannelsUsedBy(MidiFile file)
    {
        HashSet<int> channels = [];

        foreach (TrackChunk chunk in file.GetTrackChunks())
        {
            foreach (MidiEvent midiEvent in chunk.Events)
            {
                if (midiEvent is DwmChannelEvent channelEvent)
                {
                    channels.Add(channelEvent.Channel);
                }
            }
        }

        return channels;
    }

    // ---- adapters -----------------------------------------------------------------------------

    /// <summary>
    /// Translates a domain <see cref="DomainChannelEvent"/> into DryWetMIDI's event types and sends
    /// it. This and <see cref="DryWetMidiSequencePlayer"/> are the whole of the platform-bound
    /// surface; everything else about the stop sequence is decided in
    /// <see cref="PitchBendEncoder"/> and sequenced by <see cref="AbSwitcher"/>.
    /// </summary>
    private sealed class DeviceSink(IOutputDevice device) : IMidiSink
    {
        public void Send(DomainChannelEvent channelEvent)
        {
            FourBitNumber channel = (FourBitNumber)channelEvent.Channel;

            MidiEvent midiEvent = channelEvent.Kind switch
            {
                ChannelEventKind.ControlChange => new ControlChangeEvent(
                    (SevenBitNumber)channelEvent.Data1,
                    (SevenBitNumber)channelEvent.Data2) { Channel = channel },
                ChannelEventKind.ProgramChange => new ProgramChangeEvent(
                    (SevenBitNumber)channelEvent.Data1) { Channel = channel },
                ChannelEventKind.PitchBend => new PitchBendEvent(
                    (ushort)((channelEvent.Data2 * 128) + channelEvent.Data1)) { Channel = channel },
                ChannelEventKind.ChannelPressure => new ChannelAftertouchEvent(
                    (SevenBitNumber)channelEvent.Data1) { Channel = channel },
                _ => throw new NotSupportedException(
                    $"Unhandled channel event kind {channelEvent.Kind}."),
            };

            device.SendEvent(midiEvent);
        }
    }

    /// <summary>
    /// The <see cref="ISequencePlayer"/> adapter over DryWetMIDI's <c>Playback</c>. Five members,
    /// each a one-line forward, plus the metric-time conversion DryWetMIDI's generic time API needs.
    /// </summary>
    private sealed class DryWetMidiSequencePlayer : ISequencePlayer
    {
        private readonly DwmPlayback _playback;
        private readonly EventHandler<MidiEventPlayedEventArgs> _onEventPlayed;
        private bool _disposed;

        public DryWetMidiSequencePlayer(
            DwmPlayback playback,
            EventHandler<MidiEventPlayedEventArgs> onEventPlayed)
        {
            _playback = playback;
            _onEventPlayed = onEventPlayed;
            _playback.EventPlayed += onEventPlayed;
        }

        public bool IsRunning => !_disposed && _playback.IsRunning;

        public TimeSpan Duration => _playback.GetDuration<MetricTimeSpan>();

        public TimeSpan CurrentTime => _playback.GetCurrentTime<MetricTimeSpan>();

        public void MoveToTime(TimeSpan time) => _playback.MoveToTime(new MetricTimeSpan(time));

        public void Start() => _playback.Start();

        public void Stop() => _playback.Stop();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _playback.EventPlayed -= _onEventPlayed;
            _playback.Dispose();
        }
    }
}
