using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MidiRestyle.App.ViewModels;
using MidiRestyle.App.Views;

namespace MidiRestyle.App.Tests;

/// <summary>
/// The Agent access window loads its markup, binds its view model, shows and renders.
/// </summary>
/// <remarks>
/// <para>
/// Scoped the same way as <see cref="AboutWindowRenderTests"/>, and for the same reason: this window
/// sits behind a menu item that nothing else in the suite opens, so without a test that actually
/// constructs it, it could throw on every click while the suite stayed green. A bad <c>Icon</c>
/// resource is the concrete case - it fails with <c>FileNotFoundException</c> at show time and the
/// XAML compiler does not catch it.
/// </para>
/// <para>
/// It goes further than the About window's smoke test in one respect that matters here: it asserts
/// the three snippets are actually on screen, carrying the path the view model was given. The whole
/// purpose of the dialog is showing a path the user can copy, and a window that renders perfectly
/// with three empty boxes would satisfy a frame-capture check.
/// </para>
/// </remarks>
public class AgentAccessWindowRenderTests
{
    private const string InjectedPath = @"Q:\Render Fixture\Renamed MIDIRestyle.exe";

    [Fact]
    public void TheAgentAccessWindowLoadsShowsAndRenders() => AvaloniaRenderFixture.Run(() =>
    {
        AgentAccessViewModel viewModel = new(InjectedPath);
        AgentAccessWindow window = new(viewModel);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.Width.Should().Be(640, "the dialog is fixed-width, like the About box");
            window.Height.Should().BeGreaterThan(300, "the window must not render collapsed");

            using Bitmap frame = window.CaptureRenderedFrame()!;
            frame.PixelSize.Width.Should().Be(640);
            frame.PixelSize.Height.Should().Be((int)window.Height);
        }
        finally
        {
            // Closed even on failure: a window left showing outlives this test, and the next render
            // test in the assembly would inherit it.
            window.Close();
        }
    });

    [Fact]
    public void AllThreeSnippetsAreOnScreenCarryingThisExesPath() => AvaloniaRenderFixture.Run(() =>
    {
        AgentAccessViewModel viewModel = new(InjectedPath);
        AgentAccessWindow window = new(viewModel);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            string[] shown =
            [
                .. window.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text ?? string.Empty)
            ];

            shown.Should().HaveCount(3, "one read-only box per snippet");

            // Compared against the view model rather than against literals: the view model is what
            // the tests above pin, and restating its output here would let the two drift.
            shown.Should().Contain(viewModel.ClaudeCodeCommand);
            shown.Should().Contain(viewModel.ClaudeDesktopJson);
            shown.Should().Contain(viewModel.GenericJson);

            // The one property of those strings that matters to a user: every box names this exe.
            // Asserted separately so a failure reads "the path is missing" rather than "three
            // strings differ". The filename is the part that survives both encodings - the two JSON
            // boxes carry the path with its backslashes escaped, which is correct, and is exactly
            // why a blanket "contains the raw path" check over all three is wrong.
            shown.Should().OnlyContain(text => text.Contains("Renamed MIDIRestyle.exe", StringComparison.Ordinal));

            shown.Should()
                .ContainSingle(text => text.Contains(InjectedPath, StringComparison.Ordinal))
                .Which.Should().Be(
                    viewModel.ClaudeCodeCommand,
                    "only the command line carries the path unescaped - JSON escapes its backslashes");

            // Read-only, but selectable: Ctrl+C is the fallback the copy-failure message points at,
            // and it only exists if these are text boxes rather than labels.
            window.GetVisualDescendants().OfType<TextBox>().Should().OnlyContain(box => box.IsReadOnly);
        }
        finally
        {
            window.Close();
        }
    });
}
