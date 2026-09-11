using System.Diagnostics.CodeAnalysis;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.Mcp;

public static class ScaleLookup
{
    public static bool TryFind(ScaleLibrary library, string id, string parameterName,
        [NotNullWhen(true)] out Scale? scale, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            // Search returns the whole library for a blank query, so falling through would answer a
            // missing id with three arbitrary scales dressed up as near matches.
            scale = null;
            error = $"{parameterName} is required; use list_scales to search.";
            return false;
        }

        scale = library.Find(id);
        if (scale is not null)
        {
            error = null;
            return true;
        }

        string[] near = [.. library.Search(id).Take(3).Select(s => s.Id)];
        error = near.Length == 0
            ? $"{parameterName} '{id}' is not a known scale id; use list_scales to search."
            : $"{parameterName} '{id}' is not a known scale id. Did you mean: {string.Join(", ", near)}?";
        return false;
    }
}
