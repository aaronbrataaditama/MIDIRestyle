using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The phase 9 seam: running the transform and publishing everything that depends on it.
/// </summary>
public class MainWindowRestyleTests
{
    private static readonly Scale CMajor = new(
        "t.cmajor", "C major", "Western", "Europe & Balkans",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    private static readonly Scale Gong = new(
        "t.gong", "Gong", "Chinese Wusheng", "East Asia",
        [0, 200, 400, 700, 900], "Test fixture, 2026");

    private static readonly Scale Slendro = new(
        "t.slendro", "Slendro", "Gamelan", "Southeast Asia",
        [0, 240, 480, 720, 960], "Test fixture, 2026", notatable: false);

    private static TrackInfo Track(int channel, int trackIndex, params int[] midiNotes) => new()
    {
        TrackIndex = trackIndex,
        Channel = channel,
        Notes = [.. midiNotes.Select((n, i) => new Note(Pitch.FromMidi(n), i * 480, 480, 90))],
    };

    private static MidiProject Project(params TrackInfo[] tracks) => new()
    {
        FilePath = @"C:\music\test.mid",
        Format = MidiFileFormatKind.MultiTrack,
        Division = new TicksPerQuarterNote(480),
        Tracks = tracks,
    };

    private static RestyleSettings Settings(Scale target) => new()
    {
        TargetScale = target,
        TargetTonic = Pitch.FromMidi(60),
        SourceScale = CMajor,
        SourceTonic = Pitch.FromMidi(60),
    };

    private static MainWindowViewModel Loaded(params TrackInfo[] tracks)
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project(tracks));
        return vm;
    }

    [Fact]
    public void NoRestyledNotesUntilATransformHasRun()
    {
        MainWindowViewModel vm = Loaded(Track(0, 0, 60, 62, 64));

        vm.RestyledRollNotes.Should().BeEmpty();
        vm.Restyle.Should().BeNull();
        vm.Allocation.Should().BeNull();
    }

    /// <summary>The phase 9 gate, in one assertion: change a scale, the notes move.</summary>
    [Fact]
    public void ApplyingARestylePublishesMovedNotes()
    {
        MainWindowViewModel vm = Loaded(Track(0, 0, 60, 62, 64, 65, 67, 69, 71, 72));
        double[] before = [.. vm.SourceRollNotes.Select(n => n.Cents)];

        vm.ApplyRestyle(Settings(Gong));

        vm.RestyledRollNotes.Should().HaveCount(before.Length);
        vm.RestyledRollNotes.Select(n => n.Cents).Should().NotEqual(before,
            "a 7-note source into a 5-note target must move something");
        vm.Restyle.Should().NotBeNull();
        vm.Allocation.Should().NotBeNull();
    }

    [Fact]
    public void GhostNotesAreUntouchedByARestyle()
    {
        MainWindowViewModel vm = Loaded(Track(0, 0, 60, 62, 64));
        double[] ghosts = [.. vm.SourceRollNotes.Select(n => n.Cents)];

        vm.ApplyRestyle(Settings(Slendro));

        vm.SourceRollNotes.Select(n => n.Cents).Should().Equal(ghosts,
            "the ghost layer is what makes the transform legible; it must not move");
    }

    [Fact]
    public void RestyledNotesAreSortedByStartTickForTheRoll()
    {
        MainWindowViewModel vm = Loaded(Track(0, 0, 60, 62, 64), Track(1, 1, 67, 69, 71));

        vm.ApplyRestyle(Settings(Slendro));

        vm.RestyledRollNotes.Select(n => n.StartTicks).Should().BeInAscendingOrder(
            "the roll's culling depends on start order");
    }

    [Fact]
    public void AMicrotonalTargetProducesMicrotonalRollNotes()
    {
        MainWindowViewModel vm = Loaded(Track(0, 0, 60, 62, 64, 65, 67));

        vm.ApplyRestyle(Settings(Slendro));

        vm.RestyledRollNotes.Should().Contain(n => n.Cents % 100 != 0,
            "Slendro's 240-cent steps cannot land on the semitone grid, and the roll must show that");
    }

    // --- what the status bar says --------------------------------------------------------

    [Fact]
    public void ACleanTransformReportsTheScaleAndChannelCountAndNothingElse()
    {
        MainWindowViewModel vm = Loaded(Track(0, 0, 60, 62, 64));

        vm.ApplyRestyle(Settings(Gong));

        vm.Status.Message.Should().Contain("Gong").And.Contain("1 channel");
        vm.Status.HasNotices.Should().BeFalse("nothing was compromised, so nothing is announced");
    }

    /// <summary>
    /// Four Slendro tracks want 20 channels against 15, so the tolerance rises - and the user is
    /// told, because a coarser tuning is invisible in the output itself.
    /// </summary>
    [Fact]
    public void RaisingTheToleranceIsAlwaysAnnounced()
    {
        MainWindowViewModel vm = Loaded(
            Track(0, 0, 60, 62), Track(1, 1, 64, 65), Track(2, 2, 67, 69), Track(3, 3, 71, 72));

        vm.ApplyRestyle(Settings(Slendro));

        vm.Allocation!.Budget.ToleranceWasRaised.Should().BeTrue();
        vm.Status.ToleranceNotice.Should().NotBeNullOrWhiteSpace();
        vm.Status.HasNotices.Should().BeTrue();
    }

    [Fact]
    public void MutedTrackChannelsAreNamedAndTheExportIsSaidToBeUnaffected()
    {
        TrackInfo[] many = [.. Enumerable.Range(0, 20).Select(i => Track(i % 9, i, 60, 62))];
        MainWindowViewModel vm = Loaded(many);

        vm.ApplyRestyle(Settings(Slendro));

        vm.Allocation!.Muted.Should().NotBeEmpty();
        vm.Status.MutedTracksNotice.Should().NotBeNullOrWhiteSpace();
        vm.Status.MutedTracksNotice.Should().Contain("export",
            "muting is a preview compromise only, and saying so prevents a false conclusion");
    }

    [Fact]
    public void ReRunningReplacesTheNoticesRatherThanAccumulatingThem()
    {
        MainWindowViewModel vm = Loaded(
            Track(0, 0, 60, 62), Track(1, 1, 64, 65), Track(2, 2, 67, 69), Track(3, 3, 71, 72));

        vm.ApplyRestyle(Settings(Slendro));
        vm.Status.HasNotices.Should().BeTrue();

        vm.ApplyRestyle(Settings(Gong));

        vm.Status.HasNotices.Should().BeFalse("the previous run's compromises no longer apply");
    }

    // --- lifecycle -----------------------------------------------------------------------

    [Fact]
    public void ClearingDropsTheRestyledLayerAndItsNotices()
    {
        MainWindowViewModel vm = Loaded(Track(0, 0, 60, 62, 64));
        vm.ApplyRestyle(Settings(Slendro));

        vm.ClearRestyle();

        vm.RestyledRollNotes.Should().BeEmpty();
        vm.Restyle.Should().BeNull();
        vm.Allocation.Should().BeNull();
        vm.Status.HasNotices.Should().BeFalse();
    }

    /// <summary>
    /// Leaving one file's restyled notes drawn over a different piece would be worse than showing
    /// none at all.
    /// </summary>
    [Fact]
    public void LoadingANewFileDiscardsThePreviousTransform()
    {
        MainWindowViewModel vm = Loaded(Track(0, 0, 60, 62, 64));
        vm.ApplyRestyle(Settings(Slendro));

        vm.Adopt(Project(Track(0, 0, 67, 69)));

        vm.RestyledRollNotes.Should().BeEmpty();
        vm.Restyle.Should().BeNull();
        vm.Allocation.Should().BeNull();
    }

    [Fact]
    public void ApplyingWithNoFileLoadedDoesNothingRatherThanThrowing()
    {
        MainWindowViewModel vm = new();

        Action act = () => vm.ApplyRestyle(Settings(Gong));

        act.Should().NotThrow();
        vm.RestyledRollNotes.Should().BeEmpty();
    }

    [Fact]
    public void DrumsAreCarriedThroughTheRestyledLayerUnmoved()
    {
        MainWindowViewModel vm = Loaded(
            Track(0, 0, 60, 62, 64),
            Track(TrackInfo.DrumChannel, 1, 36, 38));

        vm.ApplyRestyle(Settings(Slendro));

        // The drum pitches must appear untouched among the restyled notes: the roll shows what will
        // be exported, and a percussion note number is an instrument, not a pitch.
        double[] cents = [.. vm.RestyledRollNotes.Select(n => n.Cents)];
        cents.Should().Contain(3600).And.Contain(3800);
    }
}
