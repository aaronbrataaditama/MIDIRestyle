using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Output;

namespace MidiRestyle.Core.Restyle;

/// <summary>
/// Defaults shared by every builder of <see cref="Model.RestyleSettings"/> - the GUI's style panel
/// and the MCP server's request resolver. They live in Core so the two cannot drift: a setting the
/// GUI can express must be reproducible headlessly, and vice versa.
/// </summary>
public static class RestyleDefaults
{
    /// <summary>The tonic octave that puts pitch class 0 at middle C, MIDI 60.</summary>
    public const int TonicOctave = 4;

    /// <summary>Bend-grouping tolerance the channel allocator starts from.</summary>
    public const double ToleranceCents = OffsetClusterer.DefaultToleranceCents;

    /// <summary>Narrowest useful tolerance. Below this, rounding artefacts buy channels.</summary>
    public const double MinToleranceCents = 0.5;

    /// <summary>Widest tolerance the escalation ladder ever reaches - half a semitone.</summary>
    public const double MaxToleranceCents = 50.0;

    /// <summary>Library id seeded as the source scale for a detected major key.</summary>
    public const string MajorSourceScaleId = "europe.churchmodes.ionian";

    /// <summary>Library id seeded as the source scale for a detected minor key.</summary>
    public const string MinorSourceScaleId = "europe.churchmodes.aeolian";

    /// <summary>
    /// The candidate a builder adopts from key detection: the top-ranked one, even when the result is
    /// ambiguous. Substituting nothing for "we could not tell" is the caller's decision, made on
    /// <see cref="KeyDetectionResult.HasKey"/>, not here.
    /// </summary>
    public static KeyEstimate? ChosenCandidate(KeyDetectionResult? detection) => detection?.TopCandidate;

    public static string SourceScaleIdFor(KeyEstimate key) =>
        key.IsMinor ? MinorSourceScaleId : MajorSourceScaleId;
}
