using MidiRestyle.App.Services;

namespace MidiRestyle.App.Tests;

/// <summary>
/// All temp directories are unique per test and cleaned up afterwards, so nothing is ever written
/// beside the test assembly or into the real %APPDATA%.
/// </summary>
public sealed class PathProbeTests : IDisposable
{
    private readonly string _tempRoot;

    public PathProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "midirestyle-pathprobe-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private string NewPath(string name) => Path.Combine(_tempRoot, name);

    [Fact]
    public void ProbeWritability_detects_writable_directory_by_actually_writing_and_leaves_no_file_behind()
    {
        var dir = NewPath("writable");

        var result = PathProbe.ProbeWritability(dir, "test");

        result.IsWritable.Should().BeTrue();
        Directory.Exists(dir).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(dir).Should().BeEmpty("the write probe must clean up after itself");
    }

    [Fact]
    public void ProbeWritability_reports_not_writable_when_the_directory_cannot_be_created()
    {
        // A file already sitting at the target path makes Directory.CreateDirectory fail - a
        // reliable, portable stand-in for a read-only or otherwise inaccessible directory that
        // doesn't require touching real ACLs or admin-only system folders.
        var blockedPath = NewPath("blocked");
        File.WriteAllText(blockedPath, "not a directory");

        var result = PathProbe.ProbeWritability(blockedPath, "test");

        result.IsWritable.Should().BeFalse();
        result.Reason.Should().Contain(blockedPath);
    }

    [Fact]
    public void ResolveWritableRoot_prefers_beside_the_exe_when_it_is_writable()
    {
        var besideExe = NewPath("beside-exe");
        var appData = NewPath("appdata");
        var probe = new PathProbe(besideExe, appData);

        var result = probe.ResolveWritableRoot();

        result.IsWritable.Should().BeTrue();
        result.IsBesideExe.Should().BeTrue();
        result.Root.Should().Be(besideExe);
    }

    [Fact]
    public void ResolveWritableRoot_falls_back_to_appdata_when_beside_the_exe_is_not_writable_and_states_why()
    {
        var besideExeBlocked = NewPath("beside-exe-blocked");
        File.WriteAllText(besideExeBlocked, "blocking file");
        var appData = NewPath("appdata");
        var probe = new PathProbe(besideExeBlocked, appData);

        var result = probe.ResolveWritableRoot();

        result.IsWritable.Should().BeTrue();
        result.IsBesideExe.Should().BeFalse();
        result.Root.Should().Be(appData);
        result.Reason.Should().Contain("APPDATA");
        result.Reason.Should().Contain(besideExeBlocked);
    }

    [Fact]
    public void ResolveWritableRoot_reports_not_writable_when_neither_location_can_be_written_to()
    {
        var besideExeBlocked = NewPath("beside-exe-blocked");
        File.WriteAllText(besideExeBlocked, "blocking file");
        var appDataBlocked = NewPath("appdata-blocked");
        File.WriteAllText(appDataBlocked, "blocking file");
        var probe = new PathProbe(besideExeBlocked, appDataBlocked);

        var result = probe.ResolveWritableRoot();

        result.IsWritable.Should().BeFalse();
        result.Reason.Should().Contain(besideExeBlocked);
        result.Reason.Should().Contain(appDataBlocked);
    }

    [Fact]
    public void Default_constructor_resolves_beside_the_exe_to_AppContext_BaseDirectory()
    {
        var probe = new PathProbe();

        probe.BesideExeDirectory.Should().Be(AppContext.BaseDirectory);
        probe.AppDataDirectory.Should().Contain("MIDIRestyle");
    }
}
