using MidiRestyle.Core.Scales;

namespace MidiRestyle.Mcp.Tests;

/// <summary>The real library (99 authored + 72 generated), loaded once under a temp data root so no
/// test ever writes beside the test binaries.</summary>
internal static class TestLibrary
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "midirestyle-mcp-tests-" + Guid.NewGuid().ToString("N"));

    public static PathProbe Probe { get; } = new(
        besideExeDirectory: Path.Combine(Root, "beside"),
        appDataDirectory: Path.Combine(Root, "appdata"),
        overrideRoot: Path.Combine(Root, "data"));

    static TestLibrary()
    {
        // The GUID in Root makes this safe under parallel test processes; ProcessExit is what keeps
        // it from accumulating in %TEMP% run after run. Best-effort: a locked file (e.g. an
        // antivirus scan mid-delete) must never fail the test run over cleanup.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDeleteRoot();
    }

    private static void TryDeleteRoot()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup only; a leftover temp directory is harmless.
        }
    }

    private static readonly Lazy<ScaleLibrary> Instance = new(() => new ScaleLibraryLoader(Probe).Load().Library);

    public static ScaleLibrary Load() => Instance.Value;

    public static Scale Rast => Load().Find("middleeast.arabic.maqam-rast")!;
    public static Scale Slendro => Load().Find("seasia.gamelan.slendro-kanyut-mesem")!;
    public static Scale Ionian => Load().Find("europe.churchmodes.ionian")!;
    public static Scale Aeolian => Load().Find("europe.churchmodes.aeolian")!;
}
