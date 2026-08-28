namespace MidiRestyle.App.Services;

/// <summary>
/// The outcome of probing a single directory for write access.
/// </summary>
public readonly record struct WritabilityProbe(bool IsWritable, string Reason);

/// <summary>
/// The writable root chosen for this run, and why. <see cref="IsWritable"/> is false only when
/// neither the beside-the-exe folder nor the %APPDATA% fallback could be written to; callers must
/// not attempt to write when it is false.
/// </summary>
public sealed record WritableRootResult(string Root, bool IsBesideExe, bool IsWritable, string Reason);

/// <summary>
/// Decides where MIDIRestyle may write (settings, the user <c>scales/</c> folder) and reports why.
///
/// Path resolution uses <see cref="AppContext.BaseDirectory"/>, never <see cref="System.Reflection.Assembly.Location"/>
/// - that returns <c>""</c> under <c>PublishSingleFile</c> and is banned by the RS0030 analyzer (see
/// BannedSymbols.txt). Writability is decided by attempting a real write and catching, never by
/// inspecting file attributes: attribute checks are wrong under ACLs, and a non-elevated app writing
/// into a protected folder simply throws <see cref="UnauthorizedAccessException"/> with no file
/// virtualization to fall back on.
/// </summary>
public sealed class PathProbe
{
    /// <summary>The folder the app treats as "beside the exe" - normally <see cref="AppContext.BaseDirectory"/>.</summary>
    public string BesideExeDirectory { get; }

    /// <summary>The %APPDATA%\MIDIRestyle fallback folder.</summary>
    public string AppDataDirectory { get; }

    public PathProbe(string? besideExeDirectory = null, string? appDataDirectory = null)
    {
        BesideExeDirectory = besideExeDirectory ?? AppContext.BaseDirectory;
        AppDataDirectory = appDataDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MIDIRestyle");
    }

    public WritabilityProbe ProbeBesideExe() => ProbeWritability(BesideExeDirectory, "beside-the-exe");

    public WritabilityProbe ProbeAppData() => ProbeWritability(AppDataDirectory, "%APPDATA%");

    /// <summary>
    /// Beside-the-exe wins when it is writable. Otherwise falls back to %APPDATA%. If neither is
    /// writable, returns the %APPDATA% path with <see cref="WritableRootResult.IsWritable"/> false -
    /// callers must check that flag rather than assume the returned root can be used.
    /// </summary>
    public WritableRootResult ResolveWritableRoot()
    {
        var beside = ProbeBesideExe();
        if (beside.IsWritable)
        {
            return new WritableRootResult(BesideExeDirectory, IsBesideExe: true, IsWritable: true, beside.Reason);
        }

        var appData = ProbeAppData();
        if (appData.IsWritable)
        {
            return new WritableRootResult(
                AppDataDirectory,
                IsBesideExe: false,
                IsWritable: true,
                $"{beside.Reason} Falling back to %APPDATA%: {appData.Reason}");
        }

        return new WritableRootResult(
            AppDataDirectory,
            IsBesideExe: false,
            IsWritable: false,
            $"Neither location is writable. Beside-the-exe: {beside.Reason} AppData: {appData.Reason}");
    }

    /// <summary>
    /// Probes <paramref name="directory"/> for write access by writing a uniquely-named temp file and
    /// deleting it again - never by inspecting attributes. Leaves no file behind either way.
    /// </summary>
    public static WritabilityProbe ProbeWritability(string directory, string label)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WritabilityProbe(false, $"{label} directory '{directory}' could not be created: {ex.Message}");
        }

        var probeFile = Path.Combine(directory, $".midirestyle-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = File.Open(probeFile, FileMode.CreateNew, FileAccess.Write))
            {
                stream.WriteByte(0);
            }

            return new WritabilityProbe(true, $"{label} directory '{directory}' is writable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WritabilityProbe(false, $"{label} directory '{directory}' is not writable: {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(probeFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup only; the probe result above already reflects writability.
            }
        }
    }
}
