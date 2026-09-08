namespace MidiRestyle.Mcp;

/// <summary>
/// One row of <see cref="MidiInspection.Tracks"/>: a track-channel pair, which is the unit
/// <c>restyle_midi</c>'s <c>exclude</c> parameter addresses and the unit the channel budget is spent
/// per. Format 0 files arrive already split, so every channel gets a row of its own with the same
/// <see cref="Track"/> index.
/// </summary>
/// <param name="Restylable">
/// Whether restyling will touch this row at all - <c>MidiRestyle.Core.Model.TrackInfo.IsRestylable</c>,
/// never re-derived here. False for percussion (channel 10, 0-indexed 9: a note number picks which
/// drum is struck, so remapping it changes the instrument) and for a row carrying no notes. Reported
/// explicitly so an agent choosing what to exclude does not have to know the drum rule, and so what it
/// reads here matches what <c>restyle_midi</c> will actually do.
/// </param>
public sealed record TrackSummary(
    int Track, int Channel, string? Name, string? Instrument, int NoteCount,
    bool IsDrums, bool Restylable, bool HasPitchBend, int? LowestMidi, int? HighestMidi);

/// <remarks>
/// Both numbers are carried for the reason <c>KeyEstimate</c> documents: <paramref name="R"/> is the
/// raw Krumhansl-Schmuckler correlation and a poor confidence signal on its own, while
/// <paramref name="Margin"/> - the gap to the runner-up - is the figure that says whether the answer
/// was actually determined.
/// </remarks>
public sealed record KeyCandidate(string Tonic, string Mode, double R, double Margin);

/// <summary>
/// What key detection concluded, and the source scale that implies. Always a shortlist, never a silent
/// decision: <see cref="SuggestedSourceScaleId"/> is a suggestion an agent may override by naming its
/// own <c>sourceScaleId</c> on <c>restyle_midi</c>.
/// </summary>
public sealed record KeyReport(
    string Outcome, IReadOnlyList<KeyCandidate> Candidates, bool IsAmbiguous, string? SuggestedSourceScaleId);

public sealed record TimeSignatureInfo(long Ticks, int Numerator, int Denominator);

/// <param name="TotalNotes">Every note in the file, drums included.</param>
/// <param name="RestylableNoteCount">
/// The notes a restyle would actually map - the sum over rows whose <see cref="TrackSummary.Restylable"/>
/// is true. The difference from <paramref name="TotalNotes"/> is percussion and silent rows.
/// </param>
public sealed record MidiInspection(
    string Path, string Format, string Division, long DurationTicks, double? DurationSeconds, string? Title,
    double? InitialTempoBpm, IReadOnlyList<TimeSignatureInfo> TimeSignatures,
    IReadOnlyList<TrackSummary> Tracks, int TotalNotes, int RestylableNoteCount, KeyReport Key);

public sealed record TallyReport(int DroppedOutOfRange, int DroppedNotInScale, int Merged, int Displaced);

public sealed record MutedTrack(int Track, int Channel, int NoteCount);

public sealed record ChannelReport(
    int Used, double EffectiveToleranceCents, bool ToleranceWasRaised, double WorstErrorCents, IReadOnlyList<MutedTrack> Muted);

public sealed record RestyleReport(
    string OutputPath, ResolvedSettings Resolved, int NotesRestyled, TallyReport Tally,
    ChannelReport Channels, FidelityInfo Fidelity, IReadOnlyList<string> Warnings);

public sealed record MusicXmlReport(
    string OutputPath, ResolvedSettings Resolved, int MeasureCount, IReadOnlyList<string> Parts,
    IReadOnlyList<string> Diagnostics, IReadOnlyList<string> Warnings);
