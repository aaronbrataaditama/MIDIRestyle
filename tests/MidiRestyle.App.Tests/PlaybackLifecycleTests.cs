using MidiRestyle.App.Controls;
using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Scales;
using MidiRestyle.Core.Tuning;
using MidiRestyle.Playback;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Covers three things reported from actually using the app.
/// </summary>
public class PlaybackLifecycleTests
{
    private static readonly Scale CMajor = new(
        "t.cmajor", "C major", "Western", "Europe & Balkans",
        [0, 200, 400, 500, 700, 900, 1100], "Test fixture, 2026");

    private static readonly Scale Gong = new(
        "t.gong", "Gong", "Chinese Wusheng", "East Asia",
        [0, 200, 400, 700, 900], "Test fixture, 2026");

    private static MidiProject Project(string path, params int[] notes) => new()
    {
        FilePath = path,
        Format = MidiFileFormatKind.MultiTrack,
        Division = new TicksPerQuarterNote(480),
        Tracks =
        [
            new TrackInfo
            {
                TrackIndex = 0,
                Channel = 0,
                Notes = [.. notes.Select((n, i) => new Note(Pitch.FromMidi(n), i * 480, 480, 90))],
            },
        ],
        TempoMap = [new TempoChange(0, 500_000)],
    };

    private static RestyleSettings Settings() => new()
    {
        TargetScale = Gong,
        TargetTonic = Pitch.FromMidi(60),
        SourceScale = CMajor,
        SourceTonic = Pitch.FromMidi(60),
    };

    private static MainWindowViewModel WithEngine(out NullPlaybackEngine engine)
    {
        engine = new NullPlaybackEngine("test engine");
        MainWindowViewModel vm = new();
        vm.AttachEngine(engine);
        vm.AudioAvailable = true;
        return vm;
    }

    // --- 1. Play works before a target scale is chosen ------------------------------------

    /// <summary>
    /// Hearing the file you just opened is the first thing anyone does; requiring a scale choice
    /// first would be a gate with no purpose.
    /// </summary>
    [Fact]
    public void AudioPreparesFromTheOriginalAloneWhenNoScaleHasBeenChosen()
    {
        MainWindowViewModel vm = WithEngine(out NullPlaybackEngine engine);
        vm.Adopt(Project(@"C:\a.mid", 60, 62, 64));

        vm.Restyle.Should().BeNull("no scale has been selected");
        vm.PrepareAudio().Should().BeTrue();
        engine.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void PlayIsAvailableWithoutATransformButComparingIsNot()
    {
        MainWindowViewModel vm = WithEngine(out _);
        vm.Adopt(Project(@"C:\a.mid", 60, 62));

        vm.CanPlay.Should().BeTrue();
        vm.CanCompare.Should().BeFalse();
        vm.CompareDisabledReason.Should().Contain("target scale");
    }

    [Fact]
    public void TheOriginalOnlySequenceHoldsTheSourcePitchesOnBothSides()
    {
        MidiProject project = Project(@"C:\a.mid", 60, 62, 64);

        PlaybackBuildResult built = PlaybackSequenceBuilder.BuildOriginalOnly(project);

        built.Success.Should().BeTrue(built.Message);
        built.Sequences!.Original.Should().Equal(built.Sequences.Restyled,
            "with nothing to compare, both sides are the same file - so a stray toggle is harmless");
        built.Sequences.Allocation.Should().BeNull();
    }

    // --- 2. Opening a second file must not replay the first --------------------------------

    /// <summary>
    /// The reported bug: open a file, open another, press Play, hear the first one.
    /// </summary>
    /// <remarks>
    /// The cause was that stopping is not forgetting. <c>Stop</c> left the previous sequences loaded,
    /// so the next Play replayed them. Only an explicit unload fixes it.
    /// </remarks>
    [Fact]
    public void LoadingASecondFileDiscardsTheFirstFilesPreparedAudio()
    {
        MainWindowViewModel vm = WithEngine(out NullPlaybackEngine engine);
        vm.Adopt(Project(@"C:\first.mid", 60, 62, 64));
        vm.PrepareAudio().Should().BeTrue();
        engine.IsLoaded.Should().BeTrue();

        vm.Adopt(Project(@"C:\second.mid", 67, 69));

        engine.IsLoaded.Should().BeFalse(
            "the first file's audio must be gone, or Play sounds the file the user just closed");
    }

    [Fact]
    public void ChangingTheTargetScaleDiscardsThePreparedAudioToo()
    {
        MainWindowViewModel vm = WithEngine(out NullPlaybackEngine engine);
        vm.Adopt(Project(@"C:\a.mid", 60, 62, 64));
        vm.ApplyRestyle(Settings());
        vm.PrepareAudio().Should().BeTrue();

        vm.ApplyRestyle(Settings() with { TargetScale = CMajor });

        engine.IsLoaded.Should().BeFalse("what the audio was prepared from has changed");
    }

    [Fact]
    public void LoadingASecondFileResetsTheTransportState()
    {
        MainWindowViewModel vm = WithEngine(out _);
        vm.Adopt(Project(@"C:\first.mid", 60, 62));
        vm.PrepareAudio();
        vm.IsPlaying = true;
        vm.HearingRestyled = true;

        vm.Adopt(Project(@"C:\second.mid", 67));

        vm.IsPlaying.Should().BeFalse();
        vm.HearingRestyled.Should().BeFalse("the new file has no restyled side to be hearing");
        vm.PlayheadTicks.Should().BeLessThan(0, "a hidden playhead, not one left mid-piece");
    }

    // --- 3. The roll follows the playhead --------------------------------------------------

    private static PianoRoll Roll(double pixelsPerTick = 0.5)
    {
        PianoRoll roll = new()
        {
            Width = 1000,
            Height = 600,
            PixelsPerTick = pixelsPerTick,
            PixelsPerCent = 0.25,
            TopCents = 8400,
        };

        // Bounds are a layout result, so drive one to make VisibleTicks meaningful.
        roll.Measure(new Avalonia.Size(1000, 600));
        roll.Arrange(new Avalonia.Rect(0, 0, 1000, 600));
        return roll;
    }

    [Fact]
    public void FollowingDoesNothingWhileThePlayheadIsComfortablyVisible()
    {
        PianoRoll roll = Roll();
        roll.ScrollTicks = 0;
        roll.PlayheadTicks = 500;   // 1000px / 0.5 = 2000 ticks visible, so 500 is at 25%

        roll.FollowPlayhead().Should().BeFalse(
            "scrolling when nothing needs to move makes the display twitch");
        roll.ScrollTicks.Should().Be(0);
    }

    [Fact]
    public void FollowingScrollsForwardOnceThePlayheadPassesTheComfortableBand()
    {
        PianoRoll roll = Roll();
        roll.ScrollTicks = 0;
        roll.PlayheadTicks = 1900;  // past 85% of 2000

        roll.FollowPlayhead().Should().BeTrue();
        roll.ScrollTicks.Should().BeGreaterThan(0);
        (roll.PlayheadTicks - roll.ScrollTicks).Should().BeLessThan(roll.VisibleTicks,
            "the playhead must end up on screen");
    }

    [Fact]
    public void FollowingCatchesUpWhenThePlayheadIsBehindTheViewport()
    {
        PianoRoll roll = Roll();
        roll.ScrollTicks = 10_000;
        roll.PlayheadTicks = 200;

        roll.FollowPlayhead().Should().BeTrue();
        roll.ScrollTicks.Should().BeLessThan(200, "a rewind must bring the view back with it");
        roll.ScrollTicks.Should().BeGreaterThanOrEqualTo(0, "never scroll before the start");
    }

    [Fact]
    public void FollowingDoesNothingWithNoPlayhead()
    {
        PianoRoll roll = Roll();
        roll.ScrollTicks = 500;
        roll.PlayheadTicks = -1;

        roll.FollowPlayhead().Should().BeFalse();
        roll.ScrollTicks.Should().Be(500);
    }

    [Fact]
    public void TheViewportReportsItsExtentSoAScrollbarCanSizeItself()
    {
        PianoRoll roll = Roll();

        roll.VisibleTicks.Should().BeApproximately(2000, 1e-6, "1000 px at 0.5 px/tick");
        roll.VisibleCents.Should().BeApproximately(2400, 1e-6, "600 px at 0.25 px/cent");
    }

    [Fact]
    public void DurationTicksIsExposedForTheScrollbarExtent()
    {
        MainWindowViewModel vm = WithEngine(out _);
        vm.DurationTicks.Should().Be(0, "no file, no extent");

        vm.Adopt(Project(@"C:\a.mid", 60, 62, 64));

        vm.DurationTicks.Should().Be(3 * 480, "three notes of 480 ticks each");
    }
}
