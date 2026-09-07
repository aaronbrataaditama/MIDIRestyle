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
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
