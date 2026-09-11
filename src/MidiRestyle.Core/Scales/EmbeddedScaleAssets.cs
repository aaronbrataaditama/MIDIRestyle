using System.Reflection;
using System.Text;

namespace MidiRestyle.Core.Scales;

/// <summary>One embedded scale-library JSON file: its file name and raw content.</summary>
public sealed record EmbeddedScaleAsset(string FileName, string Json);

/// <summary>
/// Reads the authored scale-library files compiled into this assembly as manifest resources. This
/// replaced an Avalonia <c>avares://</c> loader that threw without an initialised UI platform and so
/// could never run under <c>dotnet test</c> or in the headless MCP server.
/// </summary>
public static class EmbeddedScaleAssets
{
    public const string ResourcePrefix = "MidiRestyle.Core.Scales.Data.";

    public static IReadOnlyList<EmbeddedScaleAsset> ReadAll()
    {
        Assembly assembly = typeof(EmbeddedScaleAssets).Assembly;
        var assets = new List<EmbeddedScaleAsset>();

        foreach (string name in assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Manifest resource '{name}' listed but not readable.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            assets.Add(new EmbeddedScaleAsset(name[ResourcePrefix.Length..], reader.ReadToEnd()));
        }

        return assets;
    }
}
