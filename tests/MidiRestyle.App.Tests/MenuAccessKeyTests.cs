using System.Xml.Linq;

namespace MidiRestyle.App.Tests;

/// <summary>
/// No menu offers two of its own items the same Alt access key.
/// </summary>
/// <remarks>
/// <para>
/// A duplicate key does not fail loudly - it degrades. Windows cycles the highlight between the
/// clashing items instead of invoking either, so the keyboard route to both of them quietly stops
/// working while the mouse route still does, which is exactly the kind of defect a render test
/// cannot see and a human only notices if they happen to use the keyboard.
/// </para>
/// <para>
/// Read out of the markup rather than off a constructed menu: the access key is a property of the
/// authored <c>Header</c> string, so the XAML is the source of truth and parsing it needs no
/// Avalonia thread. Checked per menu, not globally - two different menus may reuse a letter freely,
/// which is why <c>_Open</c> appears in both File and Scales and is correct in both.
/// </para>
/// </remarks>
public class MenuAccessKeyTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void NoMenuOffersTwoItemsTheSameAccessKey()
    {
        XDocument markup = XDocument.Load(MainWindowMarkupPath());

        // One group per menu: the menu bar itself, and every item that opens a submenu. An item
        // with no MenuItem children is a leaf and owns no keys of its own.
        List<XElement> menus =
        [
            .. markup.Descendants().Where(e =>
                (e.Name == Avalonia + "Menu" || e.Name == Avalonia + "MenuItem")
                && e.Elements(Avalonia + "MenuItem").Any()),
        ];

        menus.Should().NotBeEmpty("the markup must still contain a menu for this test to mean anything");

        foreach (XElement menu in menus)
        {
            var keyed = menu.Elements(Avalonia + "MenuItem")
                .Select(item => (string?)item.Attribute("Header") ?? string.Empty)
                .Select(header => (Header: header, Key: AccessKey(header)))
                .Where(x => x.Key is not null)
                .ToList();

            List<string> clashes =
            [
                .. keyed.GroupBy(x => x.Key!.Value)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"'{g.Key}' shared by {string.Join(" and ", g.Select(x => $"\"{x.Header}\""))}"),
            ];

            clashes.Should().BeEmpty(
                "the items under \"{0}\" must each have their own Alt key",
                (string?)menu.Attribute("Header") ?? "the menu bar");
        }
    }

    /// <summary>The letter after the first single underscore, or null when the header sets no key.</summary>
    private static char? AccessKey(string header)
    {
        for (int i = 0; i < header.Length - 1; i++)
        {
            if (header[i] != '_')
            {
                continue;
            }

            // "__" is an escaped literal underscore, not a key marker: skip the pair.
            if (header[i + 1] == '_')
            {
                i++;
                continue;
            }

            return char.ToUpperInvariant(header[i + 1]);
        }

        return null;
    }

    /// <summary>Walks up to the repo root, the same way <see cref="PublishLayoutTests"/> does.</summary>
    private static string MainWindowMarkupPath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MIDIRestyle.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate the repo root (MIDIRestyle.slnx) walking up from '{AppContext.BaseDirectory}'.");
        }

        return Path.Combine(dir.FullName, "src", "MidiRestyle.App", "Views", "MainWindow.axaml");
    }
}
