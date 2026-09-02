using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Where in the piece you are, in bars: the file pane's count and the transport's readout.
/// </summary>
/// <remarks>
/// Both read the same <see cref="MetadataViewModel.Measures"/>, and that is the point of these
/// tests as much as the arithmetic is. Three places in the app now say where a bar is - the file
/// pane, the transport and the roll's ruler - and three readings of the time-signature map would
/// eventually disagree, in the same way <c>MeasureGrid</c>'s own remarks say the staff and the
/// exporter would.
/// </remarks>
public class BarPositionTests
{
    private static MidiProject Project(
        int noteCount = 8, int ticksPerNote = 480, params TimeSignatureChange[] signatures) => new()
        {
            FilePath = @"C:\music\test.mid",
            Format = MidiFileFormatKind.MultiTrack,
            Division = new TicksPerQuarterNote(480),
            TimeSignatures = signatures,
            Tracks =
            [
                new TrackInfo
                {
                    TrackIndex = 0,
                    Channel = 0,
                    Notes =
                    [
                        .. Enumerable.Range(0, noteCount).Select(i =>
                            new Note(Pitch.FromMidi(60), i * (long)ticksPerNote, ticksPerNote, 90)),
                    ],
                },
            ],
        };

    /// <summary>A SMPTE-timed file: absolute frames, no PPQN, so no notated bar exists to count.</summary>
    private static MidiProject SmpteProject() => new()
    {
        FilePath = @"C:\music\smpte.mid",
        Format = MidiFileFormatKind.MultiTrack,
        Division = new SmpteDivision(25, 40),
        Tracks =
        [
            new TrackInfo
            {
                TrackIndex = 0,
                Channel = 0,
                Notes = [new Note(Pitch.FromMidi(60), 0, 1000, 90)],
            },
        ],
    };

    // --- the file pane -----------------------------------------------------------------------

    [Fact]
    public void EightQuarterNotesInFourFourAreTwoBars()
    {
        MetadataViewModel metadata = new(Project());

        metadata.BarCount.Should().Be(2);
        metadata.DurationText.Should().Contain("2 bars");
    }

    [Fact]
    public void ASingleBarIsNotPluralised() =>
        new MetadataViewModel(Project(noteCount: 4)).DurationText.Should().Contain("1 bar")
            .And.NotContain("1 bars");

    [Fact]
    public void TheDurationIsShownToATenthOfASecond()
    {
        // Eight quarters at the default 120 BPM is four seconds exactly.
        new MetadataViewModel(Project()).DurationText.Should().StartWith("0:04.0");
    }

    [Fact]
    public void TheTimeSignatureDecidesTheBarCount()
    {
        // The same eight quarter notes: four bars of 2/4 rather than two of 4/4.
        MetadataViewModel metadata = new(Project(signatures: new TimeSignatureChange(0, 2, 4)));

        metadata.BarCount.Should().Be(4);
    }

    /// <summary>
    /// A SMPTE file has no tempo map and no notated beat, so it gets neither figure rather than an
    /// invented one - the same rule the duration already followed.
    /// </summary>
    [Fact]
    public void ASmpteFileReportsNoBarsRatherThanGuessing()
    {
        MetadataViewModel metadata = new(SmpteProject());

        metadata.BarCount.Should().BeNull();
        metadata.Measures.Should().BeEmpty();
        metadata.DurationText.Should().NotContain("bar");
    }

    // --- the transport readout ------------------------------------------------------------------

    [Fact]
    public void AtRestTheReadoutIsTheFirstBar()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project());

        vm.HasBarPosition.Should().BeTrue();
        vm.BarPositionText.Should().Be("bar 1 / 2", "musicians count bars from one, and the "
            + "playhead is at the top of the piece before anything has played");
        vm.PlaybackFraction.Should().Be(0);
    }

    [Fact]
    public void ThePlayheadMovesTheBarNumber()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project());

        vm.PlayheadTicks = 1920;

        vm.BarPositionText.Should().Be("bar 2 / 2");
        vm.PlaybackFraction.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void TheFractionNeverRunsPastTheEnd()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project());

        vm.PlayheadTicks = 999_999;

        vm.PlaybackFraction.Should().Be(1);
        vm.BarPositionText.Should().Be("bar 2 / 2", "a tick past the end is still in the last bar");
    }

    [Fact]
    public void WithNoFileThereIsNothingToReport()
    {
        MainWindowViewModel vm = new();

        vm.HasBarPosition.Should().BeFalse();
        vm.BarPositionText.Should().BeEmpty();
        vm.PlaybackFraction.Should().Be(0);
        vm.BarStartTicks.Should().BeEmpty();
    }

    [Fact]
    public void ASmpteFileHidesTheReadoutRatherThanShowingBarZero()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(SmpteProject());

        vm.HasBarPosition.Should().BeFalse();
        vm.BarStartTicks.Should().BeEmpty();
    }

    // --- the roll's ruler ------------------------------------------------------------------------

    [Fact]
    public void TheRollIsGivenTheSameBarlinesTheReadoutCounts()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project(noteCount: 12));

        vm.BarStartTicks.Should().Equal([0L, 1920L, 3840L]);
        vm.BarStartTicks.Length.Should().Be(
            vm.Metadata!.BarCount, "the ruler and the file pane must not count differently");
    }

    [Fact]
    public void APickupBarShowsUpAsAShortFirstMeasure()
    {
        // One eighth of pickup, then 4/4 - the shape that made the ruler's first-gap estimate wrong.
        MainWindowViewModel vm = new();
        vm.Adopt(Project(
            noteCount: 12,
            signatures: [new TimeSignatureChange(0, 1, 8), new TimeSignatureChange(240, 4, 4)]));

        vm.BarStartTicks.Take(3).Should().Equal([0L, 240L, 2160L]);
    }

    // --- notification --------------------------------------------------------------------------

    /// <summary>
    /// The readout is bound to, so being right is not enough - see
    /// <see cref="TransportNotificationTests"/> for the bug that rule comes from.
    /// </summary>
    [Fact]
    public void LoadingAFileAnnouncesTheReadout()
    {
        MainWindowViewModel vm = new();
        List<string> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "?");

        vm.Adopt(Project());

        raised.Should().Contain(nameof(MainWindowViewModel.HasBarPosition))
            .And.Contain(nameof(MainWindowViewModel.BarPositionText))
            .And.Contain(nameof(MainWindowViewModel.PlaybackFraction))
            .And.Contain(nameof(MainWindowViewModel.BarStartTicks));
    }

    [Fact]
    public void MovingThePlayheadAnnouncesTheReadout()
    {
        MainWindowViewModel vm = new();
        vm.Adopt(Project());

        List<string> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "?");

        vm.PlayheadTicks = 1920;

        raised.Should().Contain(nameof(MainWindowViewModel.BarPositionText))
            .And.Contain(nameof(MainWindowViewModel.PlaybackFraction));
    }
}
