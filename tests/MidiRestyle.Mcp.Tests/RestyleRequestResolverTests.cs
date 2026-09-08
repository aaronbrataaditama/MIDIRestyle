using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Mcp;

namespace MidiRestyle.Mcp.Tests;

public sealed class RestyleRequestResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "midirestyle-resolver-" + Guid.NewGuid().ToString("N"));
    private readonly RestyleRequestResolver _resolver = new(TestLibrary.Load());

    public RestyleRequestResolverTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Fixture(string name, (int, int)[] notes, bool format0 = false) =>
        MidiFixtures.Write(Path.Combine(_dir, name), notes, format0);

    private static RestyleRequest Request(string input, string scale = "middleeast.arabic.maqam-rast") =>
        new(input, scale, null, null, null, null, null, null, null, null, null, null, false);

    [Fact]
    public void DefaultsResolveFromKeyDetectionOnAMajorFixture()
    {
        string input = Fixture("major.mid", MidiFixtures.CMajorNotes);

        _resolver.TryResolve(Request(input), out var r, out var error).Should().BeTrue(error);

        r!.Resolved.TargetTonic.Should().Be(new TonicEcho("C4", 60));
        r.Resolved.SourceScaleId.Should().Be(RestyleDefaults.MajorSourceScaleId);
        r.Resolved.SourceTonic.Should().Be(new TonicEcho("C4", 60));
        r.Resolved.KeyDetectionUsed.Should().BeTrue();
        r.Resolved.ToleranceCents.Should().Be(RestyleDefaults.ToleranceCents);
        r.Resolved.Mapping.Should().Be(new MappingEcho("scaleDegree", "snapToNearestSourceDegree", "merge", "shiftIntoRange"));
        r.Settings.TargetScale.Id.Should().Be("middleeast.arabic.maqam-rast");
    }

    [Fact]
    public void DefaultsResolveToAeolianOnAMinorFixture()
    {
        string input = Fixture("minor.mid", MidiFixtures.AMinorNotes);

        _resolver.TryResolve(Request(input), out var r, out var error).Should().BeTrue(error);

        r!.Resolved.TargetTonic.Name.Should().Be("A4");
        r.Resolved.SourceScaleId.Should().Be(RestyleDefaults.MinorSourceScaleId);
    }

    [Fact]
    public void SuppliedValuesAreNotSecondGuessedAndDetectionIsSkipped()
    {
        string input = Fixture("major.mid", MidiFixtures.CMajorNotes);
        var request = Request(input) with { TargetTonic = "D4", SourceScaleId = "europe.churchmodes.aeolian", SourceTonic = "Eb4", ToleranceCents = 0.5 };

        _resolver.TryResolve(request, out var r, out var error).Should().BeTrue(error);

        r!.Resolved.KeyDetectionUsed.Should().BeFalse();
        r.Resolved.TargetTonic.Should().Be(new TonicEcho("D4", 62));
        r.Resolved.SourceTonic.Should().Be(new TonicEcho("Eb4", 63));
        r.Settings.ToleranceCents.Should().Be(0.5);
    }

    [Fact]
    public void AllFieldErrorsArriveInOneMessage()
    {
        var request = new RestyleRequest("relative.mid", "no.such.scale", "H4", null, null, null, "wrap", null, null, null, 0.4, null, false);

        _resolver.TryResolve(request, out var r, out var error).Should().BeFalse();

        r.Should().BeNull();
        error.Should().ContainAll("absolute", "no.such.scale", "H4", "wrap", "scaleDegree", "0.5");
    }

    [Fact]
    public void UnknownScaleIdSuggestsNearMatches()
    {
        string input = Fixture("major.mid", MidiFixtures.CMajorNotes);

        _resolver.TryResolve(Request(input, "rast"), out _, out var error).Should().BeFalse();

        error.Should().Contain("middleeast.arabic.maqam-rast");
    }

    [Fact]
    public void ExcludeMustNameALoadedTrackChannelAndFormat0IsPerChannel()
    {
        string input = Fixture("f0.mid", [(60, 0), (64, 0), (67, 1), (71, 1), (36, 9)], format0: true);

        _resolver.TryResolve(Request(input) with { Exclude = [new TrackChannelRef(0, 1)] }, out var r, out var error).Should().BeTrue(error);
        r!.Resolved.Excluded.Should().BeEquivalentTo([new TrackChannelRef(0, 1), new TrackChannelRef(0, 9)], "drums are excluded by rule and echoed");
        r.Project.Tracks.Count(r.Settings.ShouldRestyle).Should().Be(1, "channel 0 is still restyled");

        _resolver.TryResolve(Request(input) with { Exclude = [new TrackChannelRef(3, 0)] }, out _, out error).Should().BeFalse();
        error.Should().Contain("track 0, channel 0");
    }

    [Fact]
    public void NoKeyAndNoTonicIsAnErrorNamingTheParameter()
    {
        string input = Fixture("drums.mid", MidiFixtures.DrumsOnlyNotes);

        _resolver.TryResolve(Request(input), out _, out var error).Should().BeFalse();

        error.Should().Contain("targetTonic");
    }

    [Fact]
    public void NearestPitchNeedsNoSourceScaleAndWarnsIfOneWasSupplied()
    {
        string input = Fixture("drums.mid", MidiFixtures.DrumsOnlyNotes);
        var request = Request(input) with { Strategy = "nearest_pitch", TargetTonic = "C4", SourceScaleId = "europe.churchmodes.ionian" };

        _resolver.TryResolve(request, out var r, out var error).Should().BeTrue(error);

        r!.Resolved.SourceScaleId.Should().BeNull();
        r.Resolved.SourceTonic.Should().BeNull();
        r.Resolved.KeyDetectionUsed.Should().BeFalse();
        r.Warnings.Should().Contain(w => w.Contains("ignored", StringComparison.Ordinal));
        r.Warnings.Should().Contain(w => w.Contains("No notes will be restyled", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingFileIsALoadError()
    {
        _resolver.TryResolve(Request(Path.Combine(_dir, "missing.mid")), out _, out var error).Should().BeFalse();
        error.Should().Contain("not found");
    }

    [Fact]
    public void NonMidiFileIsALoadError()
    {
        string input = Path.Combine(_dir, "text.mid");
        File.WriteAllText(input, "this is not midi");

        _resolver.TryResolve(Request(input), out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }
    // Review follow-ups, Task 12: both of these threw or slipped through before the fix, and both are
    // reachable from ordinary agent JSON rather than only from hand-built objects.

    [Fact]
    public void ANullExcludeEntryIsRejectedRatherThanDereferenced()
    {
        string input = Fixture("null-exclude.mid", MidiFixtures.CMajorNotes);
        var request = Request(input) with { Exclude = new TrackChannelRef[] { null! } };

        // "exclude": [null] is legal JSON, so this must be an error, not a NullReferenceException.
        _resolver.TryResolve(request, out var r, out string? error).Should().BeFalse();

        r.Should().BeNull();
        error.Should().Contain("exclude entries need");
    }

    [Fact]
    public void ANullExcludeEntryAlongsideAValidOneIsAlsoRejected()
    {
        // The first guard runs before the file loads; this one reaches the post-load foreach, which
        // dereferenced the same entries.
        string input = Fixture("null-exclude-2.mid", MidiFixtures.CMajorNotes);
        var request = Request(input) with { Exclude = [new TrackChannelRef(0, 0), null!] };

        _resolver.TryResolve(request, out var r, out string? error).Should().BeFalse();

        r.Should().BeNull();
        error.Should().Contain("exclude entries need");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonFiniteToleranceIsRefused(double tolerance)
    {
        // NaN is the live one: McpJson.Options is built on JsonSerializerDefaults.Web, whose
        // AllowReadingFromString binds "toleranceCents": "NaN" to a real NaN. Both range comparisons
        // are false for NaN, so it used to sail through and surface as an ArgumentOutOfRangeException
        // out of OffsetClusterer once the settings reached the engine.
        string input = Fixture("tolerance.mid", MidiFixtures.CMajorNotes);
        var request = Request(input) with { ToleranceCents = tolerance };

        _resolver.TryResolve(request, out var r, out string? error).Should().BeFalse();

        r.Should().BeNull();
        error.Should().Contain("toleranceCents");
    }

}
