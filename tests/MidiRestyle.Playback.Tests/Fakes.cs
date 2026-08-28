using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Output;
using MidiRestyle.Playback;
using DomainChannelEvent = MidiRestyle.Core.Output.ChannelEvent;

namespace MidiRestyle.Playback.Tests;

/// <summary>
/// A sequence that records what was asked of it instead of making sound.
/// </summary>
/// <remarks>
/// Every A/B rule worth asserting - one side running, the playhead preserved, the stop sequence
/// before the start - is a statement about the <em>order and content</em> of calls, not about audio.
/// Recording them into a shared log makes those statements directly assertable on a machine with no
/// MIDI device, which is every build agent.
/// </remarks>
public sealed class FakeSequencePlayer : ISequencePlayer
{
    private readonly string _name;
    private readonly List<string> _log;

    public FakeSequencePlayer(string name, TimeSpan duration, List<string>? log = null)
    {
        _name = name;
        _log = log ?? [];
        Duration = duration;
    }

    public bool IsRunning { get; private set; }

    public TimeSpan Duration { get; }

    public TimeSpan CurrentTime { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int MoveCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool IsDisposed => DisposeCount > 0;

    public void MoveToTime(TimeSpan time)
    {
        CurrentTime = time;
        MoveCount++;
        _log.Add($"{_name}.MoveToTime({time.TotalMilliseconds:0.###})");
    }

    public void Start()
    {
        IsRunning = true;
        StartCount++;
        _log.Add($"{_name}.Start");
    }

    public void Stop()
    {
        IsRunning = false;
        StopCount++;
        _log.Add($"{_name}.Stop");
    }

    public void Dispose()
    {
        IsRunning = false;
        DisposeCount++;
        _log.Add($"{_name}.Dispose");
    }

    /// <summary>Simulates the clock running on, so a switch has a playhead worth preserving.</summary>
    public void Advance(TimeSpan by) => CurrentTime += by;
}

/// <summary>Records the domain channel events the switcher emits.</summary>
public sealed class RecordingMidiSink(List<string>? log = null) : IMidiSink
{
    private readonly List<string>? _log = log;

    public List<DomainChannelEvent> Events { get; } = [];

    public void Send(DomainChannelEvent channelEvent)
    {
        Events.Add(channelEvent);
        _log?.Add($"sink.{channelEvent.Kind}(ch{channelEvent.Channel},{channelEvent.Data1},{channelEvent.Data2})");
    }

    public void Clear() => Events.Clear();
}

/// <summary>
/// A MIDI output device that records instead of sounding.
/// </summary>
/// <remarks>
/// Typed as DryWetMIDI's own <see cref="IOutputDevice"/>, so it can be handed to a real
/// <c>Playback</c>. That is what lets the integration tests exercise the genuine DryWetMIDI
/// playback objects - clock, seeking, value tracking and all - without a synth anywhere in sight.
/// </remarks>
public sealed class RecordingOutputDevice : IOutputDevice, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<MidiEvent> _events = [];

    /// <summary>Required by the interface; never raised, because nothing under test listens.</summary>
    public event EventHandler<MidiEventSentEventArgs> EventSent
    {
        add => _ = value;
        remove => _ = value;
    }

    public int PrepareCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool IsDisposed => DisposeCount > 0;

    public IReadOnlyList<MidiEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public void PrepareForEventsSending()
    {
        lock (_gate)
        {
            PrepareCount++;
        }
    }

    public void SendEvent(MidiEvent midiEvent)
    {
        lock (_gate)
        {
            _events.Add(midiEvent);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }

    /// <summary>Channels that received CC123 (All Notes Off).</summary>
    public IReadOnlyList<int> ChannelsSentAllNotesOff() =>
    [
        .. Events.OfType<ControlChangeEvent>()
            .Where(e => e.ControlNumber == PitchBendEncoder.CcAllNotesOff)
            .Select(e => (int)e.Channel)
            .Distinct()
            .Order(),
    ];

    /// <summary>Channels that received a pitch bend back to centre.</summary>
    public IReadOnlyList<int> ChannelsSentBendReset() =>
    [
        .. Events.OfType<PitchBendEvent>()
            .Where(e => e.PitchValue == PitchBendEncoder.CenterBendValue)
            .Select(e => (int)e.Channel)
            .Distinct()
            .Order(),
    ];

    public void Dispose()
    {
        lock (_gate)
        {
            DisposeCount++;
        }
    }
}

/// <summary>A probe that reports whatever a test tells it to.</summary>
public sealed class FakeDeviceProbe(MidiDeviceProbeResult result) : IMidiDeviceProbe
{
    public int ProbeCount { get; private set; }

    public MidiDeviceProbeResult Probe()
    {
        ProbeCount++;
        return result;
    }
}

/// <summary>Builds the byte sequences the engine plays.</summary>
public static class MidiFixtures
{
    public const short TicksPerQuarterNote = 480;

    /// <summary>Half a second per quarter note, which is DryWetMIDI's default tempo.</summary>
    public static readonly TimeSpan QuarterNote = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// A file of <paramref name="quarterNotes"/> consecutive quarter notes on
    /// <paramref name="channel"/>, so its duration is predictable in wall-clock terms.
    /// </summary>
    public static MidiFile Notes(int channel, int quarterNotes, int firstNoteNumber = 60)
    {
        TrackChunk chunk = new();
        FourBitNumber ch = (FourBitNumber)channel;

        for (int i = 0; i < quarterNotes; i++)
        {
            SevenBitNumber note = (SevenBitNumber)(firstNoteNumber + (i % 12));

            chunk.Events.Add(new NoteOnEvent(note, (SevenBitNumber)100) { Channel = ch });
            chunk.Events.Add(new NoteOffEvent(note, SevenBitNumber.MinValue)
            {
                Channel = ch,
                DeltaTime = TicksPerQuarterNote,
            });
        }

        return new MidiFile(chunk)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(TicksPerQuarterNote),
        };
    }

    /// <summary>
    /// A deliberately event-dense file: the requested number of control changes packed into a
    /// fraction of a second, which is what makes "one notification per MIDI event" visible as a
    /// failure rather than a theoretical concern.
    /// </summary>
    public static MidiFile Dense(int channel, int events, int totalTicks)
    {
        TrackChunk chunk = new();
        FourBitNumber ch = (FourBitNumber)channel;
        long emitted = 0;

        for (int i = 0; i < events; i++)
        {
            long at = (long)((double)i / events * totalTicks);
            chunk.Events.Add(new ControlChangeEvent((SevenBitNumber)7, (SevenBitNumber)(i % 128))
            {
                Channel = ch,
                DeltaTime = at - emitted,
            });
            emitted = at;
        }

        return new MidiFile(chunk)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(TicksPerQuarterNote),
        };
    }

    public static byte[] ToBytes(MidiFile file)
    {
        using MemoryStream stream = new();
        file.Write(stream, MidiFileFormat.MultiTrack);
        return stream.ToArray();
    }

    /// <summary>
    /// A <see cref="PlaybackSequences"/> whose restyled side claims <paramref name="allocated"/> as
    /// its pitch-bend channels - the set the stop sequence must reach.
    /// </summary>
    public static PlaybackSequences Sequences(
        MidiFile original,
        MidiFile restyled,
        params int[] allocated) =>
        new(ToBytes(original), ToBytes(restyled), Allocation(allocated));

    /// <summary>A minimal channel allocation carrying just the channel numbers.</summary>
    public static ChannelAllocation? Allocation(params int[] channels)
    {
        if (channels.Length == 0)
        {
            return null;
        }

        List<AllocatedChannel> allocated =
        [
            .. channels.Select(c => new AllocatedChannel(
                OutputChannel: c,
                SourceTrackIndex: 0,
                SourceChannel: 0,
                BendCents: 0,
                Offsets: [0.0])),
        ];

        ChannelBudgetPlan budget = new(
            EffectiveToleranceCents: 5,
            ClustersPerTrackChannel: channels.Length,
            WorstErrorCents: 0,
            Muted: [],
            ChannelsUsed: channels.Length);

        return new ChannelAllocation(allocated, budget, []);
    }

    /// <summary>
    /// Playback settings using the managed tick generator.
    /// </summary>
    /// <remarks>
    /// DryWetMIDI's default clock is <c>HighPrecisionTickGenerator</c>, which P/Invokes the same
    /// native library that device enumeration needs - the one that does not exist on Linux. Tests
    /// must run wherever the build runs, so they take the managed timer instead. The shipping engine
    /// keeps the default.
    /// </remarks>
    public static PlaybackSettings ManagedClock() => new()
    {
        ClockSettings = new MidiClockSettings
        {
            CreateTickGeneratorCallback = () => new RegularPrecisionTickGenerator(),
        },
    };
}
