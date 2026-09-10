using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
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
            // A structural floor, not a snug one. The window renders at 307px, so the old 300
            // left 2.3% of headroom - under one 19px line of the explanation paragraph - and
            // trimming a line of prose would have reddened the suite complaining about a
            // collapsed window. The paragraph itself is pinned by TheExplanationParagraphIsOnScreen.
            window.Height.Should().BeGreaterThan(150, "the window must not render collapsed");

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
    public void TheExplanationParagraphIsOnScreen() => AvaloniaRenderFixture.Run(() =>
    {
        AgentAccessViewModel viewModel = new(InjectedPath);
        AgentAccessWindow window = new(viewModel);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Asserted directly rather than left to the smoke test's height check. Blanking this
            // binding does shrink the window below the 300px floor today, so that assertion happens
            // to catch it - but only by accident of the current layout: add a fourth snippet or a
            // taller status line and the paragraph could vanish with every test still green. What
            // matters is not that the window is tall, it is that this text reached the user.
            string[] shown =
            [
                .. window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? string.Empty)
            ];

            shown.Should().Contain(viewModel.Explanation);

            // The two claims the paragraph exists to make, pinned where they are actually rendered.
            // "Nothing listens on the network" is a statement about this build's transport, and a
            // user is being asked to paste an executable path into an agent's configuration on the
            // strength of it - so it has to be on screen, not merely present on the view model.
            viewModel.Explanation.Should().Contain("--mcp").And.Contain("network");
        }
        finally
        {
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

            // Each snippet pinned to the heading it sits under. Everything above treats `shown` as
            // an unordered bag, so swapping the two JSON bindings in the markup left this whole
            // class green - and that swap would put a bare server entry under "Claude Desktop
            // (claude_desktop_config.json)" and a wrapped config under "Any other stdio MCP host".
            // The heading is what the user acts on, so a snippet under the wrong one is the defect,
            // not a cosmetic one.
            SnippetUnder(window, "Claude Code").Should().Be(viewModel.ClaudeCodeCommand);
            SnippetUnder(window, "Claude Desktop (claude_desktop_config.json)").Should().Be(viewModel.ClaudeDesktopJson);
            SnippetUnder(window, "Any other stdio MCP host").Should().Be(viewModel.GenericJson);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>The text of the one snippet box sitting under <paramref name="heading"/>.</summary>
    /// <remarks>
    /// Resolved through the markup's own grouping - each heading and its box share a
    /// <see cref="StackPanel"/> - so the assertion is about which snippet a reader sees under which
    /// label, rather than about the set of strings present anywhere in the window.
    /// </remarks>
    private static string SnippetUnder(Window window, string heading)
    {
        TextBlock label = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(block => string.Equals(block.Text, heading, StringComparison.Ordinal));

        StackPanel group = label.GetVisualAncestors().OfType<StackPanel>().First();

        return group.GetVisualDescendants().OfType<TextBox>().Single().Text ?? string.Empty;
    }

    /// <summary>
    /// Every Copy button reports an outcome, whichever branch it takes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Copy buttons are the dialog's only real interaction - it exists so a string reaches the
    /// clipboard - and until now nothing exercised them: no test raised the Click, and the manual
    /// pass that would have clicked them was never performed. A handler wired to the wrong snippet,
    /// or a <c>Report</c> that never makes the line visible, would have shipped unnoticed.
    /// </para>
    /// <para>
    /// Two things are asserted, because the first alone is not enough. That pressing Copy always
    /// reports an outcome - a silent Copy is indistinguishable from a broken application, and all
    /// three branches of <c>CopyAsync</c> end in <c>Report</c>. And what actually reached the
    /// clipboard: a status-line-only assertion let a handler wired to the wrong snippet pass, which
    /// was confirmed by pointing <c>OnCopyGeneric</c> at <c>ClaudeCodeCommand</c> and watching this
    /// test stay green. The headless platform does supply a clipboard and takes the success branch,
    /// so if that ever changes this fails loudly rather than quietly weakening.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryCopyButtonReportsAnOutcome() => AvaloniaRenderFixture.Run(() =>
    {
        AgentAccessViewModel viewModel = new(InjectedPath);
        AgentAccessWindow window = new(viewModel);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TextBlock status = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => block.Name == "CopyStatus");

            status.IsVisible.Should().BeFalse("nothing has been copied yet");

            window.GetVisualDescendants().OfType<Button>()
                .Count(button => string.Equals(button.Content as string, "Copy", StringComparison.Ordinal))
                .Should().Be(3, "one Copy button per snippet");

            (string Heading, string Expected)[] blocks =
            [
                ("Claude Code", viewModel.ClaudeCodeCommand),
                ("Claude Desktop (claude_desktop_config.json)", viewModel.ClaudeDesktopJson),
                ("Any other stdio MCP host", viewModel.GenericJson),
            ];

            foreach ((string heading, string expected) in blocks)
            {
                status.Text = string.Empty;
                status.IsVisible = false;

                ButtonUnder(window, heading).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                status.IsVisible.Should().BeTrue("pressing Copy must say whether it worked");
                status.Text.Should().NotBeNullOrWhiteSpace();

                // What actually landed on the clipboard, not merely that something did. Asserting
                // only the status line let a handler wired to the wrong snippet pass - verified by
                // pointing OnCopyGeneric at ClaudeCodeCommand and watching the test stay green.
                Task<string?> read = window.Clipboard!.TryGetTextAsync();
                Dispatcher.UIThread.RunJobs();
                read.IsCompleted.Should().BeTrue("the headless clipboard completes without pumping");
                read.Result.Should().Be(expected, "the {0} button must copy the {0} snippet", heading);
            }
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>The Copy button sitting under <paramref name="heading"/>.</summary>
    private static Button ButtonUnder(Window window, string heading)
    {
        TextBlock label = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(block => string.Equals(block.Text, heading, StringComparison.Ordinal));

        return label.GetVisualAncestors().OfType<StackPanel>().First()
            .GetVisualDescendants().OfType<Button>().Single();
    }
}
