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

    private static readonly Lazy<ScaleLibrary> Instance = new(() => new ScaleLibraryLoader(Probe).Load().Library);

    public static ScaleLibrary Load() => Instance.Value;

    public static Scale Rast => Load().Find("middleeast.arabic.maqam-rast")!;
    public static Scale Slendro => Load().Find("seasia.gamelan.slendro-kanyut-mesem")!;
    public static Scale Ionian => Load().Find("europe.churchmodes.ionian")!;
    public static Scale Aeolian => Load().Find("europe.churchmodes.aeolian")!;
}
