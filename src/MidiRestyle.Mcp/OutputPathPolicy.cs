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
        string root = Full(DataRoot);
        string[] protectedFiles =
        [
            Path.Combine(root, ScaleLibraryLoader.UserScalesFileName),
            Path.Combine(root, OutputPathPolicy.SettingsFileName),
        ];

        if (ExePath is not null && Same(fullPath, Full(ExePath)))
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

        string baseDir = Full(BaseDirectory);
        if (!Same(baseDir, root) && IsUnder(fullPath, baseDir))
        {
            reason = "refused: the application's install folder is not a place for output files.";
            return true;
        }

        reason = "";
        return false;
    }

    /// <summary>
    /// Canonical form for every path comparison here: resolve `..` and relative segments, normalise
    /// slash direction, and drop a trailing separator, then compare case-insensitively (NTFS is
    /// case-insensitive). Does NOT resolve 8.3 short names, symlinks/junctions, UNC-vs-mapped-drive
    /// aliasing, or reconcile a `\\?\`-prefixed path against the same path without the prefix -
    /// closing those needs a filesystem handle, not string canonicalisation, and is out of scope here.
    /// </summary>
    private static string Full(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
    private static bool Same(string a, string b) => string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
    private static bool IsUnder(string path, string dir) =>
        Full(path).StartsWith(Full(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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

        string full = Path.GetFullPath(outputPath);
        if (!allowedExtensions.Contains(Path.GetExtension(full)))
        {
            return $"outputPath must end in {string.Join(" or ", allowedExtensions)}.";
        }

        if (protectedLocations.Refuses(full, out string reason))
        {
            return $"outputPath '{outputPath}' {reason}";
        }

        if (string.Equals(full, Path.GetFullPath(inputPath), StringComparison.OrdinalIgnoreCase) && !overwrite)
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
