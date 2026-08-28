# Independent review — the v1.1 notation layer — 2026-08-28

Fresh-context adversarial review of `src/MidiRestyle.Core/Notation/` (14 files), the two new view
controls and their geometry, the notation parts of `MainWindowViewModel`, and the 13 test classes
covering them. Conducted blind to the authoring sessions' reasoning. Baseline confirmed before
starting: `dotnet build` → 0 warnings, `dotnet test` → **1112 passed, 0 failed**.

Verification was done by reproduction, not by reading. A scratch harness outside the repo
(`…/scratchpad/fuzz`) referenced the built `MidiRestyle.Core.dll` and `MIDIRestyle.dll` and drove
the real production code. No file under `src/` or `tests/` was modified.

**Verdict: the notation layer's own headline invariant is broken in ordinary use, and the shipped
test suite cannot see it.** The design is good — the single-builder architecture is genuinely the
right shape, and the pieces that were hard to get right (frame conversion in `NoteSpeller`, floor
division in `DegreeReader`, the chord cursor and `<backup>` in the exporter, the MusicXML child
order) are all correct. The failures are concentrated in the *arithmetic seam* between the
quantiser, the decomposer and the builder, and in one culling loop in `DegreeView` whose comment
asserts a property the model does not have.

Findings: **1 Critical · 3 High · 6 Medium · 8 Low · 5 test-quality**.

---

## Critical

### C1 — Voices routinely overrun their measure; the exported MusicXML is longer than its own time signature

`src/MidiRestyle.Core/Notation/DurationDecomposer.cs:78–84`,
`src/MidiRestyle.Core/Notation/NotationBuilder.cs:390–424` and `445–474`.

`CLAUDE.md` states the invariant plainly: *"Every voice must account for exactly its measure's
length… long, and every later measure is displaced."* It does not hold.

`DurationDecomposer.Decompose` deliberately rounds **up** when a remainder is shorter than a 64th:

```csharp
if (chosen is null)
{
    // The remainder is shorter than a 64th. Returning empty here would silently delete
    // the note, so the shortest available value is written and the span rounds up.
    parts.Add(candidates[^1] with { Dots = 0 });
    break;
}
```

That decision is defensible in isolation and is documented in `CLAUDE.md`. The bug is that
**nothing downstream compensates for it.** `NotationBuilder.BuildVoice` emits one entry per part,
each carrying `DurationTicks = round(part.Ticks(ppqn))`, but then advances its cursor by the *true*
span:

```csharp
long partTicks = (long)Math.Round(parts[p].Ticks(ppqn));   // line 397 — may exceed the span
...
cursor = start + length;                                    // line 424 — the true span
```

`RestsFor` (445–474) has the identical shape. So the sum of written `DurationTicks` in a voice
exceeds `measure.LengthTicks` whenever any span in that measure is not an exact multiple of a 64th.

**How an ordinary file gets there.** Spans stop being 64th-multiples as soon as a beat is read on a
tuplet grid (a triplet 16th at 480 ppqn is 40 ticks; a sextuplet step is 80) or a barline cuts one.
Combined with H1 below, that is the common case, not the exotic one.

**Minimal reproduction** — one note, played 54 ticks late (11 % of a beat, ordinary human timing),
ppqn 480, 4/4:

```
quantised: start=80 len=480 tuplet=6:4
measure 1: len=1920 writtenSum=1950   *** OVERRUN by 30 divisions ***
   rest start=0    ticks=60  written=ThirtySecond
   rest start=60   ticks=30  written=SixtyFourth      <- 90 ticks written for an 80-tick gap
   note start=80   ticks=320 written=Quarter [6:4]
   note start=400  ticks=80  written=Sixteenth [6:4]
   note start=480  ticks=80  written=Sixteenth [6:4]
   rest start=560  ticks=360 written=Eighth.
   rest start=920  ticks=30  written=SixtyFourth      <- two adjacent 64th rests
   rest start=950  ticks=30  written=SixtyFourth
   rest start=980  ticks=960 written=Half
```

The exporter faithfully writes those `<duration>` values, so the emitted `<measure number="1">`
totals 1950 divisions against `<divisions>480</divisions>` and `4/4`. MuseScore and Finale both
treat an overlong measure as a defect: the reader either reports it or silently pushes the barline,
displacing everything after it — precisely the failure the invariant names.

**Frequency, measured.** 300 pseudo-random single-track files at ppqn 480 in 4/4, 1–24 notes each,
default `QuantiseOptions`:

| | measures overrun in ≥ 1 voice |
|---|---|
| default (`DetectTuplets = true`) | **250 / 300** |
| `DetectTuplets = false` | **0 / 300** |

A broader 400-iteration fuzz across ppqn {96, 120, 192, 384, 480, 960}, signatures
{4/4, 3/4, 6/8, 5/4, 7/8, 2/2, 12/8} and scales {Major, Rast, Slendro} produced **2173** distinct
voice-overrun failures.

**Suggested fix.** Make the builder the authority on time, not the decomposer. Either
(a) have `Decompose`/`DecomposeAt` return the tick total they actually spelled and have
`BuildVoice`/`RestsFor` advance the cursor by *that*, absorbing the difference in the following
rest; or (b) pre-quantise every span to a writable grid before decomposition and assert the result.
(a) is smaller and keeps the "never quantise a note out of existence" rule. Either way the
`NotationBuilderTests` assertion should be run over jittered input (see T2/T5).

---

## High

### H1 — A beat containing one note is read as a tuplet a third of the time; the `TupletBias` guard does not reach it

`src/MidiRestyle.Core/Notation/RhythmQuantiser.cs:96–134`.

`ChooseGrid` scores each candidate grid by *mean absolute distance from the nearest grid line*, then
multiplies a tuplet's score by `TupletBias` (2.0). With enough onsets in the beat this works
exactly as documented. With one or two onsets it does not, because a finer grid has a smaller
worst-case error by construction: the straight 16th grid has max error 60 ticks at 480 ppqn, the
sextuplet grid 40. A single onset in the 40–80 tick band beats the bias.

Measured over all 480 possible onset offsets within one beat, one note, default options:

```
single-note beat: tuplet grid chosen for 160/480 onset offsets (33.3%)
four 16ths with +/-20 tick jitter: tuplet grid chosen in 0/2000 beats (0.0%)
```

So the bias is fully effective on dense beats and completely ineffective on sparse ones — and a
melody line is mostly sparse beats.

The consequences are worse than a wrong ratio. In the C1 repro the single note came back as
`Quarter [6:4]` — a *sextuplet quarter*, exported as `<type>quarter</type>` with
`<time-modification><actual-notes>6</actual-notes><normal-notes>4</normal-notes></time-modification>`.
That is arithmetically consistent but musically meaningless, and it is what a reader will print.
It also sets the snap step for the whole beat, which is what generates the non-64th spans C1 then
mis-spells.

**Suggested fix.** Require a minimum number of onsets before a tuplet grid is even a candidate
(three is the natural floor — a tuplet is a statement about how a beat is *divided*, and one onset
divides nothing), and/or require the tuplet grid to explain onsets the straight grid cannot, rather
than merely scoring lower. A pure error-ratio comparison will always favour the finer grid on sparse
data whatever the bias constant is.

### H2 — `DegreeView` silently drops whole staves and voices; its culling comment states a property the model does not have

`src/MidiRestyle.App/Controls/DegreeView.cs:404–412`, and the claim it rests on at
`src/MidiRestyle.Core/Notation/NotationModel.cs:63`.

```csharp
// Entries are time-ordered within a measure (interleaved across voices/staves, but
// never regressing in start tick - see NotationMeasure's own remarks), so once a group
// starts past the right edge nothing later in this measure can be visible either.
if (x > bounds.Width + CullMarginPx)
{
    break;
}
```

`NotationMeasure`'s remarks do say "with every voice and staff interleaved in time order", but
`NotationBuilder.BuildMeasure` does not interleave: it appends staff 1 voice 1's complete timeline,
then staff 1 voice 2's, then staff 2's. Start ticks therefore **do** regress. Verified against the
real builder:

```
ENTRY ORDER in measure 1 (grand staff):
  s1 v1 start=0    note
  s1 v1 start=480  note
  s1 v1 start=960  rest
  s2 v1 start=0    note
  => start ticks regress within the measure: True
```

Replaying `DrawMeasureEntries`' exact loop against that measure:

```
zoom=0.6 px/tick, viewport width=800px  (measure is 1152px wide)
   s1 v1 start=    0 x=     16 -> drawn
   s1 v1 start=  480 x=    304 -> drawn
   s1 v1 start=  960 x=    592 -> drawn
   s1 v1 start= 1440 x=    880 -> SKIPPED   (fires the break)
   s2 v1 start=    0 x=     16 -> SKIPPED   (visible, but never reached)
   loop broke early: True; staff-2 entries drawn: 0 of 1
```

The entire left hand of a visible measure is not drawn. This fires whenever a measure is wider than
the viewport (routine at the upper half of the 0.04–0.6 zoom range: a 4/4 bar at 480 ppqn is 1152 px
at max zoom) and, more insidiously, on the **rightmost partially-visible measure at every zoom** —
its voice-1 tail always crosses the right edge, so every later voice and staff in that measure is
lost on every frame.

**Suggested fix.** Do not `break`; `continue`. The cheap correct form is to keep the `break` only
if the model is changed to guarantee monotonicity, and to fix `NotationMeasure`'s doc comment either
way — one of the two is wrong and the comment is the load-bearing half.

### H3 — `NotationBuilder.Build` costs ~0.4 s on a dense file and runs on the UI thread on every scale change

`src/MidiRestyle.Core/Notation/NotationBuilder.cs:190–231`;
`src/MidiRestyle.App/ViewModels/MainWindowViewModel.cs:460`.

`ApplyRestyle` now does `RestyleEngine.Restyle` **and** `NotationBuilder.Build` synchronously. The
16 ms budget in `CLAUDE.md` is stated for the engine, but its stated purpose — "the scale list is
arrow-key browsable and re-runs the transform per keystroke" — is a property of the whole call.

Measured on 20,000 notes across four tracks over 900 bars (Debug-built `MidiRestyle.Core`, which
also puts `RestyleEngine` at 25.6 ms against its 16 ms Release budget, so scale accordingly):

```
RestyleEngine.Restyle : 25.6 ms
NotationBuilder.Build : 402.9 ms  (900 measures)
MusicXmlExporter.ToXml: 190.3 ms  (12.0 MB)
```

Even discounting Debug overhead generously, the builder is an order of magnitude past the budget.
`SplitAcrossMeasures` is the algorithmic cause: it scans the measure list from index 0 for **every
note**, `continue`-ing past every measure that ends before the note starts. That is O(notes ×
measures). Holding note count fixed at 5000 and growing the bar count:

```
5000 notes over   50 bars:  17.8 ms
5000 notes over  100 bars:  25.8 ms
5000 notes over  200 bars:  44.4 ms
5000 notes over  400 bars:  96.2 ms
```

**Suggested fix.** Binary-search the first measure (the same search `StaffGeometry.MeasureIndexForTick`
already implements) instead of scanning from zero, and consider deferring the notation build until
the staff or degree pane is actually visible — the piano roll does not read `Score` at all.

---

## Medium

### M1 — Seam: the staff view and the exporter disagree about an accidental after a tie over a barline

`src/MidiRestyle.App/Controls/StaffView.cs:785–787` vs
`src/MidiRestyle.Core/Notation/MusicXmlExporter.cs:567`.

The exporter suppresses the accidental on a tie stop or continue, with a comment saying why
("it is the same notehead sounding on, and re-marking it is wrong engraving"). The staff view has
no equivalent check: `MeasureAccidentals` is reset at every barline (`StaffView.cs:717`), so the
tied-into note in the next measure sees nothing in force and draws the sign.

Verified end-to-end — a C♯4 whole note tied across a barline, hand-built score, real exporter, real
`MeasureAccidentals`:

```
measure 1: accidental element present = True
measure 2: accidental element present = False
staff view, measure 1 tie START: draws accidental = True
staff view, measure 2 tie STOP:  draws accidental = True
```

Which convention is right is arguable (many house styles *do* restate after a barline tie). What is
not arguable is that the decision now lives in two places and they have already drifted — the exact
failure the "one `NotationScore`, every consumer branches after the builder" invariant exists to
prevent. The rule belongs in the model (an `entry.NeedsAccidental` computed once) or in one shared
helper.

### M2 — `RestsFor` never receives the tuplet, so rests inside a tuplet beat are spelled on the wrong grid

`src/MidiRestyle.Core/Notation/NotationBuilder.cs:453–454`.

```csharp
var parts = DurationDecomposer.DecomposeAt(
    start - measure.StartTicks, length, ppqn, measure.BeatTicks);   // no tuplet argument
```

`BuildVoice` passes `chord[0].Tuplet` for notes but `RestsFor` always spells straight. A rest
filling a gap on a triplet or sextuplet beat therefore cannot be expressed exactly and takes the
round-up path — the 90-ticks-for-80 line in the C1 dump. Beyond feeding C1, it is wrong on its own
terms: a rest on a triplet beat should be a triplet rest with its own `<time-modification>`.

Rests also carry no tuplet into `NotationEntry.Duration`, so neither the staff view's tuplet bracket
nor the exporter's `<time-modification>` covers them, breaking the bracket run in
`StaffView.DrawMeasureEntries` wherever a rest sits inside a tuplet group.

### M3 — The quantiser's beat is frozen at measure 1's, while the decomposer uses each measure's own

`src/MidiRestyle.Core/Notation/NotationBuilder.cs:135`.

```csharp
long beatTicks = measures.Count > 0 ? measures[0].BeatTicks : ppqn;
var quantised = RhythmQuantiser.Quantise(notes, ppqn, beatTicks, options);
```

One beat value for the whole track, taken from the first measure. `DurationDecomposer.DecomposeAt`
is then called with `measure.BeatTicks` — the *current* measure's. In a file that changes from 4/4
(beat 480 at 480 ppqn) to 6/8 (beat 240), everything after the change is quantised against a beat
that is twice the one it is then split at. Tuplet detection is grouped on the wrong beat boundaries
for the whole second half of such a file. `MeasureGridTests.CompoundTimeCountsItsPrintedBeatNotItsDottedBeat`
pins `BeatTicks` correctly; nothing pins that the quantiser uses it.

### M4 — Exported MusicXML carries no tempo at all

`src/MidiRestyle.Core/Notation/` — no occurrence of "tempo" anywhere in the layer.

`MidiProject.TempoMap` is loaded and used by playback and MIDI export, but `NotationScore` has no
field for it and `MusicXmlExporter` emits no `<sound tempo="…">` or `<metronome>`. Every exported
file therefore opens at the reader's default (quarter = 120 in MuseScore). For an app whose whole
premise is "pitch remapping only — never rhythm", handing the user a file that plays at the wrong
speed is a real loss. One `<direction><sound tempo="…"/></direction>` in the first measure, plus one
at each tempo change, would cover it.

### M5 — Per-frame heap allocation in `DegreeView.Render`, including an uncached `FormattedText` per note

`src/MidiRestyle.App/Controls/DegreeView.cs:455, 470, 480, 488, 508`.

The project's stated convention is that the render path allocates nothing per frame.
`StaffView` largely honours it (cached pens, cached geometries, cached `FormattedText`). `DegreeView`
does not:

- **line 488** — `FormattedText centsText = new(…)` is built inline, *not* through the
  `NumeralTextFor`/`MeasureNumberFor` cache. On a microtonal target every note carries a cents label,
  so every visible note constructs and text-shapes a fresh `FormattedText` on every frame. This is
  the expensive one.
- **line 508** — `FormatCents` builds an interpolated string per note per frame.
- **lines 455, 470** — `DegreeGeometry.OctaveDotYOffsets` and `UnderlineYOffsets` each return a
  freshly allocated `double[]` per note per frame (`DegreeGeometry.cs:148, 175`).
- **line 480** — `new string('-', dashCount)` per note per frame.
- `DegreeReading.Numeral` (`DegreeReading.cs:50`) allocates via `int.ToString()` per note per frame.

None is catastrophic on its own; together they are a GC pressure source proportional to visible
notes × frame rate, which is exactly what the convention exists to stop. Cache the cents text by
its integer value, and return spans or write into a stack buffer for the two offset helpers.

*(Noted and dismissed: the very large number of `new Point(...)` / `new Rect(...)` sites in both
render paths are `readonly struct` constructions and do not allocate. `StaffView.DrawResidual`'s
`string.Create` and `FlushTuplet`'s `ToString` are the same class of issue as above but bounded by
visible-note count with residual ≥ 5 ¢ and by tuplet-run count respectively — see L8.)*

### M6 — `DegreeView` ignores `TieState`, so a tied note reads as repeated attacks

`src/MidiRestyle.App/Controls/DegreeView.cs` — `Tie` appears nowhere in the file.

A note split at a barline or a beat becomes several `NotationEntry` values joined by ties. The staff
view draws them as one notehead plus an arc; the exporter writes `<tie>`/`<tied>`. The degree view
draws the numeral once per entry, so a note held over a barline prints `5 5` — two attacks in cipher
notation, not one sustained tone. The view already implements the correct idiom for sustain
(`DegreeGeometry.DashCount`, drawn at line 480); it simply is not wired to `Tie`. A continuation
entry should render as dashes, not as a numeral.

---

## Low

### L1 — `PackVoices`' overflow path can drop the note its own comment says it keeps
`NotationBuilder.cs:319–345` and `383–388`. Past four voices a chord is appended to the last voice
("The note is kept rather than dropped"), but `BuildVoice` then clamps `start` to the cursor and
`if (length <= 0) continue;` discards it outright when it lies wholly inside what that voice already
wrote. The diagnostic message is still emitted, so it is not silent, but it says the rhythm is
approximate when the note may be absent.

### L2 — An out-of-range pitch becomes a rest, and could become `<chord/><rest/>`
`NotationBuilder.cs:480–489`. `SpellOrNull` returns `null` for a pitch outside MIDI range, and
`NotationEntry.IsRest` is `Note is null` — so the note silently becomes a rest with no diagnostic,
and if it were a chord member (`n > 0`) the exporter would write a `<chord/>` followed by a
`<rest/>`. **Not currently reachable**: `MappingOptions.Range` defaults to `ShiftIntoRange` and
every policy either shifts or drops before this point. Reported because the defensive path is the
wrong shape — it should raise a diagnostic and skip the entry, not manufacture a rest.

### L3 — The disabled MusicXML menu gives the wrong reason for a notatable-flagged scale the speller rejects
`MainWindowViewModel.cs:288–292`. The header only names the scale when `Notatable` is false;
a scale flagged notatable that `DiatonicSpeller` still rejects (eight- and nine-degree dastgāhs and
makams — the exact case `CLAUDE.md` calls out) falls through to "(nothing to export yet)", which is
untrue and unactionable. `_staffDiagnostic` already holds the right sentence.

### L4 — Notes past bar 10,000 vanish with no diagnostic
`MeasureGrid.cs:65, 98`. `MaxMeasures` correctly guards against a pathological signature, but
`SplitAcrossMeasures` produces no segment for a note beyond the last measure, so it disappears. The
project's rule is that nothing is decided quietly; `NotationScore.Diagnostics` exists for this.

### L5 — Tick-domain `Math.Round` calls have no explicit `MidpointRounding`
`NotationBuilder.cs:397, 460`; `MeasureGrid.cs:109`; `RhythmQuantiser.cs:100, 129` — all default to
banker's rounding, while `RhythmQuantiser.cs:144` (the snap itself) uses `AwayFromZero`. The
`CLAUDE.md` invariant is written about cents→semitone, so this is not strictly a violation, but the
two halves of the same quantiser disagreeing on tie-breaking is the shape of bug that invariant
exists to prevent. At ppqn values where a 64th is a half-tick (120, 1000) these ties are real.

### L6 — `AvaloniaRenderFixture.Run` has no timeout
`AvaloniaRenderFixture.cs:109`. The render thread's `foreach` loop swallows any escaping exception
and *exits*; a later `Run` then enqueues work nothing will ever execute and blocks on
`done.Wait()` forever. A dead render thread hangs the test run rather than failing it. A bounded
`done.Wait(timeout)` that throws would turn a hang into a diagnosable failure. The rest of the
fixture — assembly scope, `Ready` handshake, `ExceptionDispatchInfo` capture on both sides, the
`_setupFailure`-before-`Ready.Set()` ordering — is correct.

### L7 — `DegreeGeometry.VisibleMeasureRange` is O(measures) per frame
`DegreeGeometry.cs:99–120` walks the entire measure list every frame where `StaffGeometry`
binary-searches (`StaffGeometry.cs:403–425`). Two implementations of the same culling question,
one of which is the shape the project's own convention forbids.

### L8 — `StaffView.DrawBrace` allocates two `Point[]` per grand-staff part per frame
`StaffView.cs:1354–1368`. Bounded by part count so minor, but the comment explains why the *geometry*
is not cached while leaving the arrays uncached too; two `static readonly` scratch arrays would do.

---

## Test quality

### T1 — `DurationDecomposerTests` pins the behaviour that causes C1, and nothing checks the caller compensates
`tests/…/DurationDecomposerTests.cs:110–127`. `AnUnrepresentableSpanRoundsUpByLessThanTheShortestValue`
asserts `written >= ticks` and `written - ticks < sixtyFourth` — i.e. it *specifies* the overshoot.
That is a reasonable contract for the decomposer. But there is no paired test anywhere asserting
that `NotationBuilder` absorbs the overshoot, and it does not. A green test suite documenting the
first half of a two-part contract is worse than no test.

### T2 — The end-to-end fixture is machine-perfect, so it cannot reach C1 or H1
`tests/…/NotationEndToEndTests.cs:58–78`. Every onset in `WriteSourceFile` is an exact multiple of
160 or 240 ticks at 480 ppqn. `EveryVoiceInEveryExportedMeasureAccountsForItsFullLength` is the
right assertion pointed at the one input class that cannot fail it. Adding ±30 ticks of jitter to
that fixture turns it red immediately.

### T3 — Every `RhythmQuantiser` jitter test uses four onsets per beat
`tests/…/RhythmQuantiserTests.cs:34–43`. `SlightlyLooseSixteenthsStaySixteenths` — explicitly
described as "the case that a trigger-happy tuplet detector gets wrong" — uses four onsets, which is
exactly the density at which the bias works. `AVeryShortNoteIsWidenedRatherThanLost` uses a single
note at offset 0, a grid line on every candidate grid. There is no test of a sparse, off-grid beat,
which is where H1 lives.

### T4 — `NotationRenderTests` assert only "does not throw", and its fixture's start ticks never regress
`tests/…/NotationRenderTests.cs:176–219`. Eight smoke tests, no assertion about *what* was drawn —
so "the degree view draws nothing for staff 2" passes. The hand-built `Sample()` score is also
monotonic in `StartTicks` across staves, so it does not exhibit the ordering that triggers H2. A
render test that counted draw operations, or a geometry-level test of the entry-selection loop,
would have caught it.

### T5 — `NotationBuilderTests.AssertEveryVoiceFillsItsMeasure` is the right helper on the wrong inputs
`tests/…/NotationBuilderTests.cs:72–91`. The helper is correct and is applied across nine tests, but
every fixture places notes on exact 480-tick boundaries. Driving the same helper from a seeded
pseudo-random generator (or a `[Theory]` over a handful of jitter offsets) is a two-line change that
converts the whole class into a real guard.

---

## Verified correct — do not re-litigate

These were checked specifically because they are the usual places this kind of code goes wrong.

- **`NoteSpeller`'s two-frame conversion.** `DegreeSpelling.Alter` (relative to the major-scale
  degree) → `SpelledNote.Alter` (absolute, MusicXML) is done in one place and is right. `FloorDiv`
  and `PositiveMod` are correct implementations, the letter carry past B is right, and `OctaveOf`
  correctly derives the octave from the *written* letter so C♭4 stays in octave 4. The two-pass
  `FindDegree` (tight cents first, then 12-TET quantisation) is a genuinely good design and the
  ±1-octave search radius earns its keep. 33 tests, all specific.
- **`DegreeReader.Read`.** Floor division and positive modulo done explicitly; the octave-above
  tonic is checked as a wrap candidate; `DegreeCents[0] == 0` is relied on with the reason stated.
  The `isInScale` double rule (exact tolerance **or** shares the 12-TET rounding) is correct and
  necessary.
- **The MusicXML cursor.** `<chord/>` consumes no time (`MusicXmlExporter.cs:296–301`) and the
  `<backup>` before each subsequent voice is computed from the written cursor, so the document stays
  internally consistent even when C1 makes the measure overlong. The child order of `<note>` matches
  the MusicXML 4.0 DTD exactly, including `<tie>` before `<voice>` and `<staff>` after
  `<time-modification>`. Grand-staff voice offsetting (1–4 / 5–8) is right.
- **`Sanitise`'s surrogate handling.** `XmlConvert.IsXmlSurrogatePair(value[i + 1], c)` looks like a
  transposed argument list and is not — .NET's parameter order is `(lowChar, highChar)` and `c` is
  the high surrogate. An emoji in a track name survives.
- **`Utf8StringWriter`.** Overriding `Encoding` so the declaration says `utf-8` rather than
  `utf-16` is the correct fix for a real trap.
- **`StaffGeometry`.** Clef anchors (E4 = 30 treble, G2 = 18 bass), ledger-line counts, `IsOnLine`
  (negative-safe), stem direction and the middle-line extension, `MeasureOffsets`' `count + 1`
  entries, and both binary searches are all correct. 549 lines of tests, none vacuous.
- **`MeasureGrid`.** The implicit leading 4/4, the `SignatureChanged` flag being true on measure 1,
  the compound-time `BeatTicks` (the printed eighth, not the dotted quarter), and the corrupt-
  signature fallback.
- **`StaffLayout`.** `IsKeyboard`'s pattern precedence is right (`and` binds tighter than `or`), the
  median-not-mean clef choice, and the span check that stops a right-hand-only piano part getting an
  empty bass staff.
- **`AvaloniaRenderFixture`'s core design.** Assembly-scoped, single owned thread, exception capture
  on both sides, the `Ready`/`_setupFailure` ordering. The `CLAUDE.md` note about the module-
  initializer deadlock is borne out by the code's shape.
- **The documented decisions I checked and agree with:** `<fifths>` always 0; residual cents dropped
  from MusicXML on purpose and drawn on screen instead; beaming deliberately not implemented in
  favour of always-correct flags; `Tuplet.None` as 1:1 rather than null; hand-authored glyph paths
  rather than a Unicode musical-symbols font. None of these is an oversight.

---

## Not verified

- **Whether MuseScore 4 / Finale / Dorico actually reject the overlong measures from C1**, as
  opposed to silently absorbing them. I verified the arithmetic and the emitted `<duration>` sums,
  not the readers' behaviour. This does not change the finding — the measure is wrong either way —
  but it changes how loudly it fails for a user. **UNCONFIRMED.**
- **H3's timings are from a Debug build of `MidiRestyle.Core`** (I did not want to create
  `bin/Release` under `src/`). `RestyleEngine` measured 25.6 ms in the same harness against its
  16 ms Release budget, so roughly a 1.6× Debug penalty; scaling `NotationBuilder`'s 403 ms by that
  still leaves ~250 ms. A Release measurement would sharpen the number but not the conclusion.
- **H2's real-render consequence** was demonstrated by replaying `DrawMeasureEntries`' loop verbatim
  against real builder output, not by capturing a headless bitmap and counting numerals. The loop is
  eight lines and the inputs are real, so I consider the mechanism proven; the pixel-level effect is
  inferred.
