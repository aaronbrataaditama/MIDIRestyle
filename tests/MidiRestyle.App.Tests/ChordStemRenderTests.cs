using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using MidiRestyle.App.Controls;
using MidiRestyle.Core.Notation;
using MidiRestyle.Core.Tuning;

namespace MidiRestyle.App.Tests;

/// <summary>
/// Pins "one stem per chord" by counting ink on a rendered page.
/// </summary>
/// <remarks>
/// <para>
/// The beamed case has been an invariant since v1.1 while the ordinary unbeamed one still regressed
/// in v1.2 - a chord straddling the middle line grew a stem per notehead, pointing opposite ways at
/// different x - so it is worth pinning mechanically rather than by eye.
/// </para>
/// <para>
/// Avalonia 12.1.1 exposes no <c>DrawingGroup</c> and no publicly subclassable recording
/// <c>DrawingContext</c>, so the draw calls cannot be intercepted and counted. What is left is the
/// rendered page. Rather than trying to tell a stem from a barline or a clef, each assertion
/// compares two renders that share all of that furniture: it cancels, and only the stems differ.
/// </para>
/// <para>
/// Every render runs on the one Avalonia thread owned by <see cref="AvaloniaRenderFixture"/>, for
/// the reasons set out on <see cref="NotationRenderTests"/>.
/// </para>
/// </remarks>
public class ChordStemRenderTests
{
    /// <summary>Which of the three renders a stem-count comparison is asking for.</summary>
    private enum ChordCase
    {
        /// <summary>An empty measure: the page furniture on its own.</summary>
        Empty,

        /// <summary>One note, so the furniture plus exactly one stem.</summary>
        SingleNote,

        /// <summary>The same note with a chord member above it, which must still be one stem.</summary>
        Chord,
    }

    private const double Zoom = 2.0;
    private const int PageWidth = 900;
    private const int PageHeight = 400;

    /// <summary>One part, one staff, one measure, holding at most a single chord.</summary>
    /// <remarks>
    /// G4 sits two steps below the treble middle line and E5 three steps above it, so a renderer
    /// deciding direction per notehead points one stem up and the other down - and puts them at
    /// different x, since an up-stem attaches right of the head and a down-stem left of it. That is
    /// the exact shape of the defect this pins, and it is why the chord has to straddle the line.
    /// </remarks>
    private static NotationScore StemScore(ChordCase which, NoteValue value)
    {
        List<NotationEntry> entries = [];

        if (which != ChordCase.Empty)
        {
            entries.Add(new NotationEntry
            {
                Note = new SpelledNote(4, 4, 0, 0),
                SoundingPitch = Pitch.FromMidi(67),
                Duration = new NotatedDuration(value),
                StartTicks = 0,
                DurationTicks = 480,
                Staff = 1,
            });
        }

        if (which == ChordCase.Chord)
        {
            entries.Add(new NotationEntry
            {
                Note = new SpelledNote(2, 5, 0, 0),
                SoundingPitch = Pitch.FromMidi(76),
                Duration = new NotatedDuration(value),
                StartTicks = 0,
                DurationTicks = 0,                          // a chord member consumes no time
                Staff = 1,
                IsChordMember = true,
            });
        }

        return new NotationScore
        {
            Divisions = 480,
            Title = "Stems",
            ScaleName = "Test",
            Parts =
            [
                new NotationPart
                {
                    Id = "P1",
                    Name = "P",
                    TrackIndex = 0,
                    Channel = 0,
                    StaffCount = 1,
                    Clefs = [Clef.Treble],
                    ProgramNumber = 0,
                    Measures =
                    [
                        new NotationMeasure
                        {
                            Number = 1,
                            StartTicks = 0,
                            LengthTicks = 480,
                            BeatsPerMeasure = 1,
                            BeatUnit = 4,
                            TimeSignatureChanged = true,
                            Entries = [.. entries],
                        },
                    ],
                },
            ],
        };
    }

    /// <summary>Renders and returns an ink mask: true wherever the page is not its background colour.</summary>
    private static bool[,] RenderInkMask(Func<Control> makeView)
    {
        bool[,] mask = new bool[PageWidth, PageHeight];

        AvaloniaRenderFixture.Run(() =>
        {
            Control view = makeView();

            view.Measure(new Size(PageWidth, PageHeight));
            view.Arrange(new Rect(0, 0, PageWidth, PageHeight));

            RenderTargetBitmap bitmap = new(new PixelSize(PageWidth, PageHeight), new Vector(96, 96));
            using (var context = bitmap.CreateDrawingContext())
            {
                view.Render(context);
            }

            int stride = PageWidth * 4;
            int size = stride * PageHeight;

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, PageWidth, PageHeight), buffer, size, stride);

                byte[] pixels = new byte[size];
                Marshal.Copy(buffer, pixels, 0, size);

                // The view fills its own background before drawing anything, so the top-left pixel
                // is the page colour. Comparing against that, rather than against "dark", keeps this
                // working whichever way round the palette is.
                byte c0 = pixels[0], c1 = pixels[1], c2 = pixels[2];

                for (int y = 0; y < PageHeight; y++)
                {
                    for (int x = 0; x < PageWidth; x++)
                    {
                        int i = (y * stride) + (x * 4);
                        int delta = Math.Abs(pixels[i] - c0)
                            + Math.Abs(pixels[i + 1] - c1)
                            + Math.Abs(pixels[i + 2] - c2);

                        // Well under solid ink, so a stem landing on a fractional x and drawn as two
                        // part-covered columns still registers; well over an antialiasing fringe.
                        mask[x, y] = delta > 90;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        });

        return mask;
    }

    /// <summary>
    /// Counts groups of adjacent columns carrying a tall unbroken run of ink - which is what a stem
    /// is.
    /// </summary>
    /// <remarks>
    /// Barlines, the clef and the part name are tall ink too, and are counted alongside the stems.
    /// That is why no test reads this number on its own: each compares two renders sharing all of
    /// that furniture, so it cancels and only the stems differ.
    /// </remarks>
    private static int CountTallInkColumns(bool[,] mask, int minRun)
    {
        bool[] tall = new bool[PageWidth];

        for (int x = 0; x < PageWidth; x++)
        {
            int run = 0;
            for (int y = 0; y < PageHeight; y++)
            {
                run = mask[x, y] ? run + 1 : 0;
                if (run >= minRun)
                {
                    tall[x] = true;
                    break;
                }
            }
        }

        int groups = 0;
        for (int x = 0; x < PageWidth; x++)
        {
            if (tall[x] && (x == 0 || !tall[x - 1]))
            {
                groups++;
            }
        }

        return groups;
    }

    private static int TallColumnGroups(ChordCase which, NoteValue value)
    {
        bool[,] mask = RenderInkMask(() => new StaffView
        {
            Score = StemScore(which, value),
            Zoom = Zoom,

            // The playhead is a full-height vertical line, i.e. indistinguishable from a stem.
            PlayheadTicks = -1,
        });

        // Two and a half staff spaces: taller than any notehead, rest, numeral or flag, and shorter
        // than the shortest stem.
        return CountTallInkColumns(mask, (int)(StaffMetrics.ForZoom(Zoom).StaffSpace * 2.5));
    }

    /// <summary>
    /// A chord carries exactly one stem, spanning it - never one per notehead.
    /// </summary>
    /// <remarks>
    /// <see cref="ChordCase.Empty"/> is not decoration: it proves the scan can see a stem at all. A
    /// pixel test loose enough to always pass is worse than no test, so the single-note leg has to
    /// fail if the counter ever stops finding anything.
    /// </remarks>
    [Theory]
    [InlineData(NoteValue.Quarter)]
    [InlineData(NoteValue.Half)]
    [InlineData(NoteValue.Eighth)]
    public void AChordCarriesOneStemAndNotOnePerNotehead(NoteValue value)
    {
        int empty = TallColumnGroups(ChordCase.Empty, value);
        int single = TallColumnGroups(ChordCase.SingleNote, value);
        int chord = TallColumnGroups(ChordCase.Chord, value);

        single.Should().Be(empty + 1,
            "one note adds exactly its stem to the page furniture - if this leg fails the scan is " +
            "not seeing stems, and the chord assertion below would pass without proving anything");

        chord.Should().Be(single,
            "a chord straddling the middle line must carry one stem spanning it, not one per " +
            "notehead pointing opposite ways at different x");
    }

    /// <summary>A whole note has no stem, so neither does a whole-note chord.</summary>
    [Fact]
    public void AWholeNoteChordCarriesNoStem()
    {
        int empty = TallColumnGroups(ChordCase.Empty, NoteValue.Whole);

        TallColumnGroups(ChordCase.SingleNote, NoteValue.Whole).Should().Be(empty,
            "a whole note is unstemmed - a stem on one simply reads as a half note");

        TallColumnGroups(ChordCase.Chord, NoteValue.Whole).Should().Be(empty,
            "and neither of its noteheads may grow one either");
    }
}
