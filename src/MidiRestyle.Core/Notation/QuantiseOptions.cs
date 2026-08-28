namespace MidiRestyle.Core.Notation;

/// <summary>How raw MIDI timing is snapped onto a readable rhythmic grid.</summary>
public sealed record QuantiseOptions
{
    public static QuantiseOptions Default { get; } = new();

    /// <summary>
    /// The shortest straight value onsets are snapped to. A sixteenth suits most material; raise it
    /// to a thirty-second for busy writing, lower it to an eighth to flatten performance jitter.
    /// </summary>
    public NoteValue Resolution { get; init; } = NoteValue.Sixteenth;

    /// <summary>
    /// Whether to consider triplet and sextuplet grids alongside the straight one. Off, triplet
    /// material is spelled as the nearest straight value - readable, but rhythmically a lie.
    /// </summary>
    public bool DetectTuplets { get; init; } = true;

    /// <summary>
    /// How much better a tuplet grid must fit before it is preferred. At 1.0 the two compete on
    /// equal terms and ordinary straight rhythm with a little human push-and-pull starts coming out
    /// as triplets; the default demands the tuplet reading be clearly better, not merely luckier.
    /// </summary>
    /// <remarks>
    /// This is the single knob that decides whether the quantiser is trigger-happy. It is a bias
    /// rather than a threshold because absolute tick error means nothing without a PPQN to scale it.
    /// </remarks>
    public double TupletBias { get; init; } = 2.0;

    /// <summary>
    /// How many <i>distinct</i> onsets a beat must contain before a tuplet grid is considered at
    /// all. Below this the beat is read straight whatever the error arithmetic says.
    /// </summary>
    /// <remarks>
    /// <see cref="TupletBias"/> cannot cover this case and no value of it can. The score is mean
    /// distance to the nearest grid line, and a finer grid has a smaller worst-case error <i>by
    /// construction</i> - at 480 PPQN the straight sixteenth grid is at most 60 ticks from any
    /// onset, the sextuplet grid at most 40. A beat with a single onset landing in the 40-80 tick
    /// band therefore beats any fixed bias, and a third of all single-onset beats were coming back
    /// as tuplets. The comparison is not merely mistuned there, it is answering the wrong question:
    /// a tuplet is a claim about how a beat is <i>divided</i>, and one onset divides nothing.
    /// <para>
    /// Three is the natural floor. Two onsets mark at most one internal division, which the
    /// straight grid already expresses; three is also the smallest group that can be
    /// <i>printed</i> as a tuplet, since a 3:2 bracket over one note is meaningless. The cost is
    /// that a lone pair of swung eighths reads straight - the right trade, because the pair is
    /// ambiguous by nature and a wrong tuplet is far more disfiguring than a wrong straight eighth.
    /// </para>
    /// </remarks>
    public int MinimumTupletOnsets { get; init; } = 3;

    /// <summary>
    /// Notes shorter than this fraction of a grid step are treated as grace-length artefacts and
    /// widened to a full step rather than vanishing.
    /// </summary>
    public double MinimumStepFraction { get; init; } = 0.5;
}
