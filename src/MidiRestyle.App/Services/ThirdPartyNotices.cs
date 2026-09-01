using System.IO;
using System.Reflection;

namespace MidiRestyle.App.Services;

/// <summary>
/// The licence notices for everything the shipped executable redistributes.
/// </summary>
/// <remarks>
/// Read from an embedded resource rather than from a file beside the exe, and that is forced
/// rather than preferred. <c>AssertSingleFilePublish</c> requires the publish folder to hold
/// exactly one file, so a notices file cannot sit next to the binary - while the obligation it
/// discharges is a real one: the Inter faces compiled into the exe are under the SIL Open Font
/// License, which requires the licence be distributed with the font. The text therefore has to
/// travel inside the executable, which means an embedded resource and a way to read it back.
///
/// Deliberately *not* written out beside the exe on first run the way <c>scales/</c> is. That
/// folder is materialised because the user is meant to edit it; this is a fixed document that only
/// ever needs reading, and dropping 230 KB of licence text next to the binary to satisfy a notice
/// nobody has asked to see is the wrong trade.
/// </remarks>
public static class ThirdPartyNotices
{
    /// <summary>
    /// The manifest resource name, fixed by <c>LogicalName</c> in the csproj.
    /// </summary>
    /// <remarks>
    /// Pinned as a constant so the test asserting the resource is present names the same string
    /// the loader looks for. A rename in the csproj that forgot this would otherwise fail only at
    /// runtime, in a window most people never open - which is the worst place to discover it.
    /// </remarks>
    internal const string ResourceName = "MIDIRestyle.THIRD-PARTY-NOTICES.txt";

    private static readonly Lazy<string> LazyText = new(Load);

    /// <summary>The full notices document.</summary>
    public static string Text => LazyText.Value;

    private static string Load()
    {
        Assembly assembly = typeof(ThirdPartyNotices).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            // Unreachable in a correctly built app, and a test pins that. It does not throw
            // because an About-box link that takes the application down is worse than one that
            // explains itself - but it does not quietly show an empty window either: a missing
            // notice is a licence breach, not a cosmetic defect, so it says so.
            return "The third-party notices are missing from this build of MIDIRestyle. "
                + "This is a packaging fault. The notices are published as THIRD-PARTY-NOTICES.txt "
                + "in the MIDIRestyle source repository.";
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
