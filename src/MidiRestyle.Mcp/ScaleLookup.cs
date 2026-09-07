using System.Diagnostics.CodeAnalysis;
using MidiRestyle.Core.Scales;

namespace MidiRestyle.Mcp;

public static class ScaleLookup
{
    public static bool TryFind(ScaleLibrary library, string id, string parameterName,
        [NotNullWhen(true)] out Scale? scale, [NotNullWhen(false)] out string? error)
    {
        scale = string.IsNullOrWhiteSpace(id) ? null : library.Find(id);
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
