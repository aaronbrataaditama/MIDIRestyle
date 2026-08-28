using System.Text.Json;
using System.Text.Json.Serialization;

namespace MidiRestyle.Core.Scales;

/// <summary>
/// Why one <see cref="Scale"/> entry in a JSON scale library failed to load. Carries the offending
/// scale's <c>id</c> (or a positional label when the id itself could not be read) plus a
/// user-displayable reason, so a bad entry can be reported without hiding the rest of the library.
/// </summary>
public sealed record ScaleLoadFailure(string Id, string Reason);

/// <summary>
/// The outcome of loading a <c>midirestyle-scales-v1</c> JSON document: whatever scales parsed and
/// validated successfully, plus a per-entry list of the ones that did not. When the document itself
/// is unreadable - not JSON at all, or not shaped like a scale library - <see cref="FileError"/> is
/// set and both lists are empty, since there is nothing to salvage.
/// </summary>
public sealed record ScaleJsonLoadResult
{
    private ScaleJsonLoadResult(
        IReadOnlyList<Scale> scales,
        IReadOnlyList<ScaleLoadFailure> failures,
        string? fileError)
    {
        Scales = scales;
        Failures = failures;
        FileError = fileError;
    }

    /// <summary>Scales that parsed and validated successfully.</summary>
    public IReadOnlyList<Scale> Scales { get; }

    /// <summary>Per-scale failures: entries present in the file that did not load.</summary>
    public IReadOnlyList<ScaleLoadFailure> Failures { get; }

    /// <summary>
    /// Set when the document as a whole could not be read - malformed JSON, a non-object root, a
    /// missing/wrong <c>schema</c>, or a missing/non-array <c>scales</c> property. Null otherwise.
    /// </summary>
    public string? FileError { get; }

    internal static ScaleJsonLoadResult Ok(IReadOnlyList<Scale> scales, IReadOnlyList<ScaleLoadFailure> failures) =>
        new(scales, failures, null);

    internal static ScaleJsonLoadResult FileFailure(string reason) => new([], [], reason);
}

/// <summary>
/// JSON load/save for scale definitions, against the fixed <c>midirestyle-scales-v1</c> schema:
/// <c>{ "schema": "...", "scales": [ { "id", "name", "tradition", "region", "degreeCents",
/// "notatable", "source", "description"?, "spelling"? } ] }</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Malformed input is user input, not programmer error.</b> Loading never throws for bad JSON,
/// a wrong schema, or an invalid scale - it reports a stated reason instead, matching the result-type
/// style used by <see cref="ScalaFileReader"/>. A single invalid scale does not abort the whole file:
/// every other entry still loads, and the bad one is reported by id. Only genuine IO failure (a file
/// that cannot be read) is allowed to throw, from <see cref="LoadFromFile"/>.
/// </para>
/// <para>
/// Serialization uses a source-generated <see cref="JsonSerializerContext"/> - no reflection-based
/// (de)serialization - via <see cref="ScaleJsonContext"/>. Unknown JSON properties are ignored
/// (System.Text.Json's default), so the format can grow without breaking old files.
/// </para>
/// </remarks>
public static class ScaleJsonStore
{
    /// <summary>The only <c>schema</c> value this store accepts.</summary>
    public const string SchemaVersion = "midirestyle-scales-v1";

    /// <summary>
    /// Parses scale-library JSON already in memory. The primary entry point - needs no filesystem,
    /// so tests can exercise every rule without fixture files.
    /// </summary>
    public static ScaleJsonLoadResult LoadFromString(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ScaleJsonLoadResult.FileFailure($"the file is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ScaleJsonLoadResult.FileFailure(
                    "the top level of the file must be a JSON object with 'schema' and 'scales' properties.");
            }

            if (!root.TryGetProperty("schema", out var schemaProp) || schemaProp.ValueKind != JsonValueKind.String)
            {
                return ScaleJsonLoadResult.FileFailure("missing or non-string 'schema' property.");
            }

            string schema = schemaProp.GetString() ?? "";
            if (schema != SchemaVersion)
            {
                return ScaleJsonLoadResult.FileFailure(
                    $"unrecognised schema '{schema}' - expected '{SchemaVersion}'.");
            }

            if (!root.TryGetProperty("scales", out var scalesProp) || scalesProp.ValueKind != JsonValueKind.Array)
            {
                return ScaleJsonLoadResult.FileFailure("missing or non-array 'scales' property.");
            }

            var scales = new List<Scale>();
            var failures = new List<ScaleLoadFailure>();

            int index = 0;
            foreach (var element in scalesProp.EnumerateArray())
            {
                index++;
                string positionalLabel = $"(scales[{index - 1}])";

                ScaleDto? dto;
                try
                {
                    dto = element.Deserialize(ScaleJsonContext.Default.ScaleDto);
                }
                catch (JsonException ex)
                {
                    failures.Add(new ScaleLoadFailure(positionalLabel, $"could not be parsed: {ex.Message}"));
                    continue;
                }

                if (dto is null)
                {
                    failures.Add(new ScaleLoadFailure(positionalLabel, "is null."));
                    continue;
                }

                string id = string.IsNullOrWhiteSpace(dto.Id) ? positionalLabel : dto.Id;

                try
                {
                    var scale = new Scale(
                        id: dto.Id,
                        name: dto.Name,
                        tradition: dto.Tradition,
                        region: dto.Region,
                        degreeCents: dto.DegreeCents,
                        source: dto.Source,
                        notatable: dto.Notatable,
                        spelling: dto.Spelling?.Select(ToSpelling).ToList(),
                        description: dto.Description);

                    scales.Add(scale);
                }
                catch (ScaleValidationException ex)
                {
                    // Surfaced verbatim: the exception's own message already explains the downstream
                    // consequence (e.g. why a degree at 1200 cents is refused), which is more useful
                    // to a user than a generic re-wording would be.
                    failures.Add(new ScaleLoadFailure(id, ex.Message));
                }
                catch (ArgumentException ex)
                {
                    // Missing id/name, or a null degreeCents array - malformed shape rather than a
                    // domain-validation failure, but still user input, not a bug.
                    failures.Add(new ScaleLoadFailure(id, ex.Message));
                }
            }

            return ScaleJsonLoadResult.Ok(scales, failures);
        }
    }

    /// <summary>Reads and parses a scale-library JSON file.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be read.</exception>
    public static ScaleJsonLoadResult LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = File.ReadAllText(path);
        return LoadFromString(json);
    }

    /// <summary>Serializes a set of scales back to <c>midirestyle-scales-v1</c> JSON.</summary>
    public static string SaveToString(IEnumerable<Scale> scales)
    {
        ArgumentNullException.ThrowIfNull(scales);

        var dto = new ScaleFileDto
        {
            Schema = SchemaVersion,
            Scales = scales.Select(ToDto).ToList(),
        };

        return JsonSerializer.Serialize(dto, ScaleJsonContext.Default.ScaleFileDto);
    }

    /// <summary>Serializes a set of scales and writes them to a file.</summary>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
    public static void SaveToFile(string path, IEnumerable<Scale> scales)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(scales);

        File.WriteAllText(path, SaveToString(scales));
    }

    private static DegreeSpelling ToSpelling(DegreeSpellingDto dto) =>
        new(dto.Step, dto.Alter, dto.ResidualCents);

    private static ScaleDto ToDto(Scale scale) => new()
    {
        Id = scale.Id,
        Name = scale.Name,
        Tradition = scale.Tradition,
        Region = scale.Region,
        DegreeCents = [.. scale.DegreeCents],
        Notatable = scale.Notatable,
        Source = scale.Source,
        Description = scale.Description,
        Spelling = scale.Spelling?.Select(s => new DegreeSpellingDto
        {
            Step = s.DiatonicStep,
            Alter = s.Alter,
            ResidualCents = s.ResidualCents,
        }).ToList(),
    };
}

/// <summary>Wire shape of the whole document: <c>{ "schema", "scales": [...] }</c>.</summary>
internal sealed class ScaleFileDto
{
    public string Schema { get; set; } = "";

    public List<ScaleDto> Scales { get; set; } = [];
}

/// <summary>Wire shape of one scale entry.</summary>
internal sealed class ScaleDto
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Tradition { get; set; } = "";

    public string Region { get; set; } = "";

    public List<double> DegreeCents { get; set; } = [];

    public bool Notatable { get; set; }

    public string Source { get; set; } = "";

    public string? Description { get; set; }

    public List<DegreeSpellingDto>? Spelling { get; set; }
}

/// <summary>Wire shape of one <see cref="DegreeSpelling"/> override entry.</summary>
internal sealed class DegreeSpellingDto
{
    public int Step { get; set; }

    public double Alter { get; set; }

    public double ResidualCents { get; set; }
}

/// <summary>
/// Source-generated serialization metadata for the scale-library JSON format. Reflection-based
/// (de)serialization is avoided so the store stays trim/AOT-friendly and predictable under
/// <c>PublishSingleFile</c>.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ScaleFileDto))]
[JsonSerializable(typeof(ScaleDto))]
[JsonSerializable(typeof(DegreeSpellingDto))]
internal partial class ScaleJsonContext : JsonSerializerContext
{
}
