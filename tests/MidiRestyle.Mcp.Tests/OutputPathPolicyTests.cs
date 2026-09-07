using MidiRestyle.Core.Scales;
using MidiRestyle.Mcp;

namespace MidiRestyle.Mcp.Tests;

public sealed class OutputPathPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "midirestyle-outpath-" + Guid.NewGuid().ToString("N"));
    private readonly string _data;
    private readonly string _music;
    private readonly ProtectedLocations _protected;

    public OutputPathPolicyTests()
    {
        _data = Path.Combine(_root, "data");
        _music = Path.Combine(_root, "music");
        Directory.CreateDirectory(Path.Combine(_data, ScaleLibraryLoader.ScalesFolderName));
        Directory.CreateDirectory(_music);
        _protected = new ProtectedLocations(
            ExePath: Path.Combine(_data, "MIDIRestyle.exe"),
            DataRoot: _data,
            BaseDirectory: _data);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("relative.mid")]
    [InlineData(@"C:relative.mid")]
    [InlineData(@"\rooted-but-driveless.mid")]
    public void InputMustBeFullyQualified(string path)
    {
        OutputPathPolicy.ValidateInputPath(path).Should().Contain("absolute");
    }

    [Fact]
    public void InputMustExist()
    {
        OutputPathPolicy.ValidateInputPath(Path.Combine(_music, "missing.mid")).Should().Contain("not found");
        string real = Path.Combine(_music, "real.mid"); File.WriteAllBytes(real, [0]);
        OutputPathPolicy.ValidateInputPath(real).Should().BeNull();
    }

    [Fact]
    public void DefaultOutputSitsBesideTheInputWithTheScaleIdInTheName()
    {
        OutputPathPolicy.DefaultOutputPath(Path.Combine(_music, "tune.mid"), "middleeast.arabic.maqam-rast", ".mid")
            .Should().Be(Path.Combine(_music, "tune.middleeast.arabic.maqam-rast.mid"));
    }

    [Fact]
    public void DefaultOutputBesideAPortableExeIsAllowed()
    {
        // The exe lives in the same folder as the user's MIDI files - the common portable layout.
        string input = Path.Combine(_data, "tune.mid");
        string output = OutputPathPolicy.DefaultOutputPath(input, "x", ".mid");
        OutputPathPolicy.ValidateOutputPath(output, input, false, OutputPathPolicy.MidiExtensions, _protected).Should().BeNull();
    }

    [Theory]
    [InlineData("MIDIRestyle.exe")]
    [InlineData("scales/europe.json")]
    [InlineData("user.scales.json")]
    [InlineData("MIDIRestyle.settings.json")]
    public void TheAppsOwnFilesAreRefused(string relative)
    {
        string output = Path.Combine(_data, relative.Replace('/', Path.DirectorySeparatorChar));
        var allowAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".json", ".mid" };
        OutputPathPolicy.ValidateOutputPath(output, Path.Combine(_music, "in.mid"), true, allowAll, _protected)
            .Should().Contain("refused");
    }

    [Fact]
    public void SegmentBoundaryPreventsPrefixMatchOnASiblingFolder()
    {
        // _protected's DataRoot/BaseDirectory is _root/data - "data" must not be treated as
        // containing "dataother", which a raw character-prefix comparison would get wrong.
        string output = Path.Combine(_root, "dataother", "x.mid");
        OutputPathPolicy.ValidateOutputPath(output, Path.Combine(_music, "in.mid"), false, OutputPathPolicy.MidiExtensions, _protected)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("relative.mid")]
    [InlineData(@"C:relative.mid")]
    [InlineData(@"\rooted-but-driveless.mid")]
    public void OutputMustBeFullyQualified(string path)
    {
        OutputPathPolicy.ValidateOutputPath(path, Path.Combine(_music, "in.mid"), false, OutputPathPolicy.MidiExtensions, _protected)
            .Should().Contain("absolute");
    }

    [Fact]
    public void MusicXmlExtensionAllowListAcceptsItsOwnExtensionsAndRejectsOthers()
    {
        string input = Path.Combine(_music, "in.mid");
        OutputPathPolicy.ValidateOutputPath(Path.Combine(_music, "out.musicxml"), input, false, OutputPathPolicy.MusicXmlExtensions, _protected).Should().BeNull();
        OutputPathPolicy.ValidateOutputPath(Path.Combine(_music, "out.xml"), input, false, OutputPathPolicy.MusicXmlExtensions, _protected).Should().BeNull();
        OutputPathPolicy.ValidateOutputPath(Path.Combine(_music, "out.mid"), input, false, OutputPathPolicy.MusicXmlExtensions, _protected).Should().Contain(".musicxml");
    }

    [Fact]
    public void BaseDirectoryIsRefusedOnlyWhenItIsNotTheDataRoot()
    {
        var programFiles = _protected with { BaseDirectory = Path.Combine(_root, "program-files") };
        string output = Path.Combine(programFiles.BaseDirectory, "out.mid");
        OutputPathPolicy.ValidateOutputPath(output, Path.Combine(_music, "in.mid"), false, OutputPathPolicy.MidiExtensions, programFiles)
            .Should().Contain("refused");
    }

    [Fact]
    public void ExtensionAllowListIsCaseInsensitive()
    {
        string input = Path.Combine(_music, "in.mid");
        OutputPathPolicy.ValidateOutputPath(Path.Combine(_music, "OUT.MIDI"), input, false, OutputPathPolicy.MidiExtensions, _protected).Should().BeNull();
        OutputPathPolicy.ValidateOutputPath(Path.Combine(_music, "out.wav"), input, false, OutputPathPolicy.MidiExtensions, _protected).Should().Contain(".mid");
    }

    [Fact]
    public void OutputEqualToInputNeedsOverwrite()
    {
        string input = Path.Combine(_music, "same.mid");
        OutputPathPolicy.ValidateOutputPath(input, input, false, OutputPathPolicy.MidiExtensions, _protected).Should().Contain("overwrite");
        OutputPathPolicy.ValidateOutputPath(input, input, true, OutputPathPolicy.MidiExtensions, _protected).Should().BeNull();
    }

    [Fact]
    public void WriteIsAtomicAndRespectsOverwrite()
    {
        string output = Path.Combine(_music, "w.mid");

        OutputPathPolicy.WriteAtomically(output, [1, 2, 3], overwrite: false).Should().BeNull();
        File.ReadAllBytes(output).Should().Equal([1, 2, 3]);

        OutputPathPolicy.WriteAtomically(output, [9], overwrite: false).Should().Contain("overwrite");
        File.ReadAllBytes(output).Should().Equal([1, 2, 3], "a refused write leaves the file untouched");

        OutputPathPolicy.WriteAtomically(output, [9], overwrite: true).Should().BeNull();
        File.ReadAllBytes(output).Should().Equal([9]);

        Directory.EnumerateFiles(_music).Should().ContainSingle("no temp files survive");
    }

    [Fact]
    public void ConcurrentWritersWithoutOverwriteYieldExactlyOneWinner()
    {
        string output = Path.Combine(_music, "race.mid");
        var outcomes = new string?[8];

        Parallel.For(0, outcomes.Length, i => outcomes[i] = OutputPathPolicy.WriteAtomically(output, [(byte)i], overwrite: false));

        outcomes.Count(o => o is null).Should().Be(1);
        Directory.EnumerateFiles(_music).Should().ContainSingle();
    }

    [Fact]
    public void MissingDirectoryIsAnErrorNotAMkdir()
    {
        string output = Path.Combine(_music, "nope", "w.mid");
        OutputPathPolicy.WriteAtomically(output, [1], overwrite: false).Should().Contain("does not exist");
        Directory.Exists(Path.Combine(_music, "nope")).Should().BeFalse();
    }
}
