namespace MidiRestyle.Core.Scales;

/// <summary>
/// The outcome of assembling the scale library: the merged library itself, plus everything the
/// status bar needs to report - per-scale load failures (with ids), id collisions, and which
/// directory the beside-exe/AppData <c>scales/</c> folder resolved to and why.
/// </summary>
public sealed record ScaleLibraryLoadResult(
    ScaleLibrary Library,
    IReadOnlyList<ScaleLoadFailure> Failures,
    IReadOnlyList<ScaleIdCollision> Collisions,
    string ScalesDirectory,
    bool ScalesDirectoryIsBesideExe,
    string Reason);

/// <summary>
/// Assembles the app's scale library from every source, at the precedence documented in the plan's
/// "Persistence and path probing" section: <c>Generated</c> (the 72 melakarta) &lt;
/// <c>Embedded</c> (the nine shipped assets) &lt; <c>BesideExe</c> (the <c>scales/</c> folder,
/// wherever it resolved to - beside the exe or the <c>%APPDATA%</c> fallback) &lt;
/// <c>UserDefined</c> (<c>user.scales.json</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>On first run, the nine embedded assets are copied into the writable <c>scales/</c> folder</b>
/// so a user can edit or add scale JSON without a rebuild - one file at a time, and only when that
/// file is absent, so an edited file is never overwritten by a later run.
/// </para>
/// <para>
/// <b>Once a scale id is readable from the <c>scales/</c> folder, the embedded copy of that id is
/// dropped before merging</b> - the folder is the live, user-editable copy of that scale from then
/// on, and after the first run it normally holds a copy of every embedded id. Feeding both an
/// unfiltered "Embedded" tier and the "BesideExe" tier straight into <see cref="ScaleLibrary.Build"/>
/// would report all 99 as id collisions on every single run, which is not a real conflict - it is
/// this loader's own materialisation, not two sources genuinely disagreeing. Filtering keeps
/// <see cref="ScaleLibrary.Collisions"/> meaningful: empty in the steady state, and non-empty only
/// when something has actually changed underneath a shipped id (a user scale, or a hand-edited
/// beside-exe file, claiming an id whose folder copy could not be parsed) or two independent sources
/// disagree. It also gives free resilience: if a folder copy is deleted or fails to parse, that id
/// still loads from the untouched in-memory embedded copy instead of vanishing.
/// </para>
/// <para>
/// Directory resolution uses <see cref="PathProbe.ResolveWritableRoot"/>, including the
/// <c>MIDIRESTYLE_DATA_ROOT</c> override: it decides between beside-the-exe and
/// <c>%APPDATA%\MIDIRestyle</c>, by attempting a real write, never by inspecting attributes. Both the
/// <c>scales/</c> folder and <c>user.scales.json</c> are read from that resolved root, even when
/// <see cref="WritableRootResult.IsWritable"/> is false - the location may be read-only rather than
/// wholly inaccessible, and an existing file there should still load.
/// </para>
/// <para>
/// Nothing here throws for bad data. A file that is not valid <c>midirestyle-scales-v1</c> JSON
/// contributes a whole-file <see cref="ScaleLoadFailure"/> and is otherwise skipped; a single invalid
/// scale inside an otherwise good file is reported by id while its siblings still load - both
/// courtesy of <see cref="ScaleJsonStore"/>, which already draws this distinction.
/// </para>
/// </remarks>
public sealed class ScaleLibraryLoader
{
    /// <summary>Name of the writable, user-editable folder holding copies of the embedded scales.</summary>
    public const string ScalesFolderName = "scales";

    /// <summary>Name of the user's own scale-library file, directly under the writable root.</summary>
    public const string UserScalesFileName = "user.scales.json";

    private readonly PathProbe _pathProbe;
    private readonly IReadOnlyList<EmbeddedScaleAsset>? _embeddedAssets;

    public ScaleLibraryLoader(PathProbe? pathProbe = null, IReadOnlyList<EmbeddedScaleAsset>? embeddedAssets = null)
    {
        _pathProbe = pathProbe ?? PathProbe.Default();
        _embeddedAssets = embeddedAssets;
    }

    /// <summary>
    /// Assembles the full scale library: generated melakarta, embedded assets (first-run-materialised
    /// into a writable <c>scales/</c> folder), and the user's own <c>user.scales.json</c> if present -
    /// in that ascending precedence. Never throws; bad data is reported on the result instead.
    /// </summary>
    public ScaleLibraryLoadResult Load()
    {
        var failures = new List<ScaleLoadFailure>();

        IReadOnlyList<Scale> generated = MelakartaGenerator.GenerateAll();

        IReadOnlyList<EmbeddedScaleAsset> embeddedAssets = _embeddedAssets ?? EmbeddedScaleAssets.ReadAll();
        List<Scale> embeddedScales = LoadScales(
            embeddedAssets.Select(a => (Label: $"(embedded:{a.FileName})", Json: (string?)a.Json)),
            failures);

        WritableRootResult resolved = _pathProbe.ResolveWritableRoot();
        string scalesDirectory = Path.Combine(resolved.Root, ScalesFolderName);
        string reason = DescribeScalesDirectory(resolved, scalesDirectory);

        MaterializeFirstRun(resolved, scalesDirectory, embeddedAssets, ref reason);

        (List<Scale> folderScales, HashSet<string> folderIds) = LoadFolderScales(scalesDirectory, failures);

        // Drop any embedded id already supplied by the folder - see the class remarks for why this
        // filtering, rather than passing both tiers unfiltered, is what keeps the steady state
        // collision-free.
        List<Scale> embeddedNotShadowed = [.. embeddedScales.Where(s => !folderIds.Contains(s.Id))];

        List<Scale> userScales = LoadUserScales(resolved.Root, failures);

        ScaleLibrary library = ScaleLibrary.Build(
            (ScaleOrigin.Generated, generated),
            (ScaleOrigin.Embedded, embeddedNotShadowed),
            (ScaleOrigin.BesideExe, folderScales),
            (ScaleOrigin.UserDefined, userScales));

        return new ScaleLibraryLoadResult(
            library,
            failures,
            library.Collisions,
            scalesDirectory,
            resolved.IsBesideExe,
            reason);
    }

    /// <summary>
    /// Copies every embedded asset into <paramref name="scalesDirectory"/> that is not already there -
    /// "first run" is defined by absence, not by any run counter, so a file the user has since edited
    /// is never touched again. No-op, with a stated reason appended, when the directory is not
    /// writable at all. Each file is written beside its destination and moved into place, so two
    /// processes racing to materialise the same first run see either no file or a complete one -
    /// never a torn read - and a per-file IO failure is caught and skipped rather than aborting the
    /// whole run.
    /// </summary>
    private static void MaterializeFirstRun(
        WritableRootResult resolved,
        string scalesDirectory,
        IReadOnlyList<EmbeddedScaleAsset> embeddedAssets,
        ref string reason)
    {
        if (!resolved.IsWritable)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(scalesDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason += $" Could not create '{scalesDirectory}': {ex.Message}";
            return;
        }

        foreach (EmbeddedScaleAsset asset in embeddedAssets)
        {
            string destination = Path.Combine(scalesDirectory, asset.FileName);
            if (File.Exists(destination))
            {
                continue;
            }

            // Write beside the target and move into place, so a second process starting at the
            // same moment sees either no file or a complete one - never a torn read.
            string temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, asset.Json);
                File.Move(temp, destination, overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!File.Exists(destination))
                {
                    reason += $" Could not write '{destination}': {ex.Message}";
                }
            }
            finally
            {
                TryDelete(temp);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; a stray temp file is harmless and is never read (it does not end in .json).
        }
    }

    /// <summary>
    /// Reads whatever <c>*.json</c> files currently sit in <paramref name="scalesDirectory"/> - the
    /// materialised embedded copies, plus anything a user added or edited by hand. Missing or
    /// unreadable directories yield an empty result rather than throwing; that is the expected shape
    /// when both candidate roots turned out to be unwritable.
    /// </summary>
    private static (List<Scale> Scales, HashSet<string> Ids) LoadFolderScales(
        string scalesDirectory,
        List<ScaleLoadFailure> failures)
    {
        var scales = new List<Scale>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(scalesDirectory))
        {
            return (scales, ids);
        }

        IEnumerable<(string Label, string? Json)> files;
        try
        {
            files = Directory.EnumerateFiles(scalesDirectory, "*.json")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(path => (Label: Path.GetFileName(path), Json: ReadFileOrNull(path, failures)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new ScaleLoadFailure($"({scalesDirectory})", $"could not be listed: {ex.Message}"));
            return (scales, ids);
        }

        foreach (Scale scale in LoadScales(files, failures))
        {
            scales.Add(scale);
            ids.Add(scale.Id);
        }

        return (scales, ids);
    }

    private static List<Scale> LoadUserScales(string root, List<ScaleLoadFailure> failures)
    {
        string path = Path.Combine(root, UserScalesFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        string? json = ReadFileOrNull(path, failures);
        return LoadScales([(Label: UserScalesFileName, Json: json)], failures);
    }

    private static string? ReadFileOrNull(string path, List<ScaleLoadFailure> failures)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new ScaleLoadFailure($"({Path.GetFileName(path)})", $"could not be read: {ex.Message}"));
            return null;
        }
    }

    /// <summary>
    /// Parses each (label, json) pair via <see cref="ScaleJsonStore"/>, reporting a whole-file failure
    /// for unreadable content or a document that fails schema validation, and a per-scale failure
    /// (by id) for any single invalid entry - without losing the rest of that file's scales.
    /// </summary>
    private static List<Scale> LoadScales(
        IEnumerable<(string Label, string? Json)> files,
        List<ScaleLoadFailure> failures)
    {
        var scales = new List<Scale>();

        foreach ((string label, string? json) in files)
        {
            if (json is null)
            {
                // The read itself already failed and was reported by the caller.
                continue;
            }

            ScaleJsonLoadResult result = ScaleJsonStore.LoadFromString(json);
            if (result.FileError is not null)
            {
                failures.Add(new ScaleLoadFailure(label, result.FileError));
                continue;
            }

            scales.AddRange(result.Scales);
            foreach (ScaleLoadFailure failure in result.Failures)
            {
                failures.Add(new ScaleLoadFailure($"{failure.Id} {label}", failure.Reason));
            }
        }

        return scales;
    }

    private static string DescribeScalesDirectory(WritableRootResult resolved, string scalesDirectory)
    {
        string where = resolved.IsBesideExe ? "beside the exe" : "%APPDATA%";
        string writable = resolved.IsWritable
            ? $"Scales folder '{scalesDirectory}' ({where})."
            : $"Scales folder '{scalesDirectory}' ({where}, not writable).";
        return $"{writable} {resolved.Reason}";
    }
}
