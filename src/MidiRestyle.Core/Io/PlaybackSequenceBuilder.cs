using MidiRestyle.Core.Model;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Restyle;

namespace MidiRestyle.Core.Io;

/// <summary>The two sides of the A/B comparison, as bytes.</summary>
/// <param name="Original">The file as loaded, with no pitch remapping.</param>
/// <param name="Restyled">The same file with the transform applied.</param>
/// <param name="Allocation">
/// The channel plan the restyled side uses, or null when it needs no pitch bend. The playback engine
/// needs it to know which channels to send the stop sequence to.
/// </param>
public sealed record PlaybackSequences(
    byte[] Original,
    byte[] Restyled,
    ChannelAllocation? Allocation)
{
    /// <summary>
    /// The channels the restyled side touches, which is where CC123 and the bend reset must go.
    /// </summary>
    /// <remarks>
    /// Sending the stop sequence to only some allocated channels leaves notes hanging on the rest,
    /// and leaves a stale bend that detunes whatever plays there next.
    /// </remarks>
    public IReadOnlyList<int> RestyledChannels => Allocation is null
        ? []
        : [.. Allocation.Channels.Select(c => c.OutputChannel).Distinct().Order()];
}

/// <summary>
/// Builds the byte streams the playback engine plays, from the same exporter that writes files.
/// </summary>
/// <remarks>
/// <para>
/// <b>Preview plays the exported bytes.</b> Both sides of A/B go through
/// <see cref="MidiFileExporter"/> - the same code, the same channel allocation, the same ceiling -
/// so "what you heard is what you exported" is true <em>by construction</em> rather than being a
/// property we hope holds and write a test for. Any future divergence would require someone to
/// deliberately add a second path, which is exactly the bug the single-allocator design exists to
/// make hard.
/// </para>
/// <para>
/// The original side is built by restyling with **every track excluded**, rather than by re-reading
/// the file from disk. That guarantees both sides share an identical tick grid, tempo map and track
/// layout, so an A/B switch can seek to the same tick on either and land in the same musical place.
/// Re-reading the source would usually work and would occasionally not - a Format 0 file, for
/// instance, is split per channel on load and would not round-trip to the same chunk layout.
/// </para>
/// </remarks>
public static class PlaybackSequenceBuilder
{
    /// <summary>
    /// Builds both sides for a restyle.
    /// </summary>
    /// <param name="restyled">The transform to preview.</param>
    /// <param name="ceiling">
    /// Physical channels available. Must be the same value export uses - passing a different one
    /// here is precisely the divergence this type exists to prevent.
    /// </param>
    public static PlaybackBuildResult Build(
        RestyleResult restyled,
        int ceiling = ChannelBudget.DefaultCeiling)
    {
        ArgumentNullException.ThrowIfNull(restyled);

        RestyleResult original = Identity(restyled.Source, restyled.Settings);

        // The untouched side never carries bend, so it takes the 12-TET path.
        if (!TryExport(original, allocation: null, out byte[] originalBytes, out string? failure))
        {
            return PlaybackBuildResult.Fail($"The original could not be prepared: {failure}");
        }

        bool needsBend = restyled.Tracks.Any(t =>
            t.WasRestyled && t.Notes.Any(n => !n.Pitch.IsTwelveTet));

        ChannelAllocation? allocation = needsBend
            ? ChannelAllocator.Allocate(restyled, ceiling)
            : null;

        if (!TryExport(restyled, allocation, out byte[] restyledBytes, out failure))
        {
            return PlaybackBuildResult.Fail($"The restyled version could not be prepared: {failure}");
        }

        return PlaybackBuildResult.Ok(
            new PlaybackSequences(originalBytes, restyledBytes, allocation));
    }

    /// <summary>
    /// Builds the original alone, for playing a file before any target scale has been chosen.
    /// </summary>
    /// <remarks>
    /// Both sides carry the same bytes, so the A/B toggle is harmless if it is ever reached - though
    /// the UI disables it, since there is genuinely nothing to compare. Wanting to hear the file you
    /// just opened is the first thing anyone does, and requiring a scale choice first would be a
    /// gate with no purpose.
    /// </remarks>
    public static PlaybackBuildResult BuildOriginalOnly(MidiProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        RestyleResult identity = RestyleEngine.Restyle(project, IdentitySettings(project));

        if (!TryExport(identity, allocation: null, out byte[] bytes, out string? failure))
        {
            return PlaybackBuildResult.Fail($"The file could not be prepared for playback: {failure}");
        }

        return PlaybackBuildResult.Ok(new PlaybackSequences(bytes, bytes, Allocation: null));
    }

    /// <summary>
    /// Settings that transform nothing.
    /// </summary>
    /// <remarks>
    /// A target scale is structurally required by <see cref="RestyleSettings"/> but is never applied,
    /// because every track is excluded. The placeholder is deliberately not a real library scale: it
    /// must never appear in the UI or in a status message, and using, say, C major would make a
    /// debugging session confusing.
    /// </remarks>
    private static RestyleSettings IdentitySettings(MidiProject project) => new()
    {
        TargetScale = new Scales.Scale(
            "internal.playback.identity",
            "(original)",
            "Internal",
            "Internal",
            [0, 200],
            "Internal placeholder, never applied and never shown"),
        TargetTonic = Tuning.Pitch.FromMidi(60),
        Excluded = new HashSet<(int Track, int Channel)>(
            project.Tracks.Select(t => (t.TrackIndex, t.Channel))),
    };

    /// <summary>
    /// The source, restyled with everything excluded - i.e. unchanged, but built by the same path.
    /// </summary>
    private static RestyleResult Identity(MidiProject project, RestyleSettings settings)
    {
        HashSet<(int Track, int Channel)> everything =
            [.. project.Tracks.Select(t => (t.TrackIndex, t.Channel))];

        return RestyleEngine.Restyle(project, settings with { Excluded = everything });
    }

    private static bool TryExport(
        RestyleResult result,
        ChannelAllocation? allocation,
        out byte[] bytes,
        out string? failure)
    {
        using MemoryStream stream = new();

        ExportResult export = allocation is null
            ? MidiFileExporter.Export(result, stream)
            : MidiFileExporter.Export(result, stream, allocation);

        if (!export.Success)
        {
            bytes = [];
            failure = export.Message;
            return false;
        }

        bytes = stream.ToArray();
        failure = null;
        return true;
    }
}

/// <summary>The outcome of preparing playback, with a stated reason on failure.</summary>
public sealed record PlaybackBuildResult
{
    private PlaybackBuildResult(PlaybackSequences? sequences, string? message)
    {
        Sequences = sequences;
        Message = message;
    }

    public PlaybackSequences? Sequences { get; }

    public string? Message { get; }

    public bool Success => Sequences is not null;

    public static PlaybackBuildResult Ok(PlaybackSequences sequences) => new(sequences, null);

    public static PlaybackBuildResult Fail(string message) => new(null, message);
}
