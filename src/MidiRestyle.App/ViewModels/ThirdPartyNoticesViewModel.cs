using MidiRestyle.App.Services;

namespace MidiRestyle.App.ViewModels;

/// <summary>
/// The content of the third-party notices window.
/// </summary>
/// <remarks>
/// Immutable and dependency-free, like <see cref="AboutViewModel"/>: the window shows one fixed
/// document and does nothing to it. The indirection earns its place only by keeping the intro
/// wording out of the markup where a test cannot reach it.
/// </remarks>
public sealed class ThirdPartyNoticesViewModel
{
    /// <summary>
    /// Why this window exists, in the two sentences a reader needs to place the document.
    /// </summary>
    /// <remarks>
    /// Says "in addition to" rather than merely listing the notices, because the distinction
    /// people actually need is between MIDIRestyle's own licence and everybody else's - and an
    /// undifferentiated wall of licence text invites exactly the wrong conclusion about which is
    /// which.
    /// </remarks>
    public const string Intro =
        "MIDIRestyle ships as a single self-contained executable, so it carries its dependencies "
        + "and the .NET runtime inside it. Those are other people's software, under their own "
        + "licences, reproduced here in full. MIDIRestyle's own licence is the MIT License shown "
        + "in the About box.";

    /// <summary>The full notices document, as embedded in the executable.</summary>
    public string Text => ThirdPartyNotices.Text;
}
