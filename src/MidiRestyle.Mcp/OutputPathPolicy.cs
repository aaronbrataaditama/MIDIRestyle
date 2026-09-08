using MidiRestyle.Core.Scales;

namespace MidiRestyle.Mcp;

/// <summary>
/// The few places a headless file-writing server must never write: the running exe, the scale data
/// files and the settings file under the data root, and the install folder when it is not the data
/// root (Program Files). Deliberately narrow - a portable exe routinely sits in the same folder as
/// the user's MIDI files, and the default output path there must work exactly as the GUI's export does.
/// </summary>
public sealed record ProtectedLocations(string? ExePath, string DataRoot, string BaseDirectory)
{
    public static ProtectedLocations FromProcess(PathProbe probe) =>
        new(Environment.ProcessPath, probe.ResolveWritableRoot().Root, AppContext.BaseDirectory);

    public bool Refuses(string fullPath, out string reason)
    {
        string root = OutputPathPolicy.Full(DataRoot);
        string[] protectedFiles =
        [
            Path.Combine(root, ScaleLibraryLoader.UserScalesFileName),
            Path.Combine(root, OutputPathPolicy.SettingsFileName),
        ];

        if (ExePath is not null && Same(fullPath, OutputPathPolicy.Full(ExePath)))
        {
            reason = "refused: that is the running MIDIRestyle executable.";
            return true;
        }

        if (protectedFiles.Any(f => Same(fullPath, f)))
        {
            reason = "refused: that is one of MIDIRestyle's own data files.";
            return true;
        }

        if (IsUnder(fullPath, Path.Combine(root, ScaleLibraryLoader.ScalesFolderName)))
        {
            reason = $"refused: the '{ScaleLibraryLoader.ScalesFolderName}' folder holds MIDIRestyle's scale definitions.";
            return true;
        }

        string baseDir = OutputPathPolicy.Full(BaseDirectory);
        if (!Same(baseDir, root) && IsUnder(fullPath, baseDir))
        {
            reason = "refused: the application's install folder is not a place for output files.";
            return true;
        }

        reason = "";
        return false;
    }

    private static bool Same(string a, string b) =>
        string.Equals(OutputPathPolicy.Full(a), OutputPathPolicy.Full(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string dir) =>
        OutputPathPolicy.Full(path)
            .StartsWith(OutputPathPolicy.Full(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Path rules for the two write tools. Inputs must be absolute and exist; outputs must be absolute,
/// carry an allowed extension, avoid the protected locations, and are written atomically (temp file
/// beside the target, then a move) so a failed write never leaves a truncated file and two concurrent
/// callers cannot both believe they created it.
/// </summary>
public static class OutputPathPolicy
{
    /// <summary>Mirrors the App's <c>SettingsService.SettingsFileName</c>; Core has no settings.</summary>
    public const string SettingsFileName = "MIDIRestyle.settings.json";

    public static readonly IReadOnlySet<string> MidiExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mid", ".midi" };

    public static readonly IReadOnlySet<string> MusicXmlExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".musicxml", ".xml" };

    /// <summary>
    /// Canonical form for every path comparison in this file: resolve `..` and relative segments,
    /// normalise slash direction, and drop a trailing separator, then compare case-insensitively
    /// (NTFS is case-insensitive). Two categories of alias are NOT resolved here, and they are not
    /// equally serious:
    ///
    /// - Symlinks/junctions and UNC-vs-mapped-drive aliasing require an adversary to have already
    ///   set the alias up (create the junction, map the drive). By the time such an alias exists,
    ///   whoever created it could have named the protected file directly, so leaving these unresolved
    ///   grants no capability an ordinary fully-qualified path did not already have.
    /// - 8.3 short names are a different case. Windows generates one automatically for every long or
    ///   multi-dot filename (`MIDIRestyle.exe` -> roughly `MIDIRE~1.EXE`, `MIDIRestyle.settings.json`
    ///   likewise) with no setup at all, so `MIDIRE~1.EXE` reaches a target that naming
    ///   `MIDIRestyle.exe` directly is refused for. That is a real, unmitigated defeat of the one
    ///   guard this type provides - not equivalent freedom, unlike the two cases above. It is
    ///   accepted rather than closed because closing it needs a `GetLongPathName` P/Invoke (there is
    ///   no managed API for it), and exploiting it requires the caller to already know the generated
    ///   short-name string - a channel this server does not offer, since it never echoes directory
    ///   listings or short names back to the agent.
    /// </summary>
    internal static string Full(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));

    /// <summary>
    /// <see cref="Path.GetFullPath(string)"/> THROWS rather than returning anything when the OS cannot
    /// resolve a path at all - an embedded NUL, or one past the Win32 length limit. Both are reachable
    /// from an agent-supplied outputPath, and validation deliberately runs BEFORE the calling tool's
    /// try block, so an unguarded call escapes as an SDK-wrapped exception instead of a refusal. Resolve
    /// defensively so the caller can refuse in its own words like every other bad path.
    /// </summary>
    private static bool TryFull(string p, out string full)
    {
        try
        {
            full = Full(p);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
        {
            full = string.Empty;
            return false;
        }
    }

    public static string? ValidateInputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return $"inputPath '{path}' must be an absolute path (e.g. C:\\music\\tune.mid).";
        }

        return File.Exists(path) ? null : $"inputPath '{path}' was not found.";
    }

    public static string DefaultOutputPath(string inputPath, string scaleId, string extension) =>
        Path.Combine(
            Path.GetDirectoryName(inputPath) ?? "",
            $"{Path.GetFileNameWithoutExtension(inputPath)}.{scaleId}{extension}");

    public static string? ValidateOutputPath(
        string outputPath, string inputPath, bool overwrite,
        IReadOnlySet<string> allowedExtensions, ProtectedLocations protectedLocations)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !Path.IsPathFullyQualified(outputPath))
        {
            return $"outputPath '{outputPath}' must be an absolute path.";
        }

        // Deliberately does not echo outputPath back: the two values that reach here are an embedded
        // NUL and a 40,000-character path, and neither belongs in a message an agent will read.
        if (!TryFull(outputPath, out string full))
        {
            return "outputPath is not a usable path: it contains a character the file system cannot "
                + "store, or it is longer than the operating system allows.";
        }

        if (!allowedExtensions.Contains(Path.GetExtension(full)))
        {
            return $"outputPath must end in {string.Join(" or ", allowedExtensions)}.";
        }

        if (protectedLocations.Refuses(full, out string reason))
        {
            return $"outputPath '{outputPath}' {reason}";
        }

        // An inputPath that will not resolve cannot equal one that did, so a failure here is simply "not
        // the same file" - the loader has already reported anything genuinely wrong with it.
        if (TryFull(inputPath, out string fullInput)
            && string.Equals(full, fullInput, StringComparison.OrdinalIgnoreCase) && !overwrite)
        {
            return "outputPath is the input file; pass overwrite=true to replace it in place.";
        }

        return null;
    }

    public static string? WriteAtomically(string outputPath, byte[] bytes, bool overwrite)
    {
        string directory = Path.GetDirectoryName(outputPath) ?? "";
        if (!Directory.Exists(directory))
        {
            return $"Output directory '{directory}' does not exist.";
        }

        string temp = Path.Combine(directory, Path.GetFileName(outputPath) + ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, outputPath, overwrite);
            return null;
        }
        catch (IOException) when (!overwrite && File.Exists(outputPath))
        {
            return $"'{outputPath}' already exists; pass overwrite=true to replace it.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not write '{outputPath}': {ex.Message}";
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort. A stray temp file is inert.
            }
        }
    }
}
