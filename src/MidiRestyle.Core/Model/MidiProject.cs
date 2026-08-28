using MidiRestyle.Core.Tuning;

namespace MidiRestyle.Core.Model;

/// <summary>
/// An immutable, loaded MIDI file. The source of truth that restyling never mutates.
/// </summary>
/// <remarks>
/// <para>
/// Restyling is a pure function of this plus <c>RestyleSettings</c>, producing a separate
/// <c>RestyleResult</c>. Both live in memory at once, which is what makes the piano-roll overlay and
/// A/B playback nearly free - and removes the need for an undo stack, since changing a setting just
/// re-runs the transform.
/// </para>
/// <para>
/// Contains no DryWetMIDI types by design: the library is an IO detail confined to
/// <c>MidiFileLoader</c> and <c>MidiFileExporter</c>, which keeps this model trivially constructible
/// in tests.
/// </para>
/// </remarks>
public sealed record MidiProject
{
    public string? FilePath { get; init; }

    public required MidiFileFormatKind Format { get; init; }

    public required TimeDivision Division { get; init; }

    /// <summary>
    /// Track-channel pairs, in load order. Format 0 files arrive here already split per channel.
    /// </summary>
    public required IReadOnlyList<TrackInfo> Tracks { get; init; }

    public IReadOnlyList<TempoChange> TempoMap { get; init; } = [];

    public IReadOnlyList<TimeSignatureChange> TimeSignatures { get; init; } = [];

    public IReadOnlyList<MarkerInfo> Markers { get; init; } = [];

    /// <summary>
    /// How many independent sequences the file holds. Always 1 except for Format 2, where the UI
    /// reports the count and opens the first.
    /// </summary>
    public int SequenceCount { get; init; } = 1;

    public string? Title { get; init; }

    /// <summary>Total length in ticks, taken from the longest track.</summary>
    public long DurationTicks => Tracks.Count == 0 ? 0 : Tracks.Max(t => t.EndTicks);

    /// <summary>Every track-channel that restyling may touch - drums and empties excluded.</summary>
    public IEnumerable<TrackInfo> RestylableTracks => Tracks.Where(t => t.IsRestylable);

    /// <summary>Pitched track-channel count. The figure the channel budget is measured against.</summary>
    public int PitchedTrackChannelCount => Tracks.Count(t => !t.IsDrums && t.NoteCount > 0);

    public int TotalNoteCount => Tracks.Sum(t => t.NoteCount);

    public bool HasDrums => Tracks.Any(t => t.IsDrums && t.NoteCount > 0);

    /// <summary>Any track-channel already carrying pitch bend, which microtonal output would fight.</summary>
    public IEnumerable<TrackInfo> TracksWithExistingPitchBend =>
        Tracks.Where(t => t.HasExistingPitchBend);

    public Pitch? LowestPitch =>
        Tracks.Select(t => t.LowestPitch).Where(p => p is not null).DefaultIfEmpty(null).Min();

    public Pitch? HighestPitch =>
        Tracks.Select(t => t.HighestPitch).Where(p => p is not null).DefaultIfEmpty(null).Max();

    /// <summary>
    /// Duration in seconds, from the tempo map. Null for SMPTE-timed files, whose timebase is
    /// absolute rather than musical and which therefore have no tempo map to integrate.
    /// </summary>
    public double? DurationSeconds
    {
        get
        {
            if (Division is not TicksPerQuarterNote ppqn || ppqn.Ticks <= 0)
            {
                return null;
            }

            // Integrate the tempo map piecewise: each segment runs at the tempo in force at its start.
            const int DefaultMicrosecondsPerQuarter = 500_000; // 120 BPM, the MIDI default
            long end = DurationTicks;
            double seconds = 0;
            long cursor = 0;
            int tempo = DefaultMicrosecondsPerQuarter;

            foreach (TempoChange change in TempoMap.OrderBy(t => t.Ticks))
            {
                if (change.Ticks >= end)
                {
                    break;
                }

                if (change.Ticks > cursor)
                {
                    seconds += (change.Ticks - cursor) / (double)ppqn.Ticks * tempo / 1_000_000.0;
                    cursor = change.Ticks;
                }

                tempo = change.MicrosecondsPerQuarterNote;
            }

            seconds += (end - cursor) / (double)ppqn.Ticks * tempo / 1_000_000.0;
            return seconds;
        }
    }
}
