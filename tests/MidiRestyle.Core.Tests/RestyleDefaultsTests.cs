using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Output;
using MidiRestyle.Core.Restyle;

namespace MidiRestyle.Core.Tests;

/// <summary>Pins the defaults both settings builders (GUI panel and MCP resolver) share.</summary>
public class RestyleDefaultsTests
{
    [Fact]
    public void ValuesMatchWhatTheGuiShippedWith()
    {
        RestyleDefaults.TonicOctave.Should().Be(4);
        RestyleDefaults.ToleranceCents.Should().Be(OffsetClusterer.DefaultToleranceCents);
        RestyleDefaults.MinToleranceCents.Should().Be(0.5);
        RestyleDefaults.MaxToleranceCents.Should().Be(50.0);
        RestyleDefaults.MajorSourceScaleId.Should().Be("europe.churchmodes.ionian");
        RestyleDefaults.MinorSourceScaleId.Should().Be("europe.churchmodes.aeolian");
    }

    [Fact]
    public void ChosenCandidateIsTheTopCandidateEvenWhenAmbiguous()
    {
        var major = new KeyEstimate(2, IsMinor: false, R: 0.9, Margin: 0.01);
        var minor = new KeyEstimate(9, IsMinor: true, R: 0.89, Margin: -0.01);
        KeyDetectionResult ambiguous = KeyDetectionResult.Ranked(
            [major, minor], margin: 0.01, PitchClassProfile.Empty, ambiguityThreshold: 0.05);

        RestyleDefaults.ChosenCandidate(ambiguous).Should().Be(major);
        RestyleDefaults.ChosenCandidate(null).Should().BeNull();
        RestyleDefaults.SourceScaleIdFor(minor).Should().Be(RestyleDefaults.MinorSourceScaleId);
    }
}
