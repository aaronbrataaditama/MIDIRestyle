using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MidiRestyle.App.ViewModels;

/// <summary>
/// The content of the About window.
/// </summary>
/// <remarks>
/// A view model with no mutable state, which is the point: every string and both URLs live here
/// rather than as literals in the markup, so a test can assert them. A mistyped donation link is
/// invisible to the compiler, survives a rendering pass looking perfectly correct, and only fails
/// when someone tries to give the author money - which is precisely the class of mistake worth
/// pinning down mechanically rather than proof-reading.
/// </remarks>
public sealed partial class AboutViewModel : ObservableObject
{
    /// <summary>The canonical MIT licence text.</summary>
    /// <remarks>
    /// Points at the Open Source Initiative rather than a copy inside this repository: the project
    /// has no published remote for a LICENSE file to be linked to, and the OSI page is the
    /// authoritative wording of the licence in any case.
    /// </remarks>
    public const string LicenseUrl = "https://opensource.org/license/mit";

    /// <summary>The author's donation page.</summary>
    public const string DonationUrl = "https://paypal.me/aaronaditama";

    /// <summary>What the application actually does, in three paragraphs.</summary>
    /// <remarks>
    /// The second paragraph earns its place: "pitch only, in cents" is the whole design constraint
    /// of the app, and it is what separates it from a transposer. Someone reading this box should
    /// come away knowing the rhythm is untouched and that microtonal scales are not being rounded
    /// into 12-TET caricatures of themselves.
    /// </remarks>
    public const string Description =
        "MIDIRestyle re-maps the musical scale of a MIDI file. It takes a piece written in Western " +
        "major or minor and rewrites its pitches into Chinese Gong pentatonic, Maqam Rast, Gamelan " +
        "Slendro, a Carnatic melakarta, or any of around 170 other scales from around the world." +
        "\n\n" +
        "It changes pitch and nothing else - never rhythm, ornamentation or articulation. " +
        "Internally it works in cents rather than semitones, so genuinely microtonal tunings sound " +
        "in tune instead of being rounded off to the nearest piano key." +
        "\n\n" +
        "Load a file, pick a target scale, then compare the original against the restyled version " +
        "as a piano roll, on a staff, or on a scale wheel - and export the result as MIDI or " +
        "MusicXML.";

    /// <summary>The licence sentence, with <see cref="LicenseLinkText"/> shown beside it as a link.</summary>
    public const string LicenseLine = "Free and open-source software, released under the";

    public const string LicenseLinkText = "MIT License";

    /// <summary>
    /// The sentence pointing at the bundled third-party licences, with
    /// <see cref="NoticesLinkText"/> shown beside it as a link.
    /// </summary>
    /// <remarks>
    /// Named explicitly here rather than left implicit under "MIT License" above, because the two
    /// are different claims and conflating them is the mistake worth preventing: the MIT line is
    /// about MIDIRestyle's own code, while this one is about everybody else's, which happens to
    /// include a typeface under a licence that requires the notice be shown at all.
    /// </remarks>
    public const string NoticesLine =
        "The executable also bundles its dependencies and the .NET runtime - see the";

    public const string NoticesLinkText = "third-party notices";

    /// <summary>The donation ask, kept to the small print at the foot of the window.</summary>
    public const string DonationPrompt =
        "If you find this tool useful, please consider donating to my PayPal account:";

    public const string DonationLinkText = "paypal.me/aaronaditama";

    /// <summary>Why a link did not open, or null when nothing has gone wrong.</summary>
    /// <remarks>
    /// The one piece of state this view model has. It exists because launching a URL is the only
    /// thing the About window does that can fail, and a link that silently does nothing when
    /// clicked is indistinguishable from a broken application.
    /// </remarks>
    [ObservableProperty]
    private string? _launchError;

    public string Title => "MIDIRestyle";

    public string VersionLine => $"Version {DisplayVersion}";

    /// <summary>
    /// The shipping version, read from the assembly rather than typed into this file.
    /// </summary>
    /// <remarks>
    /// Cut at the '+' because <c>InformationalVersion</c> carries the source revision id after one
    /// whenever the build has repository information: "1.3.0+398e5ad..." is a build identifier, not
    /// something to show a user. Falls back to the plain assembly version if the attribute is
    /// missing. Note this reads an attribute, never <c>Assembly.Location</c> - that returns an empty
    /// string under PublishSingleFile and is banned for the whole solution.
    /// </remarks>
    public static string DisplayVersion { get; } = ReadVersion();

    private static string ReadVersion()
    {
        Assembly assembly = typeof(AboutViewModel).Assembly;

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
