# Implementation progress & handover

**Purpose:** make it safe to stop work at any point and resume later — in a new session, with no
memory of this one — without re-deriving decisions or repeating mistakes.

**Update this file at the end of every phase**, and whenever you learn something that would cost the
next session time. Keep "Next action" accurate to the single next thing to do.

- Spec: [`plan/PLAN-midi-restyle.md`](plan/PLAN-midi-restyle.md) — authoritative, 918 lines
- Invariants: [`../CLAUDE.md`](../CLAUDE.md) — load-bearing, each encodes a reproduced failure
- Layout: [`STRUCTURE.md`](STRUCTURE.md)
- Review: [`review/REVIEW-2026-08-26-independent.md`](review/REVIEW-2026-08-26-independent.md)

---

## Status at a glance

| Phase | Deliverable | State |
|---|---|---|
| 0 | Repo setup, plan, review | Complete |
| 1 | Solution scaffold, 6 projects, pinned packages, banned-API enforcement | Complete |
| 2 | `Pitch`, `Scale` + `ScaleValidation`, `MidiProject`, `MidiFileLoader` | **Complete** |
| 3 | Avalonia shell: menu bar, status bar, track list, metadata, settings/library services | **Gate met** (`ScaleLibraryService` deferred - see below) |
| 4 | `PianoRoll` control, original notes only | **Complete** |
| 5a | Scale code: melakarta gen, `.scl` reader, fidelity, speller, JSON store, `ScaleLibrary` | **Complete** |
| 5b | Scale data: ~95 hand-authored cited definitions | **Complete - 99 authored** across 9 files |
| 6 | `KeyDetector`, `KeyEstimate`, selectable source scale | **Complete** |
| 7 | `RestyleEngine`, both mappers, `RangePolicy`, collision resolver, 12-TET export | **Complete** |
| 8 | `OffsetClusterer`, `ChannelBudget`, `ChannelAllocator`, `PitchBendEncoder` | **Complete** - gate met: Rast exports on 2 channels, bends 6144/8192 |
| 9 | Style picker UI + restyled overlay | **Complete** - gate met: change a scale, see the notes move |
| 10 | `IPlaybackEngine` + A/B toggle — **core value delivered** | **Complete** — verified audible on a real device |
| 11 | Custom scale editor | **Complete** - view model + editor window + Scales menu |
| 12 | Portable publish + README | **Complete** - gate verified: exactly one file |
| v1.1 | MusicXML export, staff view, degree view, shared quantiser + beaming | **Complete** - see below |
| v1.2 | Staff as a wrapped page, staff playhead follow, degree view as a scale wheel, score follows A/B | **Complete** - see below |
| v1.3 | Click-to-seek on the staff, real clef and rest glyphs | **Complete** - see below |
| v1.4 | About window: description, MIT licence link, donation link | **Complete** - see below |
| - | Third-party notices (Inter/OFL compliance) | **Complete** - see below |

> **Current: 2026-08-31.** Last verified green: **1314 tests, 0 warnings**, publish gate passing at
> exactly one 50.2 MB file, and the published exe launches and writes its `scales/` folder beside
> itself. All twelve v1 phases complete, **plus the v1.1 notation layer and the v1.2 presentation
> pass**. 171 scales (99 authored + 72 generated). The app opens a MIDI file, describes it, draws it
> as a piano roll, **as a wrapped page of staff notation and as a scale wheel**, restyles it into any
> scale in the library, plays it, A/B switches mid-playback with the score following what you hear,
> and exports both `.mid` and **MusicXML**.
>
> **Committed.** `633d412` is the initial commit - 184 files, the whole of v1 through v1.3 -
> with the About window committed on top of it. Both commits were re-authored to
> `aaronbrataaditama <aaronbrataaditama@users.noreply.github.com>` before any push, so the
> SHAs here are the post-rewrite ones.

---

## v1.5 — the UI pass (complete, **uncommitted**, branch `ui-enhancements`)

Three changes the user asked for after taking the app's UI to Claude Design. Everything below is
built, tested and green — `dotnet build` 0 warnings, `dotnet test` 1356 passing — but **nothing is
committed**: the user asked for a branch without a commit. `git status` shows the working set.

### 1. Bars in the file pane

`MetadataViewModel.DurationText` now reads `0:14.4 · 8 bars` — tenths of a second, and the bar
count beside it. `MetadataViewModel.Measures` is the one reading of the time-signature map, built
once and lazily; the transport readout and the roll's ruler both take it from there rather than
computing their own, for the same reason `MeasureGrid` exists at all. A SMPTE file gets neither
figure rather than an invented one.

### 2. The piano roll

- A **keyboard gutter** down the left (`PianoRoll.GutterWidth`, 46px) and a **bar ruler** across
  the top (`RulerHeight`, 19px). Both are carried in `RollViewport` so the culling and the
  scrollbar extents measure the grid rather than the control — see the new CLAUDE.md bullet.
- **Sounding keys light up**, from whichever layer the A/B toggle says is audible
  (`PianoRoll.HighlightRestyled`, pushed from `HearingRestyled`). A fixed `bool[128]` cleared per
  frame, so the render path still allocates nothing.
- **Barlines and numbers.** `SetBars(long[])` takes the same measure starts the file pane counts.
  Numbers thin to a 1/2/4/8 interval and are additionally skipped if they would land within 30px of
  the last one printed — needed because a pickup bar makes the naive first-gap estimate wrong.
- The scale rows' chip now reads **`12-TET`** or **`±50¢`** instead of Exact/Close/Approximate, in
  theme-aware green or amber. `FidelityLabel` is untouched and still drives the *warning*.
- **Bar readout in the toolbar**: `bar 3 / 8` plus a determinate progress bar, top right.
- **A degree ladder** under the scale list (`DegreeLadder`, a new control) with
  `Deviation ±50¢ · 2 bend clusters at 5¢` beneath it, replacing the fidelity badge's calm case.

### 3. The degree wheel, rebuilt

Matched to the animation the user supplied. Centred header naming tradition and region; the twelve
reference ticks now carry **note names** read from the tonic; a **spoke, number, marker and cents
reading for every degree at all times**; the deviation drawn as an **arc along the ring** rather
than a chord; the tonic printed in the middle on a hub disc the spokes stop at; a sounding degree
shown by **recolouring** its spoke, marker and number.

**Removed, deliberately, and the user has been told:** the octave-radius plotting and the fading
trail. Both are gone from `DegreeGeometry` and `DegreeWheelIndex` along with their tests
(`RadiusForOctave`, `MaxOctaveRings`, `TrailStrength`, `TrailStep`, `DegreeWheelIndex.Trail`). The
reasoning is in CLAUDE.md; the short version is that the wheel no longer distinguishes a bass note
from a melody note on the same degree, and the piano roll does that better anyway. If it is wanted
back, `git log` on this branch is the place to start.

### How this was verified

No manual pass was possible from the terminal, so the controls were rendered headlessly through
`AvaloniaRenderFixture` — including the whole `MainWindow` with a real file loaded, in both theme
variants — and the PNGs read back. That harness was **deleted** before finishing; if you need it
again, it is a class that calls `AvaloniaRenderFixture.Run`, builds the control *inside* the
callback (Avalonia objects bind to the first thread that touches them), and saves with
`bitmap.Save(path, new PngBitmapEncoderOptions())`. `window.CaptureRenderedFrame()` is on
`Avalonia.Headless.HeadlessWindowExtensions`, not on `Window`.

---

## Third-party notices (complete, 2026-09-01)

The single-file `.exe` redistributes its dependencies and the .NET runtime and shipped none of
their licences. Most are MIT, which was a courtesy gap; **Inter is under the SIL Open Font License
1.1, which requires the licence be bundled with any redistribution**, so that part was a compliance
gap. Now fixed.

### What shipped

`THIRD-PARTY-NOTICES.txt` at the repository root - 4,568 lines, six sections, every licence text
reproduced verbatim from the packages themselves rather than from memory. It is **embedded into the
App assembly** (`LogicalName="MIDIRestyle.THIRD-PARTY-NOTICES.txt"`, included from the repo root so
there is one copy and it cannot drift) and read back by `Services/ThirdPartyNotices.cs`.

Embedding is forced, not stylistic: `AssertSingleFilePublish` requires exactly one file in the
publish folder, so the notices cannot sit beside the exe. `About -> third-party notices` opens
`ThirdPartyNoticesWindow`, a resizable monospaced viewer, so a lone copied `.exe` carries its
notices with it. The exe grew 53 KB (230 KB of text, compressed) and the gate still passes.

### The authoritative redistributed set

Obtained by publishing **unbundled** (`-p:PublishSingleFile=false -o <tmp>`), not from the package
list: the Linux, macOS and WebAssembly native-asset packages resolve but never land in a win-x64
publish, and BannedApiAnalyzers is an analyzer. 232 files.

| Component | Licence | Notice source |
|---|---|---|
| Avalonia (23 assemblies) | MIT | nuspec SPDX expression |
| CommunityToolkit.Mvvm | MIT | package `ThirdPartyNotices.txt` (section 6) |
| Melanchall.DryWetMidi + 2 natives | MIT | nuspec |
| SkiaSharp / `libSkiaSharp.dll` | MIT (Skia itself BSD-3) | native pkg notices (section 4) |
| HarfBuzzSharp / `libHarfBuzzSharp.dll` | MIT | same file, byte-identical - included once |
| MicroCom.Runtime, Tmds.DBus.Protocol | MIT | nuspec |
| ANGLE (`av_libglesv2.dll`) | BSD-3 | `avalonia.angle.windows.natives/LICENSE` (section 3) |
| .NET runtime 10.0.11 (173 assemblies + natives) | MIT | runtime pack `LICENSE.TXT` + notices (section 5) |
| **Inter font** (6 `.ttf` in `Avalonia.Fonts.Inter.dll`) | **SIL OFL 1.1** | the font's own `name` table (section 2) |

Section 4 is the largest because the native binaries statically incorporate ~20 further projects
(freetype, libpng, libjpeg-turbo, libwebp, ICU, expat...), each carrying its own notice.

### Two traps, if this is ever regenerated

1. **`Avalonia.Fonts.Inter` hides the font licence.** Its nuspec declares `MIT` - that covers
   Avalonia's code, not the font - and the package ships **no OFL text at all**. Inter's real
   notice was read out of the font binaries' `name` table by locating the `0x00010000` sfnt
   signature in the DLL and walking the table records; the resources are stored raw, so grep finds
   nothing. It gives `Copyright 2020 The Inter Project Authors`, v3.019, and **no Reserved Font
   Name** (so the OFL's RFN clause does not bite).
2. **`C:\Program Files\dotnet\LICENSE.txt` is the wrong file** - it is the Microsoft installer
   terms. What a self-contained app actually bundles comes from the
   `microsoft.netcore.app.runtime.win-x64` pack, which is MIT.

The OFL 1.1 body was not available from any of our own dependencies; it was taken verbatim from the
copy Aspire ships for Cascadia Code, and checked for all five clauses and both closing sections.

### Guards

`ThirdPartyNoticesTests` (11 cases) asserts the resource embeds, that the loaded text is the real
document and not the missing-resource fallback, that Inter's copyright and the full OFL are
present, and **names every redistributed component** - so adding a package fails a test instead of
silently widening what the exe redistributes. `ThirdPartyNoticesWindowRenderTests` covers the
window and budgets its open time: the ~4,600-line document is laid out in one non-virtualising
`SelectableTextBlock`, measured at **708 ms**, with the test failing above 4 s. That timing leg was
watched failing before being trusted.

## v1.4 - the About window (complete)

`Help > About MIDIRestyle` was a placeholder menu item with `IsEnabled="False"`. It now opens a
modal dialog: what the app does in three paragraphs, a link to the MIT licence, and the donation
line at the foot.

Files: `ViewModels/AboutViewModel.cs`, `Views/AboutWindow.axaml{,.cs}`, wired from
`MainWindow.axaml` + `OnAboutClicked`. Tests in `AboutViewModelTests` and `AboutWindowRenderTests`.

### Findings worth not rediscovering

- **Avalonia 12.1.1 ships no `HyperlinkButton`.** A grep of `Avalonia.Controls.dll` appears to find
  it - that hit is a coincidental substring in the binary. Reflecting over every `Avalonia*.dll` in
  the package cache finds no type matching `Hyperlink` at all. Links here are `Button`s with the
  Fluent chrome flattened away in every visual state (`/template/ ContentPresenter` for normal,
  `:pointerover` and `:pressed`), with the underline and accent colour set **inline on an inner
  `TextBlock`** rather than as a style - a local value cannot be overridden by Fluent's own
  presenter-level state setters, and a style setter can.
- **`InformationalVersion` carries a `+<commit sha>` suffix in every configuration, Debug
  included.** Confirmed in both `obj/Debug` and `obj/Release` generated `AssemblyInfo.cs`, and
  stamped on the published exe as `1.4.0+<commit sha>`. `AboutViewModel.ReadVersion` cuts at the plus
  sign; without that the box would show the sha. The csproj now carries `<Version>1.4.0</Version>`
  so the About box reads the build rather than restating it.
- **A C# string constant meant to reflow must be one logical line per paragraph.** Written as a raw
  string literal across source lines, the source wrapping is preserved verbatim and the `TextBlock`
  cannot reflow to the window - it renders ragged at a fixed width. Paragraphs are concatenated
  single-line strings joined by an explicit newline-escape pair, and a test asserts that no
  paragraph contains a newline of its own.
- **The publish gate can fail on stale runtime output, not a regression.** The app writes
  `MIDIRestyle.settings.json` and `scales/` *beside itself*, so launching the published exe from
  `publish/` leaves 10 extra files there and the next publish fails the exactly-one-file gate.
  Delete the publish directory before re-publishing. Verified afterwards: one file, 50.2 MB.

### What `AboutWindowRenderTests` actually covers - measured, not assumed

Worth stating, because the first version of its doc comment overclaimed on all three counts:

| Break introduced | Caught? |
|---|---|
| `x:Static` naming a constant that does not exist | **Yes - at build time**, AVLN2000; never reaches the test |
| `{DynamicResource}` naming a key that does not exist | **No** - Avalonia falls back silently by design |
| `SizeToContent="Height"` removed | **No** - the window is still tall enough for the assertions |
| `Icon` pointing at a missing avares resource | **Yes** - `FileNotFoundException` at show time |

So it is an honest smoke test: it proves the window builds, shows and renders without throwing,
which matters because no other test opens this window. It is not a layout or theming guard.
Appearance was checked by rendering the window headlessly in both theme variants **with the Fluent
theme added to the test `Application`** - the render fixture configures a bare `Application` with no
styles, so an unthemed render shows no accent colour, no separator and no button chrome - and
looking at the PNGs.

### Left undone, deliberately

A `LICENSE` file was added on 2026-08-31 (MIT, `Copyright (c) 2026 Aaron Brata Aditama` —
the holder name was derived from the GitHub username and confirmed by the owner
on 2026-08-31).
Its wording was verified word-for-word against SkiaSharp's plain-text MIT licence in the NuGet
cache: 162 words, zero differences.

The About box still links to `opensource.org/license/mit` rather than to the repo copy, deliberately —
the canonical text always resolves, whereas a link into a private repository does not.

**Still undone:** the self-contained exe redistributes Avalonia, DryWetMIDI, SkiaSharp,
HarfBuzzSharp, CommunityToolkit.Mvvm and the Inter font, and ships none of their notices. Inter is
under the SIL Open Font License, which *requires* its licence travel with the redistribution, so a
THIRD-PARTY-NOTICES file is a real gap rather than a tidiness one.

---

## v1.3 — click-to-seek and real glyphs (complete)

Three things the user raised after using the v1.2 build. All done, all verified by rendering and
looking, not only by tests.

### 1. Click anywhere in the score to play from there

`StaffPageLayout.TickForX` is the exact inverse of `XForTick`, and `TryTickAt` resolves the system
before the measure. Two things make this less trivial than it sounds, both now invariants in
CLAUDE.md:

- **It must invert the column interpolation, not divide the bar by time.** A bar's note columns are
  spaced by duration weight, so proportional arithmetic lands short of a whole note and past a run of
  sixteenths — the very error `XForTick` exists to avoid going the other way.
- **The y is load-bearing.** On a wrapped page the same x appears once per system, so ignoring y
  would seek into the first bar of the piece wherever you clicked on the last line.

Wired in `MainWindow.axaml.cs` with the piano roll's own gesture — press, release, 4 px slop — so
clicking a bar means the same thing in both views. The playhead is written straight into all three
views on release rather than waiting for the 60 Hz timer, because a click that leaves the playhead
still for a frame reads as a click that did not register.

### 2 & 3. The clefs and the rests were wrong

Reproduced first by rendering a glyph sheet and looking at it. All three symbols had been
hand-authored as stroked beziers, and all three read wrong:

| Glyph | What was wrong |
| --- | --- |
| G clef | Narrow, bowl undersized, spiral off the line — read as mirrored |
| F clef | Bare curve; the dots were placed by eye and sat off the F line |
| Quarter rest | A uniform thin wire, not a calligraphic zigzag |
| Eighth rest and shorter | **No visible blob at all** — a bare slanted "7" |

Now `StaffGlyphs` holds the genuine outlines, verbatim from the public-domain Wikimedia SVGs the user
linked, kept in their own source coordinates so they can be diffed against the originals; a single
`Normalise` maps each file's staff geometry onto ours. They are **filled, not stroked** — the
contours already carry the thick-and-thin. The eighth-rest family is still constructed (blob + hook +
stem, one per beam level) because that is how the glyph is built, but from proportions measured off
the source rather than guessed, and its hook is stroked because the source runs the stroke out to the
stem and back.

Still no music font, which was the point: nothing in the dependency set ships the Unicode musical
symbols block, and requiring an installed font would break the portable single-file promise.

### Verification

`dotnet build` 0 warnings / 0 errors; `dotnet test` **1305/1305**; publish gate one file. The
click-to-seek scroll-offset test was confirmed by breaking it (drop `ScrollY`, both clicks return
5528, test fails). Glyphs were checked by rendering each one large and looking at it — which is how
all four defects were found in the first place, and the fifth time in this project that looking beat
a green suite.

---

## v1.2 — notation presentation (complete)

> **As at the end of v1.2:** `dotnet build` 0 warnings / 0 errors, `dotnet test` **1293/1293**
> (verified over three consecutive full runs), publish gate passing at exactly one 50.2 MB file, and
> the published exe launches and writes its `scales/` folder beside itself.
>
> **Nothing was outstanding at that point.** The last open item — pinning one-stem-per-chord as a
> regression test — landed on 2026-08-28 as `tests/MidiRestyle.App.Tests/ChordStemRenderTests.cs`.
> v1.3 above then followed from the user trying the build again.
>
> **Resolved 2026-08-31.** This callout used to warn that the repository had no commits at
> all. It is committed now — see the status callout at the top of this file.

### The chord-stem regression test (done)

`ChordStemRenderTests` closes the last gap: the unbeamed-chord stem fix had been verified only by
eye. How it works, and why it is built this way:

- Avalonia 12.1.1 has **no `DrawingGroup` and no publicly subclassable recording `DrawingContext`**
  (confirmed by reflecting over `Avalonia.Base.dll`), so draw calls cannot be intercepted and
  counted. That leaves reading pixels off the rendered page.
- Rather than trying to tell a stem from a barline or a clef, each assertion is **differential**: it
  compares a chord render against a single-note render of the same measure. Clef, barlines, part
  name and time signature are identical in both, so they cancel and only the stems differ. This is
  what makes the test robust — it never has to locate the chord's x or classify the furniture.
- An **empty-measure** leg proves the scan can see a stem at all (`single == empty + 1`). Without it
  a counter that found nothing would make the chord assertion pass vacuously.
- `PlayheadTicks = -1` matters: the playhead is a full-height vertical line, indistinguishable from
  a stem.
- The chord is G4 + E5 under a treble clef — straddling the middle line, which is the case that
  produced two stems pointing opposite ways at different x.

**Both legs were verified by reintroducing the defect**, not assumed:

| Defect reintroduced | Result |
| --- | --- |
| `DrawStemAndFlags` called per notehead in `DrawNote` (the original v1.2 bug) | 3 of 4 fail — chord 6 tall columns vs single 5, exactly one extra stem |
| `HasStem` guard removed from `DrawStemAndFlags` | `AWholeNoteChordCarriesNoStem` fails — 5 vs 4 |

Note the two catch *different* regressions: the whole-note test correctly does **not** fire on the
per-notehead defect. Stability: **10 consecutive filtered runs**, then **3 consecutive full-suite
runs**, all green — the full runs matter because a second class touching `AvaloniaRenderFixture` is
exactly the thread-affinity hazard that bit during v1.1.

### Remaining

Nothing outstanding. Optionally revisit the cosmetic issues listed under "Known cosmetic issues,
deliberately left" — none affect correctness and all were judged not worth the regression risk.


All four items the user raised after using the app are done. Verified 2026-08-28: `dotnet build`
**0 warnings, 0 errors**; `dotnet test` **1293/1293 green** (was 1191 before this work); the portable
publish gate passes with **exactly one file, 50.2 MB**; the published exe launches, runs and
materialises its `scales/` folder beside itself.

| # | Item | Outcome |
|---|------|---------|
| 1 | Staff "doesn't look like a standard music score" | Relaid out as a **wrapped page of systems**; `StaffPageLayout` owns breaking, justification, ragged last and content height |
| 2 | Playhead does not follow playback in the staff | `StaffView.FollowPlayhead()` added and called from the 60 Hz timer |
| 3 | Degree view unreadable | Replaced with a **scale wheel** — degrees at their true cents against 12-TET ticks |
| 4 | Score should follow the target style / the A/B toggle | `ShowRestyledScore` follows the preferred playback side, so the score always shows what you hear |

### Defects found by *looking*, not by tests

This is the fourth time in this project that rendering to PNG and inspecting it found what a green
test suite did not. All four were invisible to the existing tests and all are now invariants in
`CLAUDE.md`.

1. **Every accidental overlapped its own notehead.** Ten signs anchored at one fixed offset, but they
   differ by >2x in width. Fixed by placing each from its right edge.
2. **An unbeamed chord drew one stem per member** — the beamed case was honoured, the ordinary case
   was not. A straddling chord grew stems both ways out of one column; a flagged chord got a flag per
   member.
3. **Whole notes were drawn with a stem** (only `Breve` was excluded), so every held note read as a
   half note.
4. **The wheel's header clipped mid-word** at narrow panes — no elision guard, unlike the caption.

### Test coverage added

- `StaffPageLayoutTests.cs` (new, 68) — system breaking, justification to the right margin, the
  `RaggedLastThreshold` sweep with both branches proven reached, `MinStretch`, `ContentHeight`,
  `PrintsTimeSignature`, tick lookup, scroll/follow, and edges (one measure, page narrower than a
  measure, 400 measures, grand staff). Parameterised over **PPQN 480 and 120** with onsets off the
  tick grid, per the rule that boundary-only fixtures cannot fail.
- `StaffViewInteractionTests.cs` (new, 9) — `ContentHeight`/`SystemCount` before first layout, scroll
  recovery, `FollowPlayhead` behaviour at three viewport heights including one shorter than a system.
- `StaffGeometryTests.cs` (+11), `DegreeGeometryTests.cs` (+6), `NotationRenderTests.cs` (+5, the
  wheel's header elision across four pane widths plus a re-fit-on-resize case).

### Known cosmetic issues, deliberately left

- **The treble clef is slightly top-heavy.** Correct construction (spiral centred on the G line) and
  clearly a G clef, but the upper loop is large and the bowl does not cross the stem. Rewriting the
  bézier is fiddly with real regression risk and no functional payoff.
- **`ClampScrollY(+inf)` returns 0, not `MaxScrollY`** — shares the NaN path. No scrollbar can produce
  it; documented in a test rather than changed.
- **`Abbreviate("Recorder")` gives `"Recor."`** — ugly, working as specified.
- **A very tall narrow pane leaves the wheel with large empty margins.** Inherent to fitting a circle
  in a non-square box.
- Colour fringing on text in rendered PNGs is Skia's subpixel antialiasing in the headless target,
  not a layout fault.

### The one thing that still needed a human (resolved)

This section warned that nothing was committed and there was no recovery point. Resolved on
2026-08-31: v1 through v1.3 went in as the initial commit, and the tree has been committed at
each green boundary since.

## v1.1 — notation (complete)

Everything the plan deferred, plus the machinery it was waiting on. Delivered 2026-08-28.

| Piece | Where | Note |
|---|---|---|
| Rhythm quantiser | `Notation/RhythmQuantiser.cs` | per-beat straight/triplet/sextuplet, with an onset floor |
| Duration spelling | `Notation/DurationDecomposer.cs` | tied written values; reports `WrittenTicks` |
| Measure grid | `Notation/MeasureGrid.cs` | the one source of barlines |
| Pitch spelling | `Notation/NoteSpeller.cs` | the `Alter` → `<alter>` frame conversion |
| Degree reading | `Notation/DegreeReading.cs` | cipher numerals + octave dots |
| Beam grouping | `Notation/BeamGrouper.cs` | groups, levels, hooks |
| The orchestrator | `Notation/NotationBuilder.cs` | **everything branches after this** |
| MusicXML 4.0 | `Notation/MusicXmlExporter.cs` | partwise, `<fifths>0</fifths>`, explicit accidentals |
| Staff view | `App/Controls/StaffView.cs` | vector glyphs, no music font dependency |
| Degree view | `App/Controls/DegreeView.cs` | for the equal-step families with no staff spelling |
| App icon | `App/Assets/app-icon.ico` | 9 sizes; 16/20/24 deliberately omit the grid lines |

### The review, and what it caught

`.claude/review/REVIEW-2026-08-28-notation.md` — 1 Critical, 3 High, 6 Medium, 8 Low, 5 test-quality.
All Critical/High/Medium fixed. The lesson worth carrying: **every notation fixture placed its notes
on exact tick boundaries**, which is the one input class that cannot fail the measure-sum assertion.
The invariant was broken for a day while its own test passed. Jittered and seeded-fuzz inputs now
guard it.

Measured before → after, with my own harness on both sides:

| | before | after |
|---|---|---|
| jittered files with an overlong measure | 234/300 | **0/300** |
| single-onset beats misread as tuplets | 33% | **0%** |
| dense-overlap files losing a note (L1) | 262/300 | **0/300** |
| `NotationBuilder.Build`, 5000 notes over 3200 bars | 34.9 ms | 20.4 ms |

### Known limitations

- Residual cents are dropped by MusicXML export by design; the staff view still shows them.
- Only pre-first-note controller state is mirrored onto derived channels (a v1 limitation).

---


**Deferred out of phase 3 deliberately:** `ScaleLibraryService` needs `ScaleJsonStore` (phase 5a) and
scale data (5b) to have anything to serve. Wiring it before those exist would mean writing it twice.
The phase 3 gate - a runnable app that opens and describes a MIDI file - is met without it.

---

## Phase 1 — scaffold (complete)

### Done and verified

- Six projects created and added to `MIDIRestyle.slnx`: three under `src/`, three under `tests/`.
  *(The plan's build-order table says "5 projects"; that was a leftover — its own solution layout
  lists six. Six is correct.)*
- `Directory.Build.props`: `net10.0`, `Nullable=enable`, `ImplicitUsings`, `InvariantGlobalization`,
  `TreatWarningsAsErrors=true`, `PublishTrimmed=false`.
- `Directory.Packages.props`: central package management, every version pinned.
- **All pinned versions were verified to exist on nuget.org** (via `dotnet package search`, because
  `curl` cannot reach NuGet from this machine — TLS error 35):
  Avalonia **12.1.1**, DryWetMIDI **8.0.3**, CommunityToolkit.Mvvm **8.4.2**,
  AwesomeAssertions **9.6.0**, BannedApiAnalyzers **5.6.0**.
  The plan's claims about Avalonia 12.1.1 and DryWetMIDI 8.0.3 both hold.
- `dotnet build`: **succeeds, 0 warnings, 0 errors.**
- **Banned-API enforcement verified by deliberate violation** — this is the phase-1 gate and it
  passes:
  - `using Melanchall.DryWetMidi.Multimedia;` in Core produces `error RS0030`
  - `Assembly.GetExecutingAssembly().Location` produces `error RS0030`
  - Removing both restores a clean build
- Minimal Avalonia bootstrap compiles and links: `Program.cs` (`[STAThread]`), `App.axaml`,
  `MainWindow.axaml` with a placeholder body. Phase 3 replaces the window contents.

- `dotnet test`: **green.** 3 projects, 3 tests, exit code 0 — and verified to correctly report a
  deliberate failure with exit code 2.

### Not done

- The app has not been launched yet (only built). First launch is the phase 3 gate.
- The three `ScaffoldTests.cs` placeholder files are throwaway; delete each once its project has real
  tests.

---

## Two bugs found by the user testing the published exe

Both were invisible to a then-758-test green suite, and both are worth understanding rather than
just fixing.

### 1. The Play button never enabled

`CanPlay` computed to **true** the whole time. `Adopt()` raised `HasProject` and `WindowTitle` but not
`CanPlay`, so the binding never re-evaluated and the button stayed permanently disabled.

**Why no test caught it:** every existing view-model test asserted *values*, and a value is correct
whether or not anyone was told about it. The fix is declarative rather than another Raise call -
`[NotifyPropertyChangedFor]` on the source properties, so the dependency lives beside the thing it
depends on and cannot be forgotten at a new mutation site. `TransportNotificationTests` now subscribes
to `PropertyChanged` and asserts on the *names raised*.

**Rule going forward: any computed property a control binds its enabled state to needs a notification
test, not just a value test.**

### 2. A disabled control with its explanation erased

`AttachEngine` reported "no MIDI output device" as a status *message*, and then loading a file
overwrote it. On a machine without a synth the user would get a greyed-out Play button and no reason
anywhere - which reads as a broken app rather than a machine without a synth.

Fixed by moving it to a **persistent notice** (the status bar already distinguishes a transient
message from standing notices) and adding `PlayDisabledReason` / `CompareDisabledReason` as button
tooltips. Both causes are ordinary states: no device, or no file open yet.

### Also from the same report: the v1.1 deferrals were badly signposted

The user expected the staff view to work on a piano MIDI. It is deferred to v1.1 *by design*, but the
menu said only "Staff" with the explanation hidden in a tooltip nobody hovers. Now labelled
**"Staff  (not in this version)"** with a tooltip saying why it and MusicXML export ship together -
they share a quantiser. Same for Degrees and Export MusicXML.

**A deferral that looks like a bug is a documentation failure, not a scope decision.**

---

## Phase 10 - playback and A/B (complete, verified audible)

Measured on a real device (Microsoft GS Wavetable Synth):

```
engine       : DryWetMidiPlaybackEngine, "Microsoft GS Wavetable Synth"
transform    : Maqam Rast, 16 notes on 4 channels
stopChannels : [0,1,2,3,9]
playhead     : 1157 ticks after ~1.2s   (120 BPM x 480 PPQN predicts ~1152)
A/B          : switched to restyled and back mid-playback, playhead kept
switch gap   : min 0.68 ms, median 0.95 ms, max 10.90 ms (cold first switch)
```

Target was 30 ms. The in-test assertion is 300 ms deliberately - a catastrophic-regression guard, not
a benchmark, after the 51.7 ms flake taught us that lesson.

### Preview plays the exported bytes

`PlaybackSequenceBuilder` runs **both** A/B sides through `MidiFileExporter`, so "what you heard is
what you exported" is true *by construction*. A test asserts the preview bytes are **byte-identical**
to what export writes; if it fails, someone has added a second output path.

The "original" side is built by restyling with every track excluded, not by re-reading the file, so
both sides share a tick grid and a seek lands in the same musical place on either.

### Three DryWetMIDI facts that are requirements, not trivia

All reproduced, not inferred, and now in `CLAUDE.md`:

1. **The four `Track*` flags are load-bearing.** `MoveToTime` emits nothing while stopped and
   `Start()` re-sends tracked state - which is *why* the switch order is safe. But `MoveToTime` on a
   **running** `Playback` re-sends only note on/off, so "optimising" the switch to leave the arriving
   side running would play the restyled side at 12-TET from the seek point. Silently.
2. **A seek's bend range only worked by luck.** `Playback` replays controllers in ascending number,
   so the RPN handshake returns as `CC6, CC38, … CC100, CC101` - data entry before the RPN-null, with
   no re-selection of RPN 0/0. It lands on whatever RPN the synth points at, which is 0/0 at GM's
   default +/-2 - equal to our default. **Fixed**: `PitchBendEncoder.RetuneSequence` is now re-emitted
   after any seek or switch onto the restyled side, guarded by `RetuneAfterSeekTests`.
3. **Linux lacks playback twice over.** No device native *and* the default tick generator P/Invokes
   the same library. Device enumeration also **throws** rather than returning zero, so
   `if (GetDevicesCount() == 0)` crashes rather than degrading.

### One deliberate superset of the invariant

The invariant says the stop sequence goes to every *allocated* channel. The engine sends it to the
**union** of allocated channels and every channel either side sounds on - because a note hanging on
an untouched track is just as stuck, and a stale bend there detunes whatever plays next. A superset,
never a subset.

---

## Phase 12 - the portable artefact (complete, and the fix was not the obvious one)

`dotnet publish` now produces **exactly one file, 47.8 MB**, and launching it writes the nine scale
JSON files beside itself. A gate runs `AfterTargets="Publish"` and fails the build otherwise, so a
broken artefact cannot be produced quietly.

**The MSBuild lesson, which cost the agent real time and is worth not repeating.** The obvious fix -
`<None Remove="...Melanchall_DryWetMidi_Native64.dylib" />` in the csproj - **silently removes
nothing.** DryWetMIDI's own `.targets` file is imported via NuGet's generated
`obj/*.nuget.g.targets`, which MSBuild imports *after* the csproj body, so the `Remove` runs before
the items it targets exist and succeeds at nothing at all. `CLAUDE.md` previously told you to do
exactly that; it has been corrected.

The working approach removes from **`@(ResolvedFileToPublish)`**
(`AfterTargets="ComputeResolvedFilesToPublishList"`) - the final list, after every contributor has
had its say. Matched by exact filename rather than a wildcard, so a future DryWetMIDI release that
adds a genuinely needed native is not silently dropped too.

It also strips `*.pdb`, which turned out to matter: SkiaSharp's and HarfBuzz's RID-native symbol
files land in the publish folder and are tens of megabytes. `DebugType=none` does not reach them -
that property governs only the App project's own symbols.

---

## Phase 11 - the custom scale editor (complete)

View model by subagent, editor window and Scales menu by me. Three things worth keeping:

- **The `user.` prefix is static text beside the id field, not something you type.** An id outside
  that namespace is not expressible, so shadowing a shipped scale is impossible rather than
  validated against.
- **A failed save keeps the dialog open** and shows the reason. Closing would lose the user's work,
  which is far worse than making them read a message - and a modal on top of a modal is a poor way
  to say "your USB stick is read-only".
- **Scala import routes through the editor** rather than saving silently. An imported `.scl` arrives
  with no meaningful name and `Notatable` false, so the user should see and confirm both.

**Compiled bindings earned their keep here**: two bindings failed the *build* because
`CopyOnEditExplanation` and `NotatableExplanation` are `const` fields rather than properties. In a
reflection-binding framework that would have shipped as a silently blank label.

---

## Phase 9 - wiring the transform to the UI (complete)

### Landed

- **`MainWindowViewModel.ApplyRestyle(settings)`** runs the engine, plans the allocation against the
  same ceiling playback will use, and publishes the restyled notes plus every compromise to the
  status bar. `ClearRestyle()` drops the layer; loading a new file discards the previous transform.
- **`PianoRoll` shows both layers** - ghosts from `SetGhostNotes`, solid from `SetNotes`.
- **`ScaleLibraryService`** assembles all 171 scales at the documented precedence.

### Two things `ScaleLibraryService` got right that are easy to get wrong

**`AssetLoader` throws without an initialised Avalonia runtime** (`Unable to locate
'Avalonia.Platform.IAssetLoader'`). The service therefore depends on an injectable
`IEmbeddedScaleSource`; unit tests read the real nine JSON files off disk, and the actual
`avares://MIDIRestyle/Assets/scales/` path was verified out-of-band with a headless console app.
`AvaloniaEmbeddedScaleSource` is **not** exercised by `dotnet test` - if you change it, verify it by
running the app, not by trusting the suite.

**After first-run materialisation, the embedded tier and the beside-exe folder hold the same 99
ids.** Feeding both into `ScaleLibrary.Build` unfiltered would report all 99 as collisions on every
single launch, burying the one collision that matters. The service drops embedded ids already
present on disk, so the steady state is collision-free while a deleted or corrupt folder copy still
falls back to the in-memory original.

### Complete - verified in the running app

`StylePanelViewModel` + the right-rail XAML: always-open searchable region-grouped list with
per-row fidelity badges, tonic picker and source-scale picker outside the disclosure, six policies
inside it, and a `DispatcherTimer` debounce at `StylePanelViewModel.SelectionDebounce` (150 ms).

Measured end to end against the sample file:

```
library      : 171 scales, 0 collisions
detected key : C major - margin 0.181, 2 alternates  -> seeds Ionian as source scale
search       : "slendro" -> 5 of 171
selected     : Slendro (idealised 5-equal), badge "Approximate / up to 40 cents"
reapply      : 16 restyled notes on 10 channels
12-TET mode  : fidelity warning TRUE      microtonal: FALSE
Gong         : 2 channels, no warning
```

**First-run materialisation verified in the real app**: launching the exe writes all nine scale JSON
files beside it and leaves `%APPDATA%` untouched. That also confirms Avalonia's `AssetLoader` works
in the real runtime, which the test suite cannot cover.

### Two gaps I only found by running it

1. **No source-scale picker.** I built the rail without one, and `CanRestyle` was blocked on
   "degree mapping needs a source scale" whenever key detection came back `NoKeyDetected` - with no
   way for the user to fix it. Added, bound to a new `SourceScaleChoices` projection (the existing
   `SourceScaleOptions` is `ScaleEntry`, which will not bind to a `Scale?` selection).
2. **`RestyleBlockedReason` was never displayed.** The panel computed a perfectly good explanation
   and nothing showed it. Now surfaced in the rail.

Both were invisible to the test suite because both are about what the XAML does or does not bind.

### The artifact was stale, and the subagent caught it

The mockup showed **target tonic inside the policies disclosure** and omitted **range policy**. The
plan's prose has the tonic outside (it defaults per file, so it is not set-once) and range policy in.
The agent implemented the plan, flagged the divergence rather than silently following the picture,
and the artifact is now corrected. Worth remembering that a mockup can drift from the spec it
illustrates.

---

## Phases 5-8 - the transform pipeline

Built largely in parallel by subagents, each reviewed against its own verification rather than its
report. **515 tests.**

### The scale library - 88 scales, all cited

| File | Scales | Notes |
|---|---|---|
| `east-asia.json` | 13 | Wusheng + a Pythagorean Gong variant; Japanese variant collisions resolved by naming, not merging |
| `europe.json` | 12 | 7 modes, harmonic/melodic minor, 3 Balkan |
| `americas.json` | 3 | Blues + two pentatonics |
| `south-asia-thaats.json` | 10 | All Bhatkhande thaats |
| `middle-east.json` | 16 | Arabic maqamat, **including three Hijaz variants** |
| `turkish-makam.json` | 15 | AEU 53-comma, all badge `Close` (7.5-20.8¢) |
| `persian.json` | 10 | Farhat intervals + Vaziri tempered, both labelled |
| `africa.json` | 9 | Ethiopian kiñit + three equal-step families |
| *(72 melakarta)* | 72 | Generated in code, not authored |

**Independently verified**: 0 load failures, 0 duplicate ids, no placeholder sources, and every
`Exact` badge belongs to a scale explicitly named as a notated 12-TET form.

Two data decisions worth knowing:

- **Native American flute scales were omitted deliberately.** The commonly-marketed "NAF pentatonic
  minor" was introduced in the 1970s-80s by a flute maker adapting the Japanese shakuhachi minor
  pentatonic; it is not an attested indigenous tuning and Flutopedia cites no historical source.
  Shipping it under that label would misattribute a modern invention. **If someone asks why the
  Americas region is thin, this is why** - do not "fix" it by adding them back uncited.
- **Hijaz ships in three forms** - notated 12-TET, a performed just intonation (2nd at 128.3¢), and
  a 13th-century al-Shirazi tuning (2nd at 150.6¢). This closes review finding S3, which objected to
  shipping only the 12-TET caricature of the most famous Arabic jins in an app built to avoid
  exactly that.

### Channel-wide controllers - gap closed, with one stated limitation

**Closed.** `TrackInfo` now carries `ControllerValues` and `ChannelPressure`, `MidiFileLoader`
captures them, and `MidiFileExporter` feeds them to `PitchBendEncoder.SetupSequence`. Proven end to
end by `DerivedChannelsEachCarryTheSourceChannelsVolumeAndSustain`, which restyles into Rast, exports
microtonally, reloads, and asserts both derived channels carry the source's CC7 and CC64 - not just
that capture happened.

Not a whitelist: CC1 and CC91 are captured exactly like CC7/10/11/64. Excluded are CC0/CC32 (bank
select, paired with the program change) and CC121/CC123 (commands, not state, handled by the encoder).

**Known v1 limitation:** only the state *before the first note* is captured, so **mid-piece
controller automation is not mirrored onto derived channels**. A file that sweeps volume partway
through will not have that sweep on its bent channels. Fixing it means emitting controller changes
at their original ticks on every derived channel - straightforward, but out of v1 scope and worth a
decision rather than a silent implementation.

### The pipeline

`KeyDetector` → `ScaleDegreeMapper` / `NearestPitchMapper` → `RangeEnforcer` → `CollisionResolver`
→ `RestyleEngine`, with `OffsetClusterer` → `ChannelBudget` → `PitchBendEncoder` for output.

`RestyleEngine` is a pure function: 20,000 notes in ~3 ms against a 16 ms budget, source never
mutated, same inputs always the same output.

**`ChannelBudget` implements the adaptive uniform tolerance**, and its central invariant has a
parameterised test across 1, 3, 4, 8, 15 and 40 tracks: *every surviving track always gets the same
cluster count*. A single scalar makes mixed tunings unrepresentable rather than merely discouraged -
which matters, because per-track degradation clashes 40¢ on Slendro's degrees 1 and 4.

Measured ladder behaviour, matching the plan exactly: 3 tracks fit at 5¢; 5 tracks resolve to 3
clusters at 25¢ (±10¢ error); 7 tracks to 2 clusters at 50¢ (±20¢).

---

## Phase 4 - the piano roll (complete)

**Gate met and measured**: a 20,000-note file renders, and scrolling allocates **0 bytes per frame**.

### The design decision that made the gate testable

The culling and layout maths lives in `Controls/PianoRollGeometry.cs`, entirely free of Avalonia;
`Controls/PianoRoll.cs` is a thin drawing shell over it. That split exists for one reason: a claim
about per-frame allocation is only worth making if it can be measured, and measuring it through
Avalonia's render loop would be far harder than measuring a pure function. `PianoRollGeometry.Cull`
writes into a caller-supplied `Span<NoteQuad>` and allocates nothing.

Measured on a generated 20,000-note, 8-track file:

```
load + flatten : 321 ms   (one-time, not per frame)
notes          : 20,000, sorted by start tick
visible at t=0 : 18,690   (default zoom frames the whole piece)
1000 frames    : 2.9 ms/frame, 0 bytes allocated
60fps budget   : PASS (16.67 ms/frame)
```

### A bug this caught, worth remembering

The quad buffer was originally capped at **8192**, reasoning that nobody can read more notes than
that at once. True, and irrelevant: the default zoom frames the *whole piece*, so the 20k-note file
put 18,690 notes in view and the cap silently dropped the tail - the right-hand side of the roll
would have rendered blank. **Truncating the visible set is a correctness bug however illegible the
full set would be.** The buffer is now sized to the note count (capped at 131,072 - about 5 MB), and
`AtFitThePieceZoomNearlyEveryNoteIsVisibleAtOnce` is the regression test.

The general lesson: a cap justified by "no user could perceive the difference" is still wrong if it
changes *what is drawn* rather than *how precisely* it is drawn.

### Other decisions worth keeping

- **Culling binary-searches for `scrollTicks - maxNoteLength`, not `scrollTicks`.** Searching for the
  left edge alone drops a long note that began before it and is still sounding - a held pedal tone
  would vanish the moment its onset scrolled away, which reads as a rendering bug and is easy to ship.
- **Microtonal notes draw at their true cents**, half a row off the semitone for a quarter-tone.
  That visible offset is how a user sees the app is delivering a real tuning; a test asserts it.
- **Zoom is anchored on the pointer**, not the viewport corner. Corner-anchoring is the obvious
  implementation and feels broken - content slides away from wherever you were looking.
- Key labels are the one thing the render path cannot build allocation-free (`FormattedText`), so
  they are drawn only on C rows, only when a row is tall enough to read, and cached by note number.

---

## Phase 3 - the shell (gate met)

**The app runs.** Launched from `src/MidiRestyle.App/bin/Debug/net10.0/MIDIRestyle.exe`; the window
opens with no startup error, and every XAML binding is compile-checked because the project sets
`AvaloniaUseCompiledBindingsByDefault` and each view declares `x:DataType`.

### Built

| File | Notes |
|---|---|
| `Views/MainWindow.axaml(.cs)` | Menu bar (File/View/Scales/Help), toolbar, three panes, status bar. v1.1 items are present but disabled with a "Coming in v1.1" tooltip rather than hidden. |
| `ViewModels/MainWindowViewModel.cs` | Root VM. File picker injected as a **delegate**, so the VM is headlessly testable with no storage provider, window, or Avalonia reference. |
| `ViewModels/TrackViewModel.cs` | One row per `(track, channel)`. Channel shown 1-based. |
| `ViewModels/MetadataViewModel.cs` | File header. Says "unknown (SMPTE timebase has no tempo map)" rather than inventing a duration. |
| `ViewModels/StatusBarViewModel.cs` | Where every silent engine decision reports: tolerance escalation, muted tracks, dropped notes, existing pitch bend, settings location. |
| `Services/PathProbe.cs` | Probes writability by **attempting a write**, never by inspecting attributes. |
| `Services/SettingsService.cs`, `AppSettings.cs` | Beside-exe wins over `%APPDATA%`; source-generated JSON. |

### Verified end to end

Against a generated 3-track sample (melody / bass / drums, 120 BPM, 4/4):

```
Format     : Format 1 (multi-track)      Division : 480 PPQN
Duration   : 0:04                        Tempo    : 120 BPM
track             ch  notes range         locked  restyle
Melody             1      8 C5 - G5        False     True
Bass               2      4 G2 - C3        False     True
Drums             10      4 C2 - F#2        True    False
Selection  : Melody, Bass
Drums forced on -> WillBeRestyled = False   <- the lock is real, not cosmetic
After a bad load, the previous project is still open, with a stated cause
```

Settings round-trip, through the view model:

```
fresh start   : defaults, 1280x800
save          : written beside the exe
reload        : "Settings: beside the exe", 1024x768 restored
corrupt file  : falls back to defaults, severity Warning, reason stated - no crash, no silent reset
```

### Design notes worth keeping

- **The drum lock is enforced in the view model, not just the XAML.** Disabling a checkbox stops a
  mouse, not a binding or a future refactor. `WillBeRestyled` stays false even if `Restyle` is forced
  true, and a test asserts exactly that.
- **A failed load leaves the previous project open.** Losing the user's loaded file because the next
  one was corrupt would be its own bug.
- **v1 has no centre-pane view-tab strip** (`MainWindowViewModel.ShowViewTabs` is false, with a test).
  One view ships; three tabs with two inert would be worse than none.

---

## Phase 2 / early 5a / early 8 — domain types (complete)

Written, building clean, and **verified empirically via throwaway .NET 10 file-based scripts** — but
NOT yet covered by the test suite, because the runner was broken while they were written. Converting
that verification into real tests is the next action.

### Implemented

| File | Notes |
|---|---|
| `Tuning/MidiRounding.cs` | The single rounding gate. Every cents-to-semitone conversion routes through it so the quantiser, fidelity and clusterer cannot disagree. |
| `Tuning/Pitch.cs` | Cents-based, deliberately unbounded; `MidiNote`, `BendCents`, `IsInMidiRange`, `PitchClass` (positive modulo). |
| `Model/Note.cs` | One note type for source and restyled alike — the piano roll draws both through one path. Channel lives on the track, not the note. |
| `Model/TimeDivision.cs` | Closed hierarchy: `TicksPerQuarterNote` or `SmpteDivision`. SMPTE files genuinely have no PPQN, so this stops the metadata header printing a wrong number. |
| `Model/MidiFileFormatKind.cs` | Format 0/1/2 with the reasoning attached. |
| `Model/TrackInfo.cs` | The `(TrackIndex, Channel)` unit of scope. `IsDrums`, `IsRestylable`, `HasExistingPitchBend`. |
| `Model/TempoChange.cs` | Tempo, time signature, marker records. |
| `Model/MidiProject.cs` | Immutable project; `DurationSeconds` integrates the tempo map piecewise and returns null for SMPTE. |
| `Scales/DegreeSpelling.cs` | Carries `ResidualCents`. Documents loudly that `Alter` is NOT the MusicXML `<alter>` value. |
| `Scales/Scale.cs` | Constructor-validated. `DegreeOffsets` computed once per scale, lazily. |
| `Scales/ScaleValidationException.cs` | Carries the scale id and a reason that explains the downstream failure it prevents. |
| `Scales/TwelveTetQuantiser.cs` | Cascade rule + octave guard, returning a result object rather than throwing. |
| `Scales/TuningFidelity.cs` | Computed badges; `IsWarningIn(outputIsTwelveTet)` encodes the contextual-badge rule. |
| `Output/OffsetClusterer.cs` | Greedy **span-bounded**; exposes the escalation ladder. |

### Verification evidence (from throwaway scripts, to be converted into tests)

**Rounding — the invariant behind everything else.** Confirmed on .NET 10, real C#:

```
Rast [0,200,350,500,700,900,1050]
  banker's (default) -> offsets [-50, 0, 50]  -> 3 channels
  AwayFromZero       -> offsets [-50, 0]      -> 2 channels
  the flip is at the 1050 degree: 10.5 rounds to 10 by default, 11 away-from-zero
-1/5 = 0,  -1%5 = -1,  floor = -1,  positive modulo = 4
```

**Cluster counts — every figure the plan asserts:**

```
scale               offsets                                  @5c  @1c   expected
Rast                [-50, 0]                                   2    2   2 / 2   PASS
Slendro             [-40,-20,0,20,40]                          5    5   5 / 5   PASS
Thai 7-equal        [-42.86,-28.57,-14.29,0,14.29,28.57,42.86] 7    7   7 / 7   PASS
Pythagorean Gong    [0,1.96,3.91,5.87,7.82]                    2    5   2 / 5   PASS
Gong (12-TET)       [0]                                        1    1   1 / 1   PASS

Pythagorean Gong adjacent gaps: 1.955, 1.955, 1.955, 1.955
  -> span-bounded gives 2 clusters; single-linkage would give 1. The distinction is real.
```

**Adaptive tolerance ladder** — confirms the plan's claim exactly:

```
Slendro, 15-channel ceiling:
  tol  5c -> 5 clusters, worst error  0c, fits 3 track-channels
  tol 25c -> 3 clusters, worst error 10c, fits 5 track-channels
  tol 50c -> 2 clusters, worst error 20c, fits 7 track-channels
```

**Fidelity badges and the quantiser:**

```
Gong             Exact         0.00      Pythagorean Gong  Close    7.82
Turkish AEU Rast Close        15.10      Slendro           Approx  40.00
Rast             Approx       50.00      Thai 7-equal      Approx  42.86
cascade: [0,30,60,500,700] -> [0,100,200,500,700]      (three degrees in one semitone spread)
guard:   [...,1160]        -> rejected, ExceedsOctave  (would duplicate the tonic at 1200)
```

### Subagent contributions

Four units were built in parallel by subagents, each owning its own files.

| Unit | Files | State |
|---|---|---|
| `MelakartaGenerator` | `Scales/MelakartaGenerator.cs` + tests | **Complete and self-reported.** All 72 melakarta, Ma as the outermost loop. All four cents spot-checks exact: #1 Kanakangi `[0,100,200,500,700,800,900]`, #15 Mayamalavagowla `[0,100,400,500,700,800,1100]`, #29 Dheerasankarabharanam `[0,200,400,500,700,900,1100]`, #65 Mechakalyani `[0,200,400,600,700,900,1100]`. Chakra 6 = Rutu asserted. |
| `ScalaFileReader` | `Scales/ScalaFileReader.cs` + tests | **Complete and self-reported.** 19 tests. All four parser rules covered by named tests. Confirmed the official spec matches what the plan says - nothing to correct. |
| `MidiFileLoader` | `Io/MidiFileLoader.cs`, `Io/GeneralMidi.cs`, `Io/MidiFileLoadException.cs` + tests | **Stopped mid-verification.** Compiles; 2 of its tests fail. See Open issues 1. |
| `DiatonicSpeller` | `Scales/DiatonicSpeller.cs` + tests | **Stopped before reporting.** Compiles and its tests pass, but unconfirmed by the agent. Read before trusting. |

**On mela #56.** The generator uses **Chamaram**, per the plan. The agent flagged honestly that this
is contested: Wikipedia's main melakarta table and most modern concert usage say *Shanmukhapriya*,
while its own article notes "it is called Chamaram in Muthuswami Dikshitar school". So Chamaram is a
real sourced alternate, not an error, and the code carries a doc comment saying so. **If a musician
reviews this, that is the name to ask about.**

**One structural note:** `ScalaFileReader` declares
`[assembly: InternalsVisibleTo("MidiRestyle.Core.Tests")]` inside its own file so its token parser
can be unit-tested. That works and avoided a csproj edit, but an assembly-level attribute living in
a feature file is unusual placement - consider moving it to a dedicated `AssemblyInfo.cs` if more
internals get exposed.

### Deviations from the plan, and why

| Plan says | Built as | Why |
|---|---|---|
| `Core/Pitch/` folder | `Core/Tuning/` | `namespace MidiRestyle.Core.Pitch` + `struct Pitch` collide and make the type unusable. |
| `MIDIRestyle.sln` | `MIDIRestyle.slnx` | .NET 10 emits the XML format by default. |
| "5 projects" | 6 | The plan's own solution layout lists six; the table was stale. |
| `Cents.cs`, `PitchClass.cs` dropped | `MidiRounding.cs` added | The review dropped them as unspecified. A single rounding gate replaces them and makes the "everything rounds identically" invariant structural rather than hoped-for. |

---

## Open issues

*None blocking.* Both of the entries that stood here — the two `MidiFileLoaderTests` failures and
the unconfirmed coverage of `MidiFileLoader`/`DiatonicSpeller` — are resolved; the first is kept
below for its lesson. What remains open is listed under **Known limitations** at the end of the
v1.1 section above, and the plan's own deferrals beyond v1.1.


---

## Resolved issues (kept for the lessons)

### The two `MidiFileLoaderTests` failures - both fixture problems, the loader was right

**(a) The Format 1 multi-channel split.** Decisive finding, verified empirically:
**DryWetMIDI's `MidiFile.Write` redistributes a multi-channel chunk into one chunk per channel when
asked for `MidiFileFormat.MultiTrack`.** Writing a single 3-channel chunk reads back as three
chunks. So the file on disk really did have three tracks and the loader was correct to report
`TrackIndex` 0, 1, 2 - the fixture simply could not express the file it was trying to describe.

The scenario is real (DAWs routinely export Format 1 files with several channels in one track), so
rather than delete the test it now builds the file from **raw SMF bytes** - header with `ntrks = 1`,
one `MTrk` carrying channels 0, 1 and 9. It also asserts the premise
(`GetTrackChunks().Count() == 1`) so the test cannot silently start passing for the wrong reason.

**(b) The truncated-file exception type.** The wrong expectation was mine: I told the agent a
truncated file yields `NotEnoughBytesException`. Which type DryWetMIDI raises depends on where the
cut lands - slicing inside a chunk body gives `InvalidChunkSizeException`, which is *richer* (it
carries `ChunkId`, `ExpectedSize`, `ActualSize`). The assertion now pins the contract we actually
care about - a named, reportable cause - rather than the library's choice of type.

**Lesson:** when a test fails, establish which side is wrong before touching either. Both of these
looked like product bugs and neither was.

### `dotnet test` reported "Zero tests ran" while every test passed

Two separate causes, and I got the diagnosis wrong twice before finding them:

1. **Microsoft.Testing.Platform treats "zero tests ran" as a hard failure.** Two of the three test
   projects had no test classes, so the whole run went red because of two empty projects while the one
   project with a real test was passing the entire time. Fixed with a placeholder `[Fact]` each.
2. **`dotnet test` forwards unrecognised arguments to the test application.** I was passing the
   no-logo flag out of habit from `dotnet build`. The MTP app rejects it, prints its help, and reports
   "Zero tests ran" with every test actually passing.

Wrong hypotheses burned on the way, recorded so nobody repeats them: it is *not* an xunit.v3 4.0.0 /
MTP-v2 protocol incompatibility (pinning back to 3.2.2 changed nothing), and it is *not*
`UseMicrosoftTestingPlatformRunner` being absent (`dotnet test` passes either way). That property is
kept for a smaller, honestly-stated reason: with it the test exe is an MTP application accepting the
same options `dotnet test` uses, so the two invocation paths stop diverging exactly when you are
trying to diagnose one against the other.

**Diagnostic recipe:** run the assembly directly first
(`./tests/<Proj>/bin/Debug/net10.0/<Proj>.exe`). If it reports tests and `dotnet test` does not, the
fault is in the invocation, not the tests - then read `dotnet test`'s per-assembly `Standard output:`
block, which is where the real error hides.

### Restore failing with MSB4181 was self-inflicted, not concurrency

Several cycles were spent blaming parallel agents for
`error MSB4181: The "RestoreTask" task returned false but did not log an error`. The real cause: I had
written flag names with leading hyphens inside an **XML comment** in `Directory.Build.props`, and
**XML comments may not contain a double hyphen**. The file was malformed, so every restore failed.
MSBuild's message points at NuGet and mentions neither XML nor the file, which is what made it
misleading.

**Lesson:** after any scripted edit to a `.props`, `.csproj` or `.slnx`, validate it before concluding
anything about the build:
`python -c "import xml.etree.ElementTree as ET; ET.parse('Directory.Build.props')"`

---

## Toolchain gotchas discovered (do not rediscover these)

These cost time once already.

| Gotcha | Detail |
|---|---|
| **`.slnx`, not `.sln`** | .NET 10's `dotnet new sln` emits the XML format. `dotnet sln MIDIRestyle.sln add` fails with "Could not find solution"; use `MIDIRestyle.slnx`. |
| **No Avalonia templates installed** | `dotnet new list avalonia` finds nothing. The App project is hand-written — csproj, `Program.cs`, `App.axaml`, `app.manifest`. Do not install templates over it. |
| **VSTest is gone in .NET 10** | Without an opt-in, every test project errors: "Testing with VSTest target is no longer supported...". The opt-in lives in **`global.json`** (`"test"` / `"runner"` / `"Microsoft.Testing.Platform"`) — **not** in `dotnet.config`, and the `TestingPlatformDotnetTestSupport` MSBuild property alone is insufficient. The giveaway was the internal MSBuild variable `_SupportsGlobalJsonTestRunner`. |
| **xunit v3 needs `OutputType=Exe`** | MTP runs each test assembly as its own process. Without it: "xUnit.net v3 test projects must be executable". |
| **`Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` are legacy** | Both removed; xunit.v3 brings MTP itself. Re-adding them resurrects the VSTest path. |
| **BannedApiAnalyzers symbol syntax** | A property needs `P:`, not `M:...get_X()`. `M:System.Reflection.Assembly.get_Location()` silently matches nothing — the ban looks present while enforcing nothing. Correct: `P:System.Reflection.Assembly.Location`. **Always verify a ban by violating it.** |
| **`curl` cannot reach nuget.org here** | TLS error 35. Use `dotnet package search <id> --exact-match --format json` to check versions. |
| **Never pass `--nologo` to `dotnet test`** | Unrecognised args are forwarded to the MTP test app, which rejects them, prints help, and reports "Zero tests ran" while every test passes. Fine on `dotnet build`. This cost three wrong diagnoses. |
| **MTP fails a project with zero tests** | An empty test project turns the whole run red. Keep a placeholder `[Fact]` until real tests land. |
| **Wall-clock perf assertions inside `dotnet test` are not benchmarks** | `TwentyThousandNotesRestyleWellInsideAFrame` flaked at 51.7 ms against a 50 ms ceiling while three test assemblies and an app instance shared the CPU - the idle figure is ~3 ms. The ceiling is now 400 ms and the test is explicitly a catastrophic-regression guard: it catches "went quadratic", not "got 20% slower". Use BenchmarkDotNet on an idle machine for real performance work. |
| **`dotnet test --filter` "fails" on a multi-project solution** | The filter runs against every test project, and the ones it matches nothing in report "Zero tests ran" - which MTP treats as an error, so the summary says `Failed!` and the exit code is 8 even though every matched test passed. Read the per-project lines, not the summary. To scope cleanly, filter a single project: `dotnet test tests/MidiRestyle.Core.Tests --filter "..."`. |
| **Concurrent builds collide on restore** | Parallel agents building the same `.slnx` produce `error MSB4181: The "RestoreTask" task returned false but did not log an error`. Harmless; retry once the other build finishes, or build a single project. |
| **`dotnet test`'s real error hides in "Standard output"** | The summary only says "Zero tests ran". The actual cause is in the per-assembly `Standard output:` block. |

---

## Standing decisions that are easy to get wrong

Re-read `CLAUDE.md` before writing domain code. These are the ones a plausible first implementation
gets wrong, each with the concrete failure it causes:

1. **`MidpointRounding.AwayFromZero` on every cents-to-step rounding.** The default is banker's
   rounding and quarter-tones sit exactly on the tie. Reproduced: `1050/100` gives `10` by default
   and `11` with AwayFromZero — the difference between Rast needing 2 channels and 3, with the count
   shifting by tonic.
2. **Offsets are derived from `DegreeCents` once per scale**, never from an absolute note's cents.
3. **Floor division and positive modulo** in the degree mapper. `-1 / 5 == 0` and `-1 % 5 == -1` in
   C#, so the naive formula indexes `DegreeCents[-1]` and throws on any bass note below the tonic.
4. **`OffsetClusterer` is greedy span-bounded**, not single-linkage. Pythagorean Gong's offsets have
   every adjacent gap at 1.955 cents — chaining gives 1 cluster, span-bounding gives 2.
5. **`DiatonicSpeller` branches on degree count**; heptatonic uses `step = degreeIndex` and rejects
   only at `|Alter| > 2`. A 1.5 threshold rejects 22 of the 72 melakartas.
6. **Melakarta generation puts Ma in the outermost loop.** Wrong nesting misaligns all 72 names.
7. **Never give two tracks different tunings.** When the channel budget binds, raise the tolerance
   for the whole project; mute rather than retune if even that fails.
8. **Do not use the `FF 21` MIDI Port meta event.** It is not in the MIDI specification and is
   honoured by almost nothing.

---

## How to resume

```powershell
cd C:\Projects\MIDIRestyle
dotnet build          # expect: 0 warnings, 0 errors
dotnet test           # expect: green (if not, see Open issues)
```

Then read, in order: this file, then `../CLAUDE.md` for the invariants, then the relevant section of
`plan/PLAN-midi-restyle.md` for the phase you are starting. Work TDD in `Core` — it is pure and
deterministic and the plan requires it.

v1 through v1.3 are committed as `633d412`, with the v1.4 About window committed on top. The
working tree is clean. Committing at each green boundary from here keeps resumption cheap.
