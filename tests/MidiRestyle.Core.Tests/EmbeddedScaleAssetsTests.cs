using MidiRestyle.Core.Scales;

namespace MidiRestyle.Core.Tests;

public class EmbeddedScaleAssetsTests
{
    [Fact]
    public void AllNineShippedFilesAreEmbeddedAndParse()
    {
        IReadOnlyList<EmbeddedScaleAsset> assets = EmbeddedScaleAssets.ReadAll();

        assets.Select(a => a.FileName).Should().BeEquivalentTo(
        [
            "africa.json", "americas.json", "east-asia.json", "europe.json", "middle-east.json",
            "persian.json", "south-asia-thaats.json", "southeast-asia.json", "turkish-makam.json",
        ]);

        int total = 0;
        foreach (EmbeddedScaleAsset asset in assets)
        {
            ScaleJsonLoadResult parsed = ScaleJsonStore.LoadFromString(asset.Json);
            parsed.FileError.Should().BeNull(asset.FileName);
            parsed.Failures.Should().BeEmpty(asset.FileName);
            total += parsed.Scales.Count;
        }

        total.Should().Be(99, "99 authored scales ship in the nine files");
    }
}
