using System.Text.Json;

namespace MidiRestyle.Mcp;

/// <summary>
/// Enum-like tool parameters are strings so that we, not the SDK's binder, own the error text. Input is
/// matched case-insensitively with underscores ignored (<c>scale_degree</c>, <c>scaleDegree</c>);
/// output and the "valid values" list are camelCase, matching <see cref="McpJson"/>.
/// </summary>
public static class EnumNames
{
    public static bool TryParse<TEnum>(string text, out TEnum value) where TEnum : struct, Enum
    {
        string wanted = Normalise(text);
        foreach (TEnum candidate in Enum.GetValues<TEnum>())
        {
            if (Normalise(candidate.ToString()) == wanted)
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static string Echo<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    public static IReadOnlyList<string> Valid<TEnum>() where TEnum : struct, Enum =>
        [.. Enum.GetValues<TEnum>().Select(Echo)];

    private static string Normalise(string s) => s.Replace("_", "", StringComparison.Ordinal).ToUpperInvariant();
}
