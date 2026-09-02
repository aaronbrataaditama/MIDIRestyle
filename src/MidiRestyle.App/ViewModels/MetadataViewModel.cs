using MidiRestyle.Core.Model;
using MidiRestyle.Core.Notation;

namespace MidiRestyle.App.ViewModels;

/// <summary>
/// The file-metadata header: what this file is, before anything is done to it.
/// </summary>
public sealed class MetadataViewModel(MidiProject project)
{
    private readonly MidiProject _project = project
        ?? throw new ArgumentNullException(nameof(project));

    public string? FileName =>
        _project.FilePath is null ? null : Path.GetFileName(_project.FilePath);

    public string? Title => _project.Title;

    public string FormatText => _project.Format switch
    {
        MidiFileFormatKind.SingleTrack => "Format 0 (single track)",
        MidiFileFormatKind.MultiTrack => "Format 1 (multi-track)",
        MidiFileFormatKind.MultiSequence => "Format 2 (independent sequences)",
        _ => "Unknown format",
    };

    /// <summary>
    /// The timebase. Reads "480 PPQN" or "SMPTE 25 fps, 40 ticks/frame" - never a fabricated PPQN
    /// for a SMPTE file, which genuinely does not have one.
    /// </summary>
    public string DivisionText => _project.Division.Describe();

    public int TrackChannelCount => _project.Tracks.Count;

    public int NoteCount => _project.TotalNoteCount;

    /// <summary>
    /// The barlines this file is read on, built once. Shared with the transport readout so the bar
    /// the status line counts up to is the same one the metadata pane names.
    /// </summary>
    /// <remarks>
    /// Empty for a SMPTE file: no PPQN means no notated beat, so a bar number would be invented
    /// rather than read.
    /// </remarks>
    public IReadOnlyList<MeasureSpan> Measures => _measures ??= BuildMeasures();

    private IReadOnlyList<MeasureSpan>? _measures;

    private IReadOnlyList<MeasureSpan> BuildMeasures() =>
        _project.Division is TicksPerQuarterNote ppqn && ppqn.Ticks > 0
            ? MeasureGrid.Build(_project.TimeSignatures, _project.DurationTicks, ppqn.Ticks)
            : [];

    /// <summary>How many bars the piece runs to, or null when the timebase cannot say.</summary>
    public int? BarCount => Measures.Count == 0 ? null : Measures.Count;

    /// <summary>
    /// Duration as <c>m:ss.t</c> with the bar count beside it, or a stated absence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two belong on one line because they answer the same question in the two units anyone
    /// reading a MIDI file thinks in - wall clock and bars - and neither is much use alone: seconds
    /// say how long the preview takes, bars say where in the piece you are.
    /// </para>
    /// <para>
    /// Tenths rather than whole seconds because a short loop is routine here, and <c>0:14</c> and
    /// <c>0:14.4</c> are meaningfully different lengths when the file is eight bars long. Both are
    /// null for SMPTE files, whose timebase is absolute rather than musical and which therefore have
    /// no tempo map to integrate and no notated bar to count.
    /// </para>
    /// </remarks>
    public string DurationText
    {
        get
        {
            if (_project.DurationSeconds is not { } seconds)
            {
                return "unknown (SMPTE timebase has no tempo map)";
            }

            TimeSpan span = TimeSpan.FromSeconds(seconds);
            string clock = span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds / 100}"
                : $"{span.Minutes}:{span.Seconds:00}.{span.Milliseconds / 100}";

            return BarCount is { } bars ? $"{clock} · {bars} bar{(bars == 1 ? "" : "s")}" : clock;
        }
    }

    public string TempoText
    {
        get
        {
            if (_project.TempoMap.Count == 0)
            {
                return "120 BPM (default - none stated in file)";
            }

            double first = _project.TempoMap[0].BeatsPerMinute;
            return _project.TempoMap.Count == 1
                ? $"{first:0.#} BPM"
                : $"{first:0.#} BPM, {_project.TempoMap.Count} changes";
        }
    }

    public string TimeSignatureText
    {
        get
        {
            if (_project.TimeSignatures.Count == 0)
            {
                return "4/4 (default - none stated in file)";
            }

            TimeSignatureChange first = _project.TimeSignatures[0];
            return _project.TimeSignatures.Count == 1
                ? first.ToString()
                : $"{first}, {_project.TimeSignatures.Count} changes";
        }
    }

    public bool HasMarkers => _project.Markers.Count > 0;

    public string MarkersText => $"{_project.Markers.Count} markers";

    /// <summary>
    /// Shown only for Format 2, where the file holds several independent sequences and the app is
    /// showing one of them. Saying nothing here would misrepresent the file.
    /// </summary>
    public string? SequenceNotice => _project.SequenceCount > 1
        ? $"This file holds {_project.SequenceCount} independent sequences. Showing the first."
        : null;

    public bool HasDrums => _project.HasDrums;

    /// <summary>Pitched track-channels: the figure the channel budget is measured against.</summary>
    public int PitchedTrackChannelCount => _project.PitchedTrackChannelCount;
}
