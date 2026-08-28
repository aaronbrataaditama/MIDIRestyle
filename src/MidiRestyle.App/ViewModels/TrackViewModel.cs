using CommunityToolkit.Mvvm.ComponentModel;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.ViewModels;

/// <summary>
/// One row of the track list: a single <c>(track, channel)</c> pair and whether it gets restyled.
/// </summary>
public sealed partial class TrackViewModel : ObservableObject
{
    private readonly TrackInfo _track;

    public TrackViewModel(TrackInfo track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _track = track;

        // Drums start excluded and cannot be included. Everything else starts included.
        _restyle = !track.IsDrums;
    }

    [ObservableProperty]
    private bool _restyle;

    public TrackInfo Track => _track;

    public int TrackIndex => _track.TrackIndex;

    /// <summary>1-based, because channel 10 is what musicians and every DAW call the drum channel.</summary>
    public int DisplayChannel => _track.Channel + 1;

    public string DisplayName => _track.DisplayName;

    public string? InstrumentName => _track.InstrumentName;

    public int NoteCount => _track.NoteCount;

    /// <summary>
    /// Whether the restyle checkbox is locked. True for drums, always.
    /// </summary>
    /// <remarks>
    /// Rendered as <em>locked</em> rather than merely unchecked. An unchecked box invites a click,
    /// and a click that silently does nothing is worse than a control that explains itself - hence
    /// <see cref="LockReason"/>.
    /// </remarks>
    public bool IsLocked => _track.IsDrums;

    /// <summary>Why the checkbox is locked, for the tooltip. Null when it is not.</summary>
    public string? LockReason => _track.IsDrums
        ? "Channel 10 is percussion. A note number here selects which drum is struck, not a pitch, "
          + "so remapping it would change the instrument rather than transpose it."
        : null;

    /// <summary>Whether this track will actually be transformed, given both the lock and the choice.</summary>
    public bool WillBeRestyled => !IsLocked && Restyle && _track.IsRestylable;

    /// <summary>Pitch range as note names, e.g. <c>C2 - G5</c>. Empty when the track has no notes.</summary>
    public string RangeText
    {
        get
        {
            if (_track.LowestPitch is not { } low || _track.HighestPitch is not { } high)
            {
                return string.Empty;
            }

            return $"{NoteName(low)} - {NoteName(high)}";
        }
    }

    /// <summary>Set when this track-channel already carries pitch bend, which microtonal output fights.</summary>
    public bool HasExistingPitchBend => _track.HasExistingPitchBend;

    public string? PitchBendWarning => _track.HasExistingPitchBend
        ? "This track already uses pitch bend. Microtonal output would conflict with it - "
          + "switch to 12-TET for this file, or expect the existing bends to be overridden."
        : null;

    private static readonly string[] NoteNames =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private static string NoteName(Pitch pitch)
    {
        // MIDI 0 is C-1 by the General MIDI convention, so the octave is note/12 - 1.
        int octave = (int)Math.Floor(pitch.MidiNote / (double)MidiRounding.SemitonesPerOctave) - 1;
        return $"{NoteNames[pitch.PitchClass]}{octave}";
    }
}
