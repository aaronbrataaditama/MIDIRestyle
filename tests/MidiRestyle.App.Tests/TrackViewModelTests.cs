using MidiRestyle.App.ViewModels;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Guards the track-list invariants that the plan puts in the view layer.
/// </summary>
/// <remarks>
/// MVVM was chosen partly so these could be asserted rather than left to manual clicking. Each of
/// these rules is one a plausible implementation gets wrong.
/// </remarks>
public class TrackViewModelTests
{
    private static TrackInfo Track(int channel, int trackIndex = 0, params int[] noteNumbers) =>
        new()
        {
            TrackIndex = trackIndex,
            Channel = channel,
            Name = channel == TrackInfo.DrumChannel ? "Drums" : "Melody",
            Notes = [.. noteNumbers.Select(n => new Note(Pitch.FromMidi(n), 0, 480, 90))],
        };

    [Fact]
    public void PitchedTrackStartsIncludedAndUnlocked()
    {
        TrackViewModel vm = new(Track(0, 0, 60, 64, 67));

        vm.IsLocked.Should().BeFalse();
        vm.LockReason.Should().BeNull();
        vm.Restyle.Should().BeTrue();
        vm.WillBeRestyled.Should().BeTrue();
    }

    [Fact]
    public void DrumTrackIsLockedAndExcludedFromTheStart()
    {
        TrackViewModel vm = new(Track(TrackInfo.DrumChannel, 0, 36, 38));

        vm.IsLocked.Should().BeTrue();
        vm.Restyle.Should().BeFalse();
        vm.WillBeRestyled.Should().BeFalse();
    }

    /// <summary>
    /// The lock has to be real, not cosmetic. Disabling a checkbox in XAML stops a mouse, not a
    /// binding or a future refactor - so the rule is enforced in the view model too.
    /// </summary>
    [Fact]
    public void ForcingTheDrumCheckboxOnStillDoesNotRestyleDrums()
    {
        TrackViewModel vm = new(Track(TrackInfo.DrumChannel, 0, 36));

        vm.Restyle = true;

        vm.WillBeRestyled.Should().BeFalse(
            "remapping a percussion note number changes which drum is struck, not its pitch");
    }

    [Fact]
    public void DrumLockExplainsItselfRatherThanJustBeingDisabled()
    {
        TrackViewModel vm = new(Track(TrackInfo.DrumChannel, 0, 36));

        vm.LockReason.Should().NotBeNullOrWhiteSpace();
        vm.LockReason.Should().Contain("drum");
    }

    /// <summary>Channel is shown 1-based because that is what every DAW and musician calls it.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 10)]
    [InlineData(15, 16)]
    public void ChannelIsDisplayedOneBased(int channel, int expected) =>
        new TrackViewModel(Track(channel, 0, 60)).DisplayChannel.Should().Be(expected);

    [Fact]
    public void RangeTextNamesTheLowestAndHighestNotes()
    {
        // MIDI 60 is middle C, which General MIDI calls C4; MIDI 67 is G4.
        TrackViewModel vm = new(Track(0, 0, 60, 67, 64));

        vm.RangeText.Should().Be("C4 - G4");
    }

    [Fact]
    public void RangeTextIsEmptyForATrackWithNoNotes() =>
        new TrackViewModel(Track(0)).RangeText.Should().BeEmpty();

    [Fact]
    public void AnEmptyTrackIsNotRestyledEvenThoughItIsUnlocked()
    {
        TrackViewModel vm = new(Track(0));

        vm.IsLocked.Should().BeFalse();
        vm.Restyle.Should().BeTrue();
        vm.WillBeRestyled.Should().BeFalse("there is nothing to transform");
    }

    [Fact]
    public void ExistingPitchBendIsSurfacedWithAReason()
    {
        TrackInfo track = Track(0, 0, 60) with { HasExistingPitchBend = true };
        TrackViewModel vm = new(track);

        vm.HasExistingPitchBend.Should().BeTrue();
        vm.PitchBendWarning.Should().NotBeNullOrWhiteSpace();
        vm.PitchBendWarning.Should().Contain("12-TET");
    }

    [Fact]
    public void NoPitchBendMeansNoWarning() =>
        new TrackViewModel(Track(0, 0, 60)).PitchBendWarning.Should().BeNull();
}
