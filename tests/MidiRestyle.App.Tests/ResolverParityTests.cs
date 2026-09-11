using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Scales;
using MidiRestyle.Mcp;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The GUI's <see cref="StylePanelViewModel.BuildSettings"/> and the MCP
/// <see cref="RestyleRequestResolver"/> are two builders of one <see cref="RestyleSettings"/>.
/// </summary>
/// <remarks>
/// <para>
/// Given the same file, scale and detection they must agree on every field the engine reads. They
/// are separate code paths by necessity - one reads a view model the user has been clicking on, the
/// other a JSON request - so nothing but a test holds them together, and a divergence would not
/// show up as a crash. It would show up as an agent quietly producing a different file from the one
/// the user hears when they press Play, which is the single failure this whole MCP surface has to
/// not have.
/// </para>
/// <para>
/// Asserted field by field rather than by comparing the two <c>RestyleSettings</c> whole: a record
/// equality check would pass or fail as one opaque boolean, and <see cref="RestyleSettings"/> is
/// not a record anyway. Field by field, a failure names the field that drifted.
/// </para>
/// <para>
/// <b>Every case here uses the default <c>ScaleDegree</c> strategy, and a <c>NearestPitch</c> leg
/// would fail as written.</b> Under <c>NearestPitch</c> the resolver never looks the source scale up
/// at all - <c>usesSource</c> gates the whole resolution - so it carries a null <c>SourceScale</c>
/// and a default <c>SourceTonic</c>, while the panel passes through whatever the detected key
/// seeded, since it only dims those controls. The difference is inert: the <c>NearestPitch</c> arm
/// of the mapper reads neither field. Left alone deliberately rather than forced into agreement,
/// because making the resolver resolve a source it does not use would make key detection mandatory
/// for requests that succeed without it today. Recorded here so the next person to add the case
/// knows the failure is expected, and why it is not a bug.
/// </para>
/// </remarks>
public sealed class ResolverParityTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "midirestyle-parity-" + Guid.NewGuid().ToString("N"));

    public ResolverParityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file left open by a failed assertion must not turn one red test into two - and on
            // Windows a locked or read-only file raises UnauthorizedAccessException, which is not
            // an IOException, so catching only that defeated the purpose.
        }
    }

    /// <summary>
    /// An empty request - nothing but a file and a target scale - must equal the panel as it stands
    /// after the user accepts the detected key, which is the GUI's own default path.
    /// </summary>
    /// <param name="fixture">
    /// Three fixtures, each present because a mutation survived without it. <see cref="Fixture.CMajor"/>
    /// alone let a resolver that hardcodes the major source scale pass, since the major and minor arms
    /// of <c>SourceScaleIdFor</c> are never told apart by a major-only file - <see cref="Fixture.AMinor"/>
    /// separates them. And C major's tonic is pitch class 0, which every spelling convention agrees on,
    /// so a sharp-preferring <c>TonicParser.FromDetected</c> was invisible; <see cref="Fixture.EFlatMajor"/>
    /// detects to a black key, where flat-preferring and sharp-preferring disagree.
    /// </param>
    /// <param name="tolerance">
    /// Null exercises the defaulted path on both sides; 0.5 proves the supplied value is carried
    /// rather than the default being echoed back, which would pass a null-only test.
    /// </param>
    [Theory]
    [InlineData(Fixture.CMajor, null)]
    [InlineData(Fixture.CMajor, 0.5)]
    [InlineData(Fixture.AMinor, null)]
    [InlineData(Fixture.EFlatMajor, null)]
    public void AnEmptyRequestEqualsThePanelAfterApplyDetectedKey(Fixture fixture, double? tolerance)
    {
        PathProbe probe = new(
            Path.Combine(_root, "beside"), Path.Combine(_root, "appdata"), Path.Combine(_root, "data"));
        ScaleLibrary library = new ScaleLibraryLoader(probe).Load().Library;
        string input = MidiFixtures.Write(Path.Combine(_root, $"{fixture}.mid"), NotesFor(fixture));

        Scale rast = library.Find("middleeast.arabic.maqam-rast")
            ?? throw new InvalidOperationException("the Rast fixture scale is missing from the library");

        // GUI side: exactly what the panel holds once the user accepts the suggested key.
        MidiProject project = MidiFileLoader.Load(input);
        StylePanelViewModel panel = new(library) { SelectedScale = rast };
        panel.ApplyDetectedKey(KeyDetector.Detect(project));

        if (tolerance is { } t)
        {
            panel.ToleranceCents = t;
        }

        RestyleSettings gui = panel.BuildSettings();

        // MCP side: the same two choices and nothing else.
        RestyleRequest request = new(
            input, rast.Id, null, null, null, null, null, null, null, null, tolerance, null, false);

        new RestyleRequestResolver(library)
            .TryResolve(request, out Resolution? resolution, out string? error)
            .Should().BeTrue(error);

        RestyleSettings mcp = resolution!.Settings;

        // Scales compared by id: the library hands out the same instances here, so reference
        // equality would pass without proving the resolver looked anything up.
        mcp.TargetScale.Id.Should().Be(gui.TargetScale.Id);
        mcp.TargetTonic.Should().Be(gui.TargetTonic);
        mcp.TonicSpelling.Should().Be(gui.TonicSpelling);
        mcp.SourceScale!.Id.Should().Be(gui.SourceScale!.Id);
        mcp.SourceTonic.Should().Be(gui.SourceTonic);
        mcp.Mapping.Should().Be(gui.Mapping);
        mcp.ToleranceCents.Should().Be(gui.ToleranceCents);
        mcp.Excluded.Should().BeEquivalentTo(gui.Excluded);

        // The ninth field, which the brief's list omitted. RestyleRequest carries no output mode, so
        // the two agree here only because neither side is asked to change it - parity by shared
        // default, not by construction. Pinned anyway: if either default moves, the MCP path would
        // start writing files in a mode the user never chose, and nothing else would notice.
        mcp.OutputMode.Should().Be(gui.OutputMode);
    }

    /// <summary>Which synthesised file a theory case runs against.</summary>
    public enum Fixture
    {
        /// <summary>Tonic-heavy C major: detects major, tonic pitch class 0.</summary>
        CMajor,

        /// <summary>Tonic-heavy A minor: detects MINOR, so the source-scale arms are told apart.</summary>
        AMinor,

        /// <summary>C major moved up three semitones: detects a BLACK-KEY tonic, pitch class 3.</summary>
        EFlatMajor,
    }

    /// <summary>The notes for <paramref name="fixture"/>.</summary>
    /// <remarks>
    /// E flat major is transposed from the C major fixture rather than added to <c>MidiFixtures</c>,
    /// because that file is a copy of the one in <c>MidiRestyle.Mcp.Tests</c> and the two silently
    /// disagreeing about what "the same file" means is the single thing this test exists to rule out.
    /// Transposing a whole scale by a constant preserves its intervals, so a detector that finds C
    /// major in the one finds E flat major in the other.
    /// </remarks>
    private static (int Note, int Channel)[] NotesFor(Fixture fixture) => fixture switch
    {
        Fixture.CMajor => MidiFixtures.CMajorNotes,
        Fixture.AMinor => MidiFixtures.AMinorNotes,
        Fixture.EFlatMajor => [.. MidiFixtures.CMajorNotes.Select(n => (n.Note + 3, n.Channel))],
        _ => throw new ArgumentOutOfRangeException(nameof(fixture)),
    };

    /// <summary>
    /// An exclusion set by the user reaches the engine identically down both paths.
    /// </summary>
    /// <remarks>
    /// Kept out of the theory above because the two paths populate <c>Excluded</c> from entirely
    /// different sources - the GUI from its track list, straight into <c>BuildSettings</c>; the MCP
    /// from <c>request.Exclude</c>, through a validation pass the GUI has no equivalent of. Comparing
    /// two empty sets, which is all the theory does, proves nothing about the <c>(Track, Channel)</c>
    /// key agreeing. It has to agree: it is what <c>ShouldRestyle</c> matches on, and the loader's
    /// Format-0 per-channel split makes the track index less obvious than it looks.
    /// </remarks>
    [Fact]
    public void AnExcludedTrackChannelReachesTheEngineTheSameWayDownBothPaths()
    {
        PathProbe probe = new(
            Path.Combine(_root, "beside"), Path.Combine(_root, "appdata"), Path.Combine(_root, "data"));
        ScaleLibrary library = new ScaleLibraryLoader(probe).Load().Library;
        string input = MidiFixtures.Write(Path.Combine(_root, "excluded.mid"), MidiFixtures.CMajorNotes);

        Scale rast = library.Find("middleeast.arabic.maqam-rast")
            ?? throw new InvalidOperationException("the Rast fixture scale is missing from the library");

        MidiProject project = MidiFileLoader.Load(input);

        // Taken from the file rather than assumed, so the test cannot pass by excluding something
        // that is not there - the resolver rejects an unknown track-channel outright.
        TrackInfo target = project.Tracks[0];

        StylePanelViewModel panel = new(library) { SelectedScale = rast };
        panel.ApplyDetectedKey(KeyDetector.Detect(project));
        RestyleSettings gui = panel.BuildSettings(
            new HashSet<(int Track, int Channel)> { (target.TrackIndex, target.Channel) });

        RestyleRequest request = new(
            input, rast.Id, null, null, null,
            [new TrackChannelRef(target.TrackIndex, target.Channel)],
            null, null, null, null, null, null, false);

        new RestyleRequestResolver(library)
            .TryResolve(request, out Resolution? resolution, out string? error)
            .Should().BeTrue(error);

        resolution!.Settings.Excluded.Should().BeEquivalentTo(gui.Excluded);
        gui.Excluded.Should().ContainSingle("the fixture has one track-channel and it was excluded");
    }
}
