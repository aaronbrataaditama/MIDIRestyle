# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project status

**v1 is complete, the v1.1 notation work has landed on top of it, and a v1.2 presentation pass has
landed on top of that.** All twelve of the plan's phases are built, plus the three features the plan
deferred: MusicXML export, the grand-staff view and the degree view, with the shared rhythm
quantiser they were waiting on. v1.2 then rebuilt how the notation is *presented*, after the user
tried the app: the staff is now a wrapped page of systems rather than a horizontal strip, its
playhead follows playback, the degree view is a scale wheel rather than bare numerals, and the score
follows the A/B switch so it always shows what you are hearing. The authoritative
spec is at `.claude/plan/PLAN-midi-restyle.md` — read it before starting work — alongside an
independent review at `.claude/review/REVIEW-2026-08-26-independent.md` whose findings are folded in.
Treat the plan as the design record rather than a to-do list: where it says a feature is deferred to
v1.1, that is now history, and the invariants below describe what was actually built.

The plan has been reviewed twice and the invariants below are the *outcome* of those reviews, not
guesses. Several encode a specific failure that was actually reproduced. Treat them as constraints,
not preferences.

## What this is

MIDIRestyle is a portable Windows desktop app (single self-contained `.exe`, no installer) that
loads a MIDI file and **re-maps its musical scale into a different one** — Western diatonic into
Chinese Gong pentatonic, Maqam Rast, Gamelan Slendro, and ~170 other world scales. It also shows a
piano roll, track list and file metadata, and offers A/B playback of original vs restyled.

Restyling is **pitch remapping only** — never rhythm, ornamentation or articulation.

## Commands

```powershell
dotnet build                                    # whole solution
dotnet test                                     # all tests - do NOT add --nologo, see below
dotnet run --project src/MidiRestyle.App        # launch the app (no CLI file argument - open from the File menu)

# a single test / a single class
dotnet test --filter "FullyQualifiedName~ChannelAllocatorTests"
dotnet test --filter "FullyQualifiedName~ChannelAllocatorTests.RastAllocatesTwoChannels"

# run one test assembly directly - first thing to try when `dotnet test` looks wrong,
# because it isolates discovery/execution from the `dotnet test` integration
./tests/MidiRestyle.Core.Tests/bin/Debug/net10.0/MidiRestyle.Core.Tests.exe --list-tests

# portable release build — the shipping artefact
dotnet publish src/MidiRestyle.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

**This does not by itself produce a single file** — the csproj now fixes it, and the fix is not the
obvious one. DryWetMIDI ships its natives as plain MSBuild `None`/`CopyToOutputDirectory` items, not
RID-graph native assets. With the flag, the two `.dll` natives are bundled but
`Melanchall_DryWetMidi_Native64.dylib` is still copied out loose. Without the flag, all three sit
loose beside the exe **and the app still runs** — P/Invoke's default probing finds them.

**A `<None Remove="…Native64.dylib" />` in the csproj silently removes nothing.** DryWetMIDI's own
`build/Melanchall.DryWetMidi.targets` is imported through NuGet's generated
`obj/*.nuget.g.targets`, which MSBuild imports *after* the csproj's own content — so a `Remove`
earlier in document order is asked to remove items that do not exist yet, and quietly succeeds at
nothing. The working fix removes from **`@(ResolvedFileToPublish)`** instead
(`AfterTargets="ComputeResolvedFilesToPublishList"`), which is the final list after every source has
contributed. It also has to strip `*.pdb`: SkiaSharp's and HarfBuzz's RID-native symbol files land
there and `DebugType=none` does not reach them, since that only governs the App project's own output.

`AssertSingleFilePublish` runs `AfterTargets="Publish"` and fails the build if the folder holds
anything but one file. Verified: exactly one file, 50.2 MB (47.9 MiB), and it materialises its `scales/` folder
beside itself on first run. Do **not** use `IncludeAllContentForSelfExtract`: it bundles everything
but repoints `AppContext.BaseDirectory` at the extraction directory, breaking settings-beside-exe.

Do not add `PublishTrimmed` — Avalonia's reflection-based binding breaks under trimming.

**Never pass `--nologo` to `dotnet test`.** The .NET 10 SDK forwards unrecognised arguments to the
Microsoft.Testing.Platform test application, which rejects the flag, prints its help, and makes the
run report **"Zero tests ran"** — while every test is in fact passing. The flag is harmless on
`dotnet build`, which is exactly why the mistake is easy to make and hard to spot. When `dotnet test`
reports zero, read the per-assembly `Standard output:` block: the real error is there, not in the
summary. Note also that MTP treats a test project containing **zero tests** as a hard failure, so an
empty project turns the whole run red.

## Architecture

All projects target **`net10.0`** (current LTS; .NET 8 leaves support in November 2026). Avalonia
12.1.1 lists net8.0 as its minimum, not its maximum.

Three assemblies, and the boundaries between them are the important part:

- **`MidiRestyle.Core`** — the entire domain: pitch model, scale library, key detection, mapping
  strategies, channel allocation, file IO. Has **no UI dependency and never calls
  `Melanchall.DryWetMidi.Multimedia`**. This is what makes it testable headlessly, and what keeps
  the eventual Linux port viable. Referencing DryWetMIDI itself is fine here — only the
  `Multimedia` namespace is platform-bound.
- **`MidiRestyle.Playback`** — the only platform-bound assembly. Wraps DryWetMIDI's Multimedia API
  behind `IPlaybackEngine`, with a `NullPlaybackEngine` fallback. DryWetMIDI's device API supports
  Windows and macOS only, **not Linux**; the interface exists so that fact never leaks upward.
- **`MidiRestyle.App`** — Avalonia UI, MVVM via CommunityToolkit.Mvvm.

### The pipeline is non-destructive

```
Load .mid → MidiProject (immutable)
              ├→ KeyDetector → suggested tonic + mode
              └→ RestyleEngine(MidiProject, RestyleSettings) → RestyleResult (immutable, cents)
                        ├→ PianoRoll overlay          (reads cents directly)
                        ├→ NotationBuilder → NotationScore → { StaffView | DegreeView | .musicxml }
                        └→ ChannelAllocator → { A/B playback | export .mid }
```

`RestyleEngine` is a **pure function of the source model and the settings**. Both the original and
restyled models are held in memory simultaneously. Three consequences worth internalising before
changing anything here:

1. There is no undo stack, and none is wanted — changing a setting re-runs the transform.
2. The piano-roll overlay (ghost originals under solid restyled notes) is nearly free. It branches
   *before* the allocator — the roll draws true cents and has no interest in channels.
3. `ChannelAllocator` is the single path shared by playback *and* export, and both pass the **same**
   channel ceiling, so preview and exported file are always identical. There is no path by which
   they diverge — if you find yourself adding one, that is the bug.

## Invariants

These are load-bearing. Breaking any of them produces bugs that look like something else.

- **Pitch is cents, never MIDI note numbers.** `Pitch` wraps absolute cents above C-1 and exposes
  `MidiNote`/`BendCents`. Mapping algorithms work purely in cents; note numbers appear only at the
  output stage. This is what allows genuinely microtonal scales (Arabic maqam, Gamelan, Thai
  7-equal, Persian dastgāh) instead of 12-TET caricatures of them.
- **Every cents→semitone rounding uses `MidpointRounding.AwayFromZero`.** `Math.Round(double)`
  defaults to banker's rounding, and quarter-tone scales land *exactly* on the ±50¢ tie on every
  note. Under the default, Rast spells one neutral degree `E −50¢` and the other `B♭ +50¢` — two
  offsets from one inflection, so it allocates three channels instead of two and the count shifts
  with the tonic. The quantiser, `TuningFidelity` and `ChannelAllocator` must all use the same mode
  or they disagree with each other.
- **Offsets belong to the scale, never to a note.** The target tonic is a 12-TET pitch and octaves
  are exactly 1200¢, so `offset(i)` is fully determined by `DegreeCents[i]`. Compute once per scale.
  Deriving offsets from absolute note cents makes the channel count depend on tonic and octave.
- **The degree mapper needs floor division and positive modulo.** C# truncates toward zero and `%`
  keeps the sign, so `-1 / 5 == 0` and `-1 % 5 == -1` — the naive formula indexes `DegreeCents[-1]`
  and throws. Notes below the tonic are routine: any bass line has them.
- **Channel 10 (0-indexed 9) is never remapped.** It is percussion — remapping a note number
  changes which drum is struck. Excluded from restyling, from key detection's pitch-class profile,
  and from channel allocation. Because this rule is per-*channel* while the UI opt-out is
  per-*track*, `MidiFileLoader` **splits Format 0 files into per-channel pseudo-tracks** — otherwise
  a Format 0 file's single checkbox could not exclude drums.
- **Microtonal output allocates one channel per distinct cent-offset, not per voice.** Pitch bend
  is channel-wide, so grouping notes by required offset gives unlimited polyphony per channel.
  Maqam Rast needs two channels, not fifteen. Every allocated channel needs its own RPN bend-range
  setup, program change, bank select and duplicated controllers (see below). Offsets are
  **clustered within a tolerance, default 5¢**: at 1¢, Pythagorean Gong burns five channels for an
  inaudible 7.8¢ correction. Verified cluster counts, for tests: Rast 2, Slendro 5, Thai 7-equal 7,
  Pythagorean Gong 2 at 5¢ (5 at 1¢).
- **The channel budget binds in ordinary use, and the fix is uniform, never per-track.** One port
  gives 15 usable channels (16 minus drums). Allocation is keyed on `(track, channel, cluster)`, so
  the budget is `Sigma over (track,channel) of clusterCount` — four track-channels in Slendro need
  4 x 5 = 20. When it does not fit, **raise the clustering tolerance for the whole project** (5 -> 10
  -> 15 -> 25 -> 35 -> 50 cents) until it does, and report the effective tolerance and worst-case
  error. Never give different tracks different tunings: 12-TET Slendro against true Slendro clashes
  **40 cents** on degrees 1 and 4. If even one cluster each will not fit, **mute** the excess
  track-channels in preview rather than retuning them. Playback and export pass the same ceiling, so
  preview and file are always identical.
- **Do not use the `FF 21` MIDI Port meta event.** It appears nowhere in the MIDI 1.0 specification
  or its addenda, the MMA never endorsed it, and it is honoured by almost nothing — MuseScore 4
  writes a no-op copy on every track, REAPER preserves it without routing on it, Ableton ignores
  ports entirely. When ignored, `(port, channel)` collapses to `channel` and tracks silently stomp
  each other's pitch bend and RPN state.
- **`OffsetClusterer` is greedy span-bounded, not single-linkage.** Sort offsets, start a cluster at
  the lowest unassigned, extend while `candidate - clusterMin <= tolerance`, bend = cluster mean.
  The distinction is not academic: Pythagorean Gong's offsets have *every* adjacent gap at 1.955
  cents, so chaining gives 1 cluster and span-bounding gives 2 — and across ~20 Turkish makams the
  choice swings channel demand by up to 2x.
- **Degree mapping changes the range of the piece by `n_target / n_source`** — 1.4x for 7->5, so
  never assume output fits 0..127. `MappingOptions.RangePolicy` is applied in `RestyleEngine` and
  asserted again in the exporter. **Corrected 2026-08-26, verified twice independently:** the
  88-key piano range (MIDI 21..108) into Slendro on a C4 tonic reaches 4.80..127.20 cents-wise, and
  127.20 *rounds to MIDI 127*, so that case does **not** overflow. Real overflow needs material
  wider than a piano (the full 0..127 range drops 36 notes, reaching MIDI -24 and 154) or a shifted
  target tonic (C3 drops 3). Do not cite the piano case as the overflow example.
- **`RangePolicy.ShiftIntoRange` can never actually drop a note.** 0..127 spans 10.67 octaves, so
  some octave transposition always lands inside. The drop path exists and is guarded, but no test
  can reach it and the status bar will never show that cause under this policy.
- **Re-emit pitch bend and RPN range after any source `CC121` or GM-reset SysEx** — those reset the
  pitch wheel and may reset the bend range, and without re-emission everything after is detuned.
  **`CC123` is NOT one of them**: All Notes Off silences sounding notes and nothing else, which is
  exactly why the A/B switch sends CC123 *and* a separate bend reset.
- **Copy Bank Select (`CC0` then `CC32`) immediately before every Program Change**, and duplicate
  *all* channel-wide controllers and channel pressure to derived channels — not a whitelist of four.
  A Program Change without its bank select selects a different instrument on any GS/XG device.
  `MidiFileLoader` captures those values into `TrackInfo.ControllerValues` / `ChannelPressure`;
  `PitchBendEncoder.SetupSequence` emits them. **Known v1 limitation: only the state *before the
  first note* is captured**, so a volume sweep partway through a piece is not mirrored onto derived
  channels. Do not assume mid-piece controller automation survives restyling.
- **Mapped notes can collide.** Two simultaneous notes on one channel can map to the same pitch,
  producing overlapping Note On/Off pairs — ambiguous MIDI and stuck notes. `CollisionResolver`
  handles this and is a correctness requirement, not a nicety.
- **`NotationBuilder` is the single source of measures, ties, rests and voices.** The staff view,
  the degree view and MusicXML export all read one `NotationScore`. This is why the three shipped
  together: measure splitting, rest inference and voice assignment are required by the file format
  and by the renderer alike, and two implementations would eventually disagree — the exported file
  would stop matching the screen. Every consumer branches *after* the builder, never before.
- **Every voice must account for exactly its measure's length.** A voice short by one division makes
  MusicXML readers reject the file; long, and every later measure is displaced. Rests fill every
  gap, including a staff that is silent for a whole measure. `NotationBuilderTests` asserts this
  across every fixture, and the end-to-end test asserts it again on a real file. **Assert it over
  jittered input**: every fixture placing notes on exact tick boundaries is the one input class that
  cannot fail, which is why this invariant was broken for a day while its own test passed.
- **Beam grouping is computed once, in `BeamGrouper`, never by a consumer.** The staff renderer and
  the MusicXML exporter must not disagree about where a beam starts, for the same reason they must
  not disagree about measures. `NotationEntry.Beams` carries one `BeamState` per level, level 1
  first, and is empty for rests, for quarter-and-longer values, for a lone flagged note and for
  chord members. A note never carries more levels than its own `FlagCount()`.
- **A beam group never crosses a beat — and in compound time the group is the dotted quarter.**
  With `beatUnit == 8` and a numerator divisible by 3, the group is three printed beats, not one.
  That distinction is the whole reason 6/8 is written differently from 3/4; beaming it per eighth
  would make it read as 3/4 with a different denominator.
- **A beamed chord has one stem, and it spans the chord.** A chord member's `Beams` is empty by
  contract, which is indistinguishable from "not beamed" — so a renderer that stems every unbeamed
  entry gives each chord its group stem *plus* a stub per member. The stem runs from the notehead
  furthest from the beam to the beam itself, and the group's direction and slope are taken from the
  chords' extents, not from the timed head alone. A consequence worth knowing: the group cannot be
  flushed on seeing `End`, because that note may be a chord whose members have not arrived yet.
- **An *unbeamed* chord has one stem too, and `DrawNote` must not draw stems at all.** The beamed
  case above was honoured while the ordinary case was not: direction is decided per notehead, so a
  chord straddling the middle line grew stems pointing *both* ways out of one column — and at
  different x, since an up-stem attaches right of the head and a down-stem left of it — while a
  flagged chord got one flag per member. Chords route through
  `BeginChordStem`/`ExtendChordStem`/`FlushChordStem`, which accumulate the chord's extent and emit a
  single spanning stem. Reproduced 2026-08-28 on a straddling chord, a flagged chord and cello
  triads, and now pinned by `ChordStemRenderTests`. Avalonia 12 exposes no `DrawingGroup` and no
  subclassable recording `DrawingContext`, so draw calls cannot be intercepted and counted; the test
  counts tall ink columns on the rendered page instead and **compares against a single-note render**,
  so the clef, barlines and part name cancel and only the stems differ. Its empty-measure leg exists
  to prove the scan sees a stem at all. Both legs were confirmed by reintroducing the defect — do not
  trust a pixel test you have not watched fail.
- **Clefs and the quarter rest are real glyph outlines, filled — never freehand strokes.** All three
  were hand-authored beziers until 2026-08-28 and all three read wrong: the G clef came out narrow
  and mirror-ish, the quarter rest a uniform wire instead of a calligraphic zigzag. A treble clef is
  a specific figure that cannot be approximated from memory. `StaffGlyphs` holds the genuine
  outlines, taken **verbatim** from public-domain Wikimedia SVGs, in their own source coordinates so
  they can be diffed against the originals; `Normalise` maps each source file's staff geometry onto
  ours (units per staff space, and the y of the glyph's reference line). Do not retype path data into
  staff units by hand, and **fill these, never stroke them** — the contours already carry the
  thick-and-thin, so stroking one outlines that shape instead of inking it.
- **An eighth rest is a blob, a hook and a stem, and the blob is the part that reads.** Drawn without
  it — or with it at two-thirds size against a two-point hook — the glyph is a bare slanted "7",
  which is what shipped until 2026-08-28. The proportions in `DrawFlaggedRest` are measured off
  `Music-eighthrest.svg`, which is why they are not round numbers. The hook is **stroked**, not
  filled: the source runs the stroke out to the stem and back, and filling the sliver between those
  two passes leaves a wisp barely a pixel wide. Blobs step along the stem's own slope, so a sixteenth
  rest's pair sits on the stem rather than beside it.
- **The F clef's dots are placed by us, not taken from the source SVG.** That file puts its own a
  little off its F line. Everything else about the glyph is worth taking verbatim; two dots exactly
  half a space either side of the line are not.
- **Accidentals are placed from their right edge, never at a fixed offset from the notehead.** The
  ten signs differ by more than 2x in width, so one shared offset puts a double flat two-thirds of a
  space *inside* the notehead and a natural or sharp a third of the way in. Every branch of
  `DrawAccidental` positions its glyph against `StaffGeometry.AccidentalRightEdge`, leaving
  `AccidentalGapSpaces` clear of `NoteheadHalfWidthSpaces`. This was visible at ordinary zoom on
  every accidental in the score.
- **A whole note has no stem, and the test is `> NoteValue.Whole`, not "is it hollow".** Excluding
  only `Breve` leaves whole notes stemmed, which simply reads as a half note — and it appeared on
  every held note. The check lives in `StaffGeometry.HasStem` and is enforced inside
  `DrawStemAndFlags` so no caller can forget it. Note it is deliberately off-by-one from `IsHollow`:
  a half note is hollow *and* stemmed.
- **A note whose neighbour does not reach its beam level takes a hook, not a beam.** The sixteenth
  in a dotted-eighth-plus-sixteenth pair is `[End, BackwardHook]` against the eighth's `[Begin]`.
  Without hooks that pair — one of the commonest rhythms in Western music — cannot be written at all.
- **A chord member consumes no time.** `NotationEntry.IsChordMember` is MusicXML's `<chord/>`: it
  sounds with the previous note, and a cursor that advances past one corrupts the rest of the
  measure. This is the classic MusicXML export bug and it is tested explicitly.
- **Voice numbers are unique per staff, not per part.** `NotationEntry.Voice` restarts at 1 on each
  staff, but a MusicXML voice belongs to exactly one staff for the whole part — so the exporter
  offsets staff 2 by a **stride**, computed per part as the wider of `MaxVoicesPerStaff` and the
  highest voice that part actually uses. An ordinary grand staff still exports 1–4 and 5–8; a part
  whose staff genuinely needs six voices gets 1–6 and 7–12. Reusing 1 on both staves leaves a reader
  unable to say which staff a voice is in, and a *fixed* offset collides the moment a staff exceeds
  it — which it now can, see below.
- **`MaxVoicesPerStaff` is a readability threshold, not a cap; the cap is `VoiceCeilingPerStaff`.**
  Crossing four raises a diagnostic and nothing else. The old behaviour — fold the extra chord into
  the last voice — never actually kept the note: that voice's cursor was already past the whole
  span, so `length <= 0` discarded it. Measured before the fix, with dense overlapping input,
  **262 of 300 files lost at least one note, 908 notes in all**. Voices now keep being allocated up
  to a hard ceiling of 16, past which notes are **counted and named** in the diagnostics, never
  dropped in silence.
- **"Every source note survives" is the wrong invariant to test the notation layer with.**
  `CollisionResolver` legitimately merges two overlapping same-pitch notes upstream, so with
  overlapping input the source count and the notated count differ for reasons that are not
  notation's fault. Count against the *restyled* tracks instead. For the same reason, an overlap
  fixture should use scale tones: chromatic pitches restyled out of a heptatonic source collapse
  onto shared degrees and merge, which looks exactly like a note-loss bug and is not.
- **`<fifths>` is always 0 and every accidental is written out.** A restyled maqam or pentatonic is
  not a major or minor key, so no key signature is correct. Accidentals are explicit instead, with
  a `natural` emitted only to cancel an earlier sign at the same staff position in the same measure.
- **MusicXML cannot carry `ResidualCents`, and that is deliberate.** `<alter>` takes the quantised
  half-accidental (±0.5 is legal and renders as `quarter-flat`) and the leftover comma — up to 25¢
  under the notatability rule — is dropped. The residual survives in the model, and the staff view
  draws it as a small cent figure. Do not "fix" the exporter to invent a representation for it.
- **A tuplet needs a minimum number of onsets before the error comparison is even consulted.**
  `RhythmQuantiser` scores a straight grid against triplet and sextuplet grids per beat and applies
  `TupletBias`, but the bias alone is not enough and no value of it would be: a finer grid has a
  smaller worst-case error *by construction*, so on a sparse beat the tuplet always wins. Measured
  before `MinimumTupletOnsets` existed: a beat with one onset was read as a tuplet **33% of the
  time**, while four-onset beats were never wrong. The floor is 3 distinct onsets - two mark at most
  one internal division, which the straight grid already expresses, and a 3:2 bracket over a single
  note is meaningless. Onsets are counted **distinct**, so a chord is one attack. Decided per beat,
  not per file: a straight tune with a triplet turn in one bar is the normal case.
- **Spans are cut at tuplet-run boundaries before they are spelled.** A measure's beats collapse into
  maximal runs sharing one grid, and a span crossing a change of grid becomes tied pieces. This is
  what makes the decomposer's round-up path unreachable from the builder: within a run every position
  is a whole multiple of that run's shortest writable value, whereas a sextuplet position (80 ticks
  at 480 PPQN) is *not* a whole number of straight 64ths (30) - which is exactly why mixing them was
  unwritable. The cost is slightly more ties, which is the correct reading anyway, since a tuplet
  cannot extend past the beat it divides.
- **Per-entry ticks are differences between rounded absolute positions, never independently rounded
  durations.** Identical at any PPQN divisible by 48, but at 120 PPQN a 64th is 7.5 ticks and
  independent rounding leaves the measure a tick short. Differencing telescopes exactly.
- **`DurationDecomposer` expects an already-quantised span, and the caller must not assume it got
  one.** Only multiples of a 64th are writable, so the decomposer rounds *up* rather than returning
  nothing — a note must never quantise out of existence. That round-up is the Critical bug of
  2026-08-28: the builder emitted the rounded `DurationTicks` while advancing its cursor by the
  *true* span, so voices overran their bar. One note played 54 ticks late produced a 1950-division
  4/4 measure, and **234 of 300** jittered files had at least one bad bar. `Decompose` now reports
  `WrittenTicks`, and `BuildVoice`/`AppendRests` advance by *that*, so any residual round-up is
  absorbed by the following rest. Never advance a cursor by a length you did not write.
- **The staff is a wrapped page of systems, and `StaffPageLayout` owns every part of that.** Measures
  fill a system to the page width and then wrap; each system is justified so its last barline meets
  the right margin, except a final system shorter than `RaggedLastThreshold` (0.65) of its natural
  width, which stays ragged — justifying a two-bar last line across the page is the classic mark of a
  naive engraver. A single measure too wide for the page squeezes no further than `MinStretch`
  (0.35). Measure 0 never prints its own time signature: that belongs in the first system's indent,
  after the clef. The view scrolls **vertically in pixels** (`ScrollY`, `ContentHeight`) — it is a
  page, not the horizontal strip it used to be, and nothing should reintroduce a measure-valued
  scroll.
- **Click-to-seek inverts the column interpolation, and it needs the y.** `TickForX` is the exact
  inverse of `XForTick` — it walks the same note columns — because a bar's columns are spaced by
  duration weight, not linearly in time, so proportional arithmetic lands short of a whole note and
  past a run of sixteenths. And `TryTickAt` resolves the **system** before the measure: on a wrapped
  page the same x appears once per system, so ignoring y seeks into the first bar of the piece
  wherever you click on the last line. Ticks before a measure's first column are deliberately not
  invertible — `XForTick` maps all of them onto that column, so the mapping is many-to-one there.
  The control adds its own `ScrollY` before asking, which is the one part the layout tests cannot
  reach.
- **A grand staff is for keyboards that actually span middle C.** GM programs 0–7 and 16–23, and
  only when the part has notes on both sides of the split — a right-hand-only piano part does not
  need an empty bass staff running the length of the piece.
- **The degree view is a wheel, and degrees sit at their true cents angle — never evenly by index.**
  Its entire purpose is making the deviation from 12-TET *visible*, so the 12-TET reference ticks and
  the scale's actual degrees are drawn as distinct things and the gap between them is the
  information. Space the degrees evenly and Maqam Rast's neutral third becomes indistinguishable
  from a major third, which is precisely the distinction the view exists to show. It must render for
  **any** scale, notatable or not — it is the fallback when the staff cannot spell a scale, so
  degree count and microtonality both vary freely. Its header elides with an ellipsis rather than
  clipping: real tuning names run to `Slendro (Kyahi Kanyut Mesem, Mangkunegaran, Surakarta)`, and
  the fit is cached on `(scale, width)` so an ordinary playhead frame costs nothing.
- **The wheel's furniture never moves; playback only recolours it.** Every degree keeps a spoke, a
  number, a marker and a cents reading at all times, and a sounding degree is shown by recolouring
  those rather than by adding anything. The v1.2 wheel drew a bare ring at rest and grew spokes,
  haloes, octave rings and a fading trail as notes arrived, and the user's verdict on it was that it
  was confusing — the standing information and the momentary information looked alike, and there was
  no still frame to learn the control from. The cost is deliberate: **the wheel no longer encodes
  octave**, so a bass note and a melody note on the same degree light the same marker. That
  distinction was the biggest single source of the clutter, it put dots across the middle of the
  wheel where the eye had no reason to associate them with the rim, and the piano roll shows octave
  far better than a ring ever did. The trail went with it — the standing spokes already keep the
  wheel from being blank between attacks, which was the trail's own justification.
- **The wheel's twelve reference ticks carry note names, and they are always sharp-spelled.** Twelve
  o'clock is the *tonic*, not C, so the names run from the tonic's own pitch class — which needs
  positive modulo, since a tonic pitch class plus a tick index runs past B on every tonic but C.
  Sharps throughout: these name the equal-tempered grid the scale is read against, not the scale's
  own degrees, and those carry their own spelling in `Scale.Spelling`. A reference grid that
  respelled itself per scale would be a second, disagreeing opinion about the same twelve pitches.
- **A degree's cents label is its offset, not its absolute cents.** Absolute cents restate the
  marker's own position, which the wheel already shows. The offset is the number the reader cannot
  see — and it is exactly what pitch bend has to carry, so it is the figure the channel budget is
  spent on. Same reason the right rail's card reads `Deviation ±50¢ · 2 bend clusters at 5¢` rather
  than naming a fidelity grade: `Exact / Close / Approximate` grade a scale against a standard the
  user never chose and cannot act on, and "Approximate" reads as a fault in Maqam Saba rather than
  as a fact about twelve equal semitones. `FidelityReport` still owns the *warning* rule.
- **`Scale.Notatable` gates the staff; `DiatonicSpeller` decides it.** The flag alone is not the
  whole answer: a scale may be flagged notatable and still have no seven-letter spelling, because
  several dastgāhs and makams run to eight or nine degrees. Ask the speller, not the flag. The staff
  menu item is never greyed — it explains itself and offers the degree view — but `Export MusicXML`
  *is* disabled, with the reason in the menu header.
- **Headless Avalonia tests need one thread, claimed before any test runs.** `Dispatcher.UIThread`
  binds to whichever thread reaches Avalonia first and stays bound, and Avalonia objects have thread
  affinity — so with xunit running classes in parallel, a class that merely constructs an Avalonia
  type could bind it to a worker thread and leave the renderer unable to touch the compositor. That
  failed *only* in a full run, never when the render tests ran alone. `AvaloniaRenderFixture` is an
  **assembly** fixture for that reason. A module initializer also runs early enough but deadlocks:
  it holds the module lock while waiting on a thread that needs the same class's statics.
- **Tuning fidelity is computed, never hand-tagged** — derived from each scale's deviation from its
  own 12-TET quantisation, so it stays correct automatically.
- **`DegreeSpelling.Alter` is NOT the MusicXML `<alter>` value.** `Alter` is relative to the
  *major-scale degree* at the same index, which makes it tonic-independent; `<alter>` is an absolute
  alteration of the *natural letter*. On a D tonic, Hijaz's step 2 has `Alter = 0` yet notates as F#
  needing `<alter>1</alter>`. `RestyleSettings.TonicSpelling` records the tonic's letter and
  alteration, because MIDI 61 may be C# or Db and every letter downstream depends on which.
- **`DiatonicSpeller` rejects at `|Alter| > 2`, not 1.5.** Double accidentals are legitimate
  notation and legal MusicXML; a 1.5 threshold rejects 22 of the 72 melakartas, which need them
  (mela #1 Kanakangi is `C Db Ebb F G Ab Bbb`). Non-heptatonic scales may **repeat** a diatonic step
  when the alters differ and pitch stays ascending — without that the blues scale is wrongly
  rejected. More than 7 degrees returns `null` *with a diagnostic*, never silently.
- **`Scale.Notatable` is authored data; `Spelling` is derived.** Notatability is a cultural
  judgement, not a computation — Slendro *can* be approximated with quarter-tone accidentals to
  within 10¢, but no gamelan musician reads that. So `Notatable` is a hand-set flag (false for the
  equal-step families), and `Spelling` is `null` whenever it is false, regardless of what derivation
  would produce. `DegreeSpelling.Alter` is a `double` so quarter-tones are ±0.5 — the same type
  MusicXML's `<alter>` element takes.
- **`DiatonicSpeller` branches on degree count.** Exactly 7 degrees → `step = degreeIndex`, because
  a heptatonic scale uses all seven letters once; that is how Western notation works. Any other
  count → nearest diatonic step, ties resolving to the *higher* step so alterations come out as
  flats. Using nearest-step on a heptatonic scale is a bug: a melakarta with both R3 (300¢) and G3
  (400¢) has two degrees nearest to E, and the collision check would wrongly reject a scale that
  spells cleanly as `C D♯ E F G…`.
- **The scale editor's cents/ratio rule is NOT the `.scl` reader's.** A Scala file says a value with
  no decimal point is a ratio, so a bare `700` means 700/1 (~11,344 cents) — correct for a file
  format, wrong for a field someone is typing into, where `200` means 200 cents. Editor rule: a `/`
  means a ratio, anything else means cents. The ratio arithmetic is shared; the disambiguation is not.
- **`Source` on `Scale` is non-nullable.** Wrong cents values make a wrong app and would never fail
  a mechanical test, so every non-generated scale carries a citation and a test asserts it.
- **The source scale is a setting, not an assumption.** K-S detection only reports major or minor,
  but a file may already be pentatonic or in a maqam. Without `RestyleSettings.SourceScale` the app
  can restyle *into* ~170 scales but only *out of* two.
- **The fidelity badge is contextual.** Deviation is neutral information, shown calmly at all
  times. It becomes a warning *only* when output mode is 12-TET and deviation exceeds 25¢. Inverted,
  it cries wolf on every maqam or goes silent when the user is actually short-changed.
- **`RestyleEngine` must finish in under 16 ms for a 20,000-note file.** The scale list is
  arrow-key browsable and re-runs the transform per keystroke. This rules out re-parsing or
  re-allocating the model per run — which is the obvious first implementation.
- **Key detection is a suggestion, never a silent decision.** Surface the top candidates and always
  let the user override.
- **On stop and on A/B switch**, send CC123 to *every* allocated channel plus a bend reset to 8192.
  Otherwise notes hang and the next sequence inherits a stale pitch bend.

- **A computed property a control binds to needs a notification, and a test for the notification.**
  The Play button shipped permanently disabled because `CanPlay` was correct and never announced;
  every test passed, because they all asserted values. Use
  `[NotifyPropertyChangedFor(nameof(Computed))]` on the source `[ObservableProperty]` so the
  dependency lives beside its input, and assert on the names raised through `PropertyChanged`.
- **A disabled control must explain itself, and the explanation must outlive the next event.**
  "No MIDI device" was reported as a status *message*, so loading a file erased it and left a greyed
  button with no reason - which reads as a broken app. Standing explanations belong in
  `StatusBarViewModel`'s notice slots, not in `Report`.

## Conventions

- **`Playback` must keep all four `Track*` flags on, and this is load-bearing.** DryWetMIDI's
  `MoveToTime` emits nothing while stopped; the following `Start()` re-sends the tracked state
  (program, bend, controllers). That is *why* the A/B switch order is safe. But `MoveToTime` on a
  *running* `Playback` re-sends only note on/off — not CC/PC/PB — so anyone "optimising" the switch to
  leave the arriving side running would have the restyled side play at bend 8192, i.e. 12-TET, from
  the seek point until the next bend event. Silently detuned: the exact failure the reset exists to
  prevent.
- **A seek must re-establish the bend range explicitly — `Playback`'s own replay is not enough.** It
  re-sends tracked controllers in *ascending controller number*, so an authored RPN handshake returns
  as `CC6, CC38, … CC100, CC101` — data entry before the RPN-null, with no re-selection of RPN 0/0.
  It lands on whatever RPN the synth points at. On a fresh synth that is 0/0 at GM's default ±2
  semitones, which happens to equal our default, so it looks right **by luck**. Use
  `PitchBendEncoder.RetuneSequence` after any seek or switch onto the restyled side.
  Do **not** re-emit the full `SetupSequence`: it carries bank and program, and a player has no
  `SourceChannelState`, so it would send `Program 0` and reset every instrument to piano.
- **Linux lacks playback twice over, not once.** DryWetMIDI ships no Linux device native — and
  `Playback`'s default `HighPrecisionTickGenerator` P/Invokes the same library. A future ALSA or
  soft-synth engine must also supply
  `PlaybackSettings.ClockSettings.CreateTickGeneratorCallback = () => new RegularPrecisionTickGenerator()`.
  Also note device enumeration **throws** rather than returning zero on such a platform:
  `OutputDevice.GetAll()` raises `DllNotFoundException`, so `if (GetDevicesCount() == 0)` crashes.
- **`Should().Equal(...)` takes an array, not `params`, whenever you add a because-string.**
  AwesomeAssertions' `Equal(params T[])` swallows the reason as another expected element and then
  reports the collection is one item short - a confusing failure on a passing assertion. Write
  `Should().Equal([1, 2, 3], "reason")`. This has bitten three times.
- TDD for everything in `Core` — it is pure, deterministic and has no excuse not to be tested first.
  Mapping and channel-allocation logic in particular is verified with golden tests (known input +
  scale → exact expected pitches).
- The piano roll is a custom `Control` overriding `Render(DrawingContext)`, **not** a panel of
  per-note elements. Cull to the visible tick/pitch range and avoid per-frame allocation; a dense
  file is tens of thousands of notes.
- **The roll's keyboard and bar ruler are furniture, and every horizontal or vertical question is
  asked of the grid, not the control.** `RollViewport` carries `GutterWidth` and `RulerHeight`, and
  `EndTicks` / `BottomCents` measure `NoteAreaWidth` / `NoteAreaHeight`. Measure the whole control
  instead and the scrollbar claims more music on screen than there is, leaving the last bar
  permanently just past the right edge with the thumb already at its end — and click-to-seek must
  subtract the gutter too, or a click just right of the keyboard seeks a gutter's worth of ticks
  into the piece. The keyboard's rows are all the same height, because a row here is a grid row and
  the notes beside it have to line up; a real keyboard's uneven naturals would put the two out of
  register, which is why every piano-roll editor does the same. Lit keys follow the **A/B toggle**,
  not the solid layer — lighting the restyled keys while the original is sounding is the quiet lie
  the A/B switch exists to prevent.
- **Bar spacing is averaged over the piece, and a label that would collide is skipped anyway.**
  Measuring the first gap is wrong on real files: a pickup bar is routinely a fraction of the bars
  after it — one test file opens with a single 1/8 bar and continues in 4/4 — so the first gap is
  eight times too small and sparsifies the whole ruler. The interval snaps to 1/2/4/8… because
  musicians count bars in fours, and a greedy "not within 30px of the last one printed" pass on top
  keeps it legible where the metre genuinely changes.
- DryWetMIDI raises playback events on a background thread. Marshal playhead updates with
  `Dispatcher.UIThread.Post` on a ~60 Hz timer — never per MIDI event.
- **The third-party notices are embedded in the exe, and the publish gate is why.** The
  single-file build redistributes every dependency plus the .NET runtime, and the Inter faces
  compiled into it are under the SIL Open Font License, which requires the licence travel with the
  font. `AssertSingleFilePublish` allows exactly one file in the publish folder, so the notices
  **cannot** sit beside the exe - the csproj embeds the repository-root `THIRD-PARTY-NOTICES.txt`
  under a fixed `LogicalName` and the About box opens it. Adding a package silently widens what is
  redistributed, so `ThirdPartyNoticesTests` names every shipped component and fails when one is
  missing. Two traps when regenerating it: the redistributed set is **not** the package list (the
  Linux, macOS and WebAssembly native-asset packages resolve but never land in a win-x64 publish -
  take the list from an unbundled publish instead), and `Avalonia.Fonts.Inter` declares MIT in its
  nuspec while shipping no font licence at all, because that MIT covers Avalonia's code and not the
  font. Inter's real notice was read out of the font binaries' own `name` table. Never reproduce a
  licence text from memory.
- Portable-first file handling: settings and the writable `scales/` folder live beside the exe,
  falling back to `%APPDATA%` when that directory is read-only (read-only USB, Program Files).
  Canonical scale JSON is embedded as an assembly resource *and* written out on first run, so a
  lone copied `.exe` still works.
- The 72 Carnatic melakarta are **generated** (6 Ri/Ga × 6 Dha/Ni × 2 Ma positions, with Sa=0 and
  Pa=700 fixed), not hand-authored as JSON.
