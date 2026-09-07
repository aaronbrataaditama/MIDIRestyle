using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MidiRestyle.Core.Analysis;
using MidiRestyle.Core.Io;
using MidiRestyle.Core.Mapping;
using MidiRestyle.Core.Model;
using MidiRestyle.Core.Restyle;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.Mcp;

public sealed record Resolution(
    MidiProject Project,
    RestyleSettings Settings,
    ResolvedSettings Resolved,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a <see cref="RestyleRequest"/> into the <see cref="RestyleSettings"/> the GUI would have built
/// for the same choices - same defaults (<see cref="RestyleDefaults"/>), same key-detection candidate,
/// same drums rule. Error precedence: every field problem is reported together first (one round trip
/// for the agent), then the file load, then key detection.
/// </summary>
public sealed class RestyleRequestResolver(ScaleLibrary library)
{
    public bool TryResolve(RestyleRequest request, [NotNullWhen(true)] out Resolution? resolution, [NotNullWhen(false)] out string? error)
    {
        resolution = null;
        var errors = new List<string>();
        var warnings = new List<string>();

        if (OutputPathPolicy.ValidateInputPath(request.InputPath) is { } inputError)
        {
            errors.Add(inputError);
        }

        ScaleLookup.TryFind(library, request.TargetScaleId, "targetScaleId", out Scale? target, out string? targetError);
        if (targetError is not null) { errors.Add(targetError); }

        Scale? source = null;
        if (request.SourceScaleId is not null && !ScaleLookup.TryFind(library, request.SourceScaleId, "sourceScaleId", out source, out string? sourceError))
        {
            errors.Add(sourceError);
        }

        ParsedTonic? targetTonic = ParseTonic(request.TargetTonic, "targetTonic", errors);
        ParsedTonic? sourceTonic = ParseTonic(request.SourceTonic, "sourceTonic", errors);

        MappingStrategy strategy = ParseEnum(request.Strategy, "strategy", MappingOptions.Default.Strategy, errors);
        NonScaleNotePolicy nonScale = ParseEnum(request.NonScaleNotes, "nonScaleNotes", MappingOptions.Default.NonScaleNotes, errors);
        CollisionPolicy collisions = ParseEnum(request.Collisions, "collisions", MappingOptions.Default.Collisions, errors);
        RangePolicy range = ParseEnum(request.Range, "range", MappingOptions.Default.Range, errors);

        double tolerance = request.ToleranceCents ?? RestyleDefaults.ToleranceCents;
        if (tolerance < RestyleDefaults.MinToleranceCents || tolerance > RestyleDefaults.MaxToleranceCents)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture,
                $"toleranceCents {tolerance} is outside {RestyleDefaults.MinToleranceCents}..{RestyleDefaults.MaxToleranceCents}."));
        }

        if (request.Exclude?.Any(e => e.Track < 0 || e.Channel is < 0 or > 15) == true)
        {
            errors.Add("exclude entries need track >= 0 and channel 0..15.");
        }

        if (errors.Count > 0)
        {
            error = Join(errors);
            return false;
        }

        if (!MidiFileLoader.TryLoad(request.InputPath, out MidiProject? project, out string? loadError) || project is null)
        {
            error = $"Could not load '{request.InputPath}': {loadError}";
            return false;
        }

        var excluded = new HashSet<(int Track, int Channel)>();
        foreach (TrackChannelRef e in request.Exclude ?? [])
        {
            if (!project.Tracks.Any(t => t.TrackIndex == e.Track && t.Channel == e.Channel))
            {
                string valid = string.Join(", ", project.Tracks.Select(t => $"track {t.TrackIndex}, channel {t.Channel}"));
                error = $"exclude names track {e.Track}, channel {e.Channel}, which the file does not have. It has: {valid}.";
                return false;
            }

            excluded.Add((e.Track, e.Channel));
        }

        bool usesSource = strategy == MappingStrategy.ScaleDegree;
        if (!usesSource && (request.SourceScaleId is not null || request.SourceTonic is not null))
        {
            warnings.Add("sourceScaleId/sourceTonic were ignored: strategy nearestPitch does not use a source scale.");
            source = null;
            sourceTonic = null;
        }

        bool needsKey = targetTonic is null || (usesSource && (source is null || sourceTonic is null));
        KeyEstimate? key = null;
        if (needsKey)
        {
            KeyDetectionResult detection = KeyDetector.Detect(project);
            key = RestyleDefaults.ChosenCandidate(detection);
            if (!detection.HasKey || key is null)
            {
                var missing = new List<string>();
                if (targetTonic is null) { missing.Add("targetTonic"); }
                if (usesSource && source is null) { missing.Add("sourceScaleId"); }
                if (usesSource && sourceTonic is null) { missing.Add("sourceTonic"); }
                error = $"No key could be detected in '{request.InputPath}', so {string.Join(", ", missing)} must be supplied.";
                return false;
            }

            if (detection.IsAmbiguous && detection.Candidates.Count > 1)
            {
                warnings.Add(string.Create(CultureInfo.InvariantCulture,
                    $"Key detection was ambiguous: used {key.Name} over {detection.Candidates[1].Name} (margin {detection.Margin:0.###})."));
            }
        }

        targetTonic ??= TonicParser.FromDetected(key!.PitchClass, RestyleDefaults.TonicOctave);
        if (usesSource)
        {
            if (source is null)
            {
                string id = RestyleDefaults.SourceScaleIdFor(key!);
                source = library.Find(id);
                if (source is null)
                {
                    error = $"The scale library has no '{id}' to use as the source scale; supply sourceScaleId.";
                    return false;
                }
            }

            sourceTonic ??= TonicParser.FromDetected(key!.PitchClass, RestyleDefaults.TonicOctave);
        }

        var settings = new RestyleSettings
        {
            TargetScale = target!,
            TargetTonic = targetTonic.Pitch,
            TonicSpelling = targetTonic.Spelling,
            SourceScale = usesSource ? source : null,
            SourceTonic = usesSource ? sourceTonic!.Pitch : default,
            Mapping = new MappingOptions { Strategy = strategy, NonScaleNotes = nonScale, Collisions = collisions, Range = range },
            ToleranceCents = tolerance,
            Excluded = excluded,
        };

        if (!project.Tracks.Any(settings.ShouldRestyle))
        {
            warnings.Add("No notes will be restyled: every track-channel with notes is drums or excluded.");
        }

        var resolved = new ResolvedSettings(
            target!.Id,
            new TonicEcho(targetTonic.Name, targetTonic.Midi),
            usesSource ? source!.Id : null,
            usesSource ? new TonicEcho(sourceTonic!.Name, sourceTonic.Midi) : null,
            [.. project.Tracks.Where(t => t.NoteCount > 0 && !settings.ShouldRestyle(t)).Select(t => new TrackChannelRef(t.TrackIndex, t.Channel))],
            new MappingEcho(EnumNames.Echo(strategy), EnumNames.Echo(nonScale), EnumNames.Echo(collisions), EnumNames.Echo(range)),
            tolerance,
            KeyDetectionUsed: needsKey);

        resolution = new Resolution(project, settings, resolved, warnings);
        error = null;
        return true;
    }

    private static ParsedTonic? ParseTonic(string? text, string name, List<string> errors)
    {
        if (text is null) { return null; }
        if (TonicParser.TryParse(text, out ParsedTonic? tonic, out string? err)) { return tonic; }
        errors.Add($"{name}: {err}");
        return null;
    }

    private static TEnum ParseEnum<TEnum>(string? text, string name, TEnum fallback, List<string> errors) where TEnum : struct, Enum
    {
        if (text is null) { return fallback; }
        if (EnumNames.TryParse(text, out TEnum value)) { return value; }
        errors.Add($"{name} '{text}' is not valid; use one of: {string.Join(", ", EnumNames.Valid<TEnum>())}.");
        return fallback;
    }

    private static string Join(List<string> errors) =>
        errors.Count == 1 ? errors[0] : $"The request has {errors.Count} problems:\n- " + string.Join("\n- ", errors);
}
