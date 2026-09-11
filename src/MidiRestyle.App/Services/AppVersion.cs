using System.Reflection;

namespace MidiRestyle.App.Services;

/// <summary>
/// The version shown to people: the About box, <c>--version</c>, and the MCP server's
/// <c>ServerInfo</c>. Read from the informational-version attribute with the <c>+&lt;commit&gt;</c>
/// build metadata removed; never from <c>Assembly.Location</c>, which is empty under single-file publish.
/// </summary>
public static class AppVersion
{
    public static string Display { get; } = Read();

    private static string Read()
    {
        Assembly assembly = typeof(AppVersion).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return StripBuildMetadata(informational);
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    /// <summary>
    /// Strips the <c>+&lt;build-metadata&gt;</c> from an informational version string.
    /// </summary>
    /// <param name="informationalVersion">A version string like "1.5.0" or "1.5.0+abc123def..."</param>
    /// <returns>The version without build metadata, e.g., "1.5.0"</returns>
    internal static string StripBuildMetadata(string informationalVersion)
    {
        int plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informationalVersion : informationalVersion[..plus];
    }
}
