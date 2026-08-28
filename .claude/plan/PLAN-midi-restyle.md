# MIDIRestyle — Portable MIDI Scale-Restyling Desktop App

> Status: **awaiting review**. Phase 0 (repo setup) complete; no code written yet.
> Revised twice on 2026-08-26 — see [Revision log](#revision-log). Independent review findings are
> recorded in `.claude/review/REVIEW-2026-08-26-independent.md`.

## Context

The goal is a **portable Windows desktop application** (single self-contained `.exe`, no installer,
runnable from a folder or USB stick) whose primary function is to **re-map the musical scale of a
MIDI file into a different scale** — take a Western diatonic melody and restyle it into Chinese Gong
pentatonic, Maqam Rast, Gamelan Slendro, or any of ~170 world scales. Loading and viewing a MIDI
file is step one of that pipeline, not the end goal.

Mac/Linux support is a later ambition, so the design avoids Windows-only lock-in where it is cheap
to do so, and isolates the one place where it isn't (MIDI playback devices).

### Decisions made with the user

| Decision | Choice |
|---|---|
| Stack | .NET 10 (LTS) + **Avalonia 12.1.1** (chosen over Photino.NET — Photino needs an OS-provided WebView2 runtime, which defeats "portable") |
| Tuning | **Microtonal in v1.** Cents-based pitch model throughout; output via per-offset pitch-bend channel allocation. A 12-TET compatibility mode is also offered. |
| Mapping algorithm | **Both** scale-degree mapping and nearest-pitch snapping, user-selectable; degree-mapping is the default |
| Playback | **Yes** — A/B preview of original vs restyled, through the OS synth (no bundled SoundFont) |
| Scale library | **~170 scales**, grouped by region; built-in JSON + user-defined + Scala `.scl` import |
| Restyle scope | **Pitch remapping only.** Never rhythm, ornamentation or articulation. |
| Views | Piano roll, track list + summary, file metadata header, style picker |
| Notation | **`Scale.Spelling` in v1.** MusicXML export, staff and cipher views all deferred to v1.1. |
| Spelling data | Derived where derivation is sound; **notatability is an authored flag** |
| Channel budget | **Adaptive uniform tolerance** — one tuning for the whole project, always |
| UI | Three-pane plus menu bar and status bar: scope left, result centre, transform right |

Interface design, with live mockups of the main window, menus and staff notation:
<https://claude.ai/code/artifact/8912edc9-5630-4969-a574-75675ec37bf8>

### Why microtonality is in v1

**About 31% of the shipped library cannot be expressed in 12 equal semitones** — 52 or so scales out
of ~170. (The other 110-odd are 12-TET by construction: 82 South Asian, and the European, East
Asian and American entries.) Quantising Gamelan Slendro to 12-TET yields `0,200,500,700,1000` — a
suspended pentatonic, not Slendro. The same applies to Arabic maqamat (50¢ quarter-tones), Turkish
makam (up to ~19¢ in AEU notation, 30–60¢ in performance), Persian koron (~50–66¢), Thai 7-equal
(~43¢) and African equiheptatonic and overtone scales. Shipping those as 12-TET would misrepresent
them rather than approximate them.

The enabling insight is that microtonal output needs **one channel per distinct cent-offset, not
one per voice**. Maqam Rast's two neutral degrees are both −50¢, so it needs two channels with
unlimited polyphony each — not the 15-note MPE limit. Scales need between 1 and 7 offsets; see
[The channel budget](#the-channel-budget-and-adaptive-tolerance) for why that is tighter than it looks.

The other honest measure of the work: **~95 hand-authored scale definitions**, each requiring a
citation. That, not the code, is the largest single risk in the plan.

### Assumptions (flag any you disagree with)

1. Restyling applies to all pitched tracks by default, with a per-track opt-out.
   **Channel 10 (drums) is always excluded** — remapping percussion note numbers changes which
   drum is struck.
2. Target tonic defaults to the detected source tonic (preserves register); user can override.
3. Undo is achieved by the transform being a *pure function of settings* — change a setting, it
   re-runs. No undo stack in v1.
4. No trimming (`PublishTrimmed`) in v1 — Avalonia's reflection-based binding makes it risky.
   Binary lands ~40 MB compressed, acceptable for "portable".
5. Target framework is `net10.0`. .NET 8 leaves support in November 2026, before this would ship;
   Avalonia 12.1.1 targets both.
6. Turkish makam ships a documented subset (~20 principal makams) in **AEU 53-comma notation**,
   labelled as such. The rest are reachable via `.scl` import or the custom scale editor.
7. Notation in v1 is **`Scale.Spelling` only** — the data, not any output. MusicXML export moves to
   v1.1 beside the staff and cipher views: not because `<type>` is required (it is optional, and
   exact durations need only `<divisions>` = PPQN) but because **measure splitting, rest inference
   and voice assignment** are, and the staff view needs the same machinery. `Spelling` still lands
   in v1 because retrofitting it into ~170 definitions later is tedious.
8. **All scales are octave-periodic at exactly 1200¢.** Stretched-octave tunings — which real
   gamelan use, at 1203–1212¢ — and non-octave scales such as Bohlen-Pierce are **out of scope for
   v1** and rejected at import with a stated reason. This is load-bearing: the per-scale offset model
   depends on it.

### Known limits, stated up front

- **Melakarta and thaat are scales; ragas are not.** A raga carries vadi/samvadi hierarchy,
  characteristic phrases and differing ascent/descent (*vakra*). Same for makam *seyir* and
  dastgah *gusheh*. Remapping pitches gives the skeleton, not the idiom. Surface this in the UI.
- **Notatability is a cultural judgement, not a computation.** Slendro *can* be approximated with
  quarter-tone accidentals to within 10¢ — but no gamelan musician reads that. So `Notatable` is
  authored data. Roughly ten scales are flagged false by hand: the Slendro and Pelog tunings,
  Thai/Khmer 7-equal, West African equiheptatonic, and the equidistant African pentatonics.
- **Several traditions have no single tuning, and the library says so rather than picking one.**
  Pelog, Slendro and Thai each ship two or three cited measured tunings *plus* an explicitly
  labelled idealization. Hijaz ships both its notated 12-TET form and a cited microtonal form.
  A scale's `Name` must carry its variant, and its `Source` must carry its citation.
- **AEU notation is itself an approximation of Turkish practice.** No AEU degree deviates more than
  18.9¢ from 12-TET, while performed Segâh sits at ~340–370¢ against AEU's 384.9¢. The library
  labels these "AEU" and does not claim they capture how makam sounds.
- If a source channel already uses pitch bend (e.g. guitar bends), microtonal output conflicts.
  See [Source channel state](#source-channel-state) for the specified behaviour.

---

## Architecture

Non-destructive pipeline. The loaded file is an **immutable source model**; restyling is a pure
function producing a separate result. Both live in memory at once, which makes the piano-roll
overlay and A/B playback nearly free, and removes the need for an undo stack.

```
Load .mid ──> MidiProject (immutable)
                   │
                   ├──> KeyDetector ──> suggested tonic + mode (or NoKeyDetected)
                   │
                   └──> RestyleEngine(MidiProject, RestyleSettings) ──> RestyleResult (immutable, cents)
                                    │
                    ┌───────────────┴───────────────┐
              PianoRoll overlay            ChannelAllocator ← ChannelBudget(ceiling)
              (reads cents directly)               │
                                        ┌──────────┴──────────┐
                                   A/B Playback          Export .mid
```

The piano roll branches **before** the allocator — it draws true cents and has no interest in
channels. `ChannelAllocator` takes an explicit channel ceiling from `ChannelBudget`; playback and
export pass the same value (15), so **preview and export are always identical**. There is no path
by which they diverge.

### Solution layout

```
MIDIRestyle/
  Directory.Build.props     # shared TFM, LangVersion, analyzers, package pins
  MIDIRestyle.sln
  src/
    MidiRestyle.Core/          # net10.0. Pure domain. NO UI, NO Multimedia. Fully testable headless.
      Pitch/       Pitch.cs
      Scales/      Scale.cs, ScaleValidation.cs, DegreeSpelling.cs, DiatonicSpeller.cs,
                   ScaleLibrary.cs, MelakartaGenerator.cs, ScalaFileReader.cs,
                   ScaleJsonStore.cs, TuningFidelity.cs
      Analysis/    KeyDetector.cs, PitchClassProfile.cs, KeyEstimate.cs
      Mapping/     IPitchMapper.cs, ScaleDegreeMapper.cs, NearestPitchMapper.cs,
                   MappingOptions.cs, RangePolicy.cs, CollisionResolver.cs
      Output/      OffsetClusterer.cs, ChannelBudget.cs, ChannelAllocator.cs,
                   PitchBendEncoder.cs, OutputMode.cs
      Model/       MidiProject.cs, TrackInfo.cs, RestyleSettings.cs, RestyleResult.cs
      Io/          MidiFileLoader.cs, MidiFileExporter.cs
      Restyle/     RestyleEngine.cs
    MidiRestyle.Playback/      # net10.0. The ONLY platform-bound assembly.
      IPlaybackEngine.cs, DryWetMidiPlaybackEngine.cs, NullPlaybackEngine.cs, AbSwitcher.cs
    MidiRestyle.App/           # net10.0. Avalonia + CommunityToolkit.Mvvm.
      Controls/    PianoRoll.cs          # custom-drawn, not a panel of elements
      Views/       MainWindow.axaml, MenuBar, TrackListView, StylePanelView,
                   MetadataView, StatusBarView, ScaleEditorView
      ViewModels/  MainWindowViewModel.cs, TrackViewModel.cs, StylePanelViewModel.cs,
                   StatusBarViewModel.cs, ...
      Services/    FileDialogService.cs, ScaleLibraryService.cs, SettingsService.cs, PathProbe.cs
      Assets/scales/*.json     # embedded defaults, written beside the exe on first run
  tests/
    MidiRestyle.Core.Tests/       # xUnit. TDD — see superpowers:test-driven-development.
    MidiRestyle.Playback.Tests/   # engine selection, Null path, A/B switching
    MidiRestyle.App.Tests/        # ViewModel invariants — see UI testability
  .claude/plan/PLAN-midi-restyle.md
  .claude/review/REVIEW-2026-08-26-independent.md
```

**The Core→Multimedia boundary is enforced mechanically, not by convention.**
`Melanchall.DryWetMidi.Multimedia` is *not* a separate package — 180 types in that namespace live
inside the same assembly Core references, so nothing stops a stray `using` and no compiler error
results. Phase 1 therefore adds `Microsoft.CodeAnalysis.BannedApiAnalyzers` with a
`BannedSymbols.txt` banning `N:Melanchall.DryWetMidi.Multimedia` in `MidiRestyle.Core`, promoted to
an error. Without this the invariant is decorative.

### Dependencies

Pinned exactly in `Directory.Build.props`. "Current" is not a version.

| Package | Version | Why |
|---|---|---|
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` | 12.1.1 | UI |
| `CommunityToolkit.Mvvm` | 8.4.0 | Source-generated observable properties/commands |
| `Melanchall.DryWetMidi` | 8.0.3 (MIT) | MIDI read/write; playback in the Playback assembly |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | 4.x | Enforces the Core→Multimedia ban |
| `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | pinned | Tests |
| `AwesomeAssertions` | 8.x (Apache-2.0) | **Not `FluentAssertions`** — v8+ is proprietary Xceed licensing at $130/dev/yr for commercial use. `AwesomeAssertions` is the Apache-2.0 fork with the same API; `FluentAssertions 7.x` is the alternative if the fork is unacceptable. |

---

## Core domain design

### Pitch — cents, not note numbers

```csharp
public readonly record struct Pitch(double Cents)          // absolute cents above C-1 (MIDI 0)
{
    public int    MidiNote  => (int)Math.Round(Cents / 100.0, MidpointRounding.AwayFromZero);
    public double BendCents => Cents - MidiNote * 100.0;
    public bool   InMidiRange => MidiNote is >= 0 and <= 127;
    public static Pitch FromMidi(int note) => new(note * 100.0);
}
```

`MidpointRounding.AwayFromZero` is **mandatory, not stylistic**, and applies to *every* cents→step
rounding in the codebase — `Pitch`, `TuningFidelity`, the 12-TET quantiser, `OffsetClusterer` and
`PitchBendEncoder`. `Math.Round(double)` defaults to banker's rounding, and quarter-tone scales land
*exactly* on the ±50¢ tie on every note. Verified on .NET 10: `350/100 → 4` under both modes, but
`1050/100 → 10` under the default and `11` under `AwayFromZero`. So under the default, Rast on C
spells its half-flat third `E −50¢` and its half-flat seventh `B♭ +50¢` — two offsets from one
inflection, three channels instead of two, and a count that shifts with the tonic.

`Pitch` is deliberately unbounded — it is a value type and clamping belongs to a policy, not a
constructor. Range enforcement happens in `RestyleEngine` (see [Range policy](#range-policy)) and is
asserted again in `MidiFileExporter`.

### Scale

```csharp
public sealed record Scale(
    string Id,                               // "seasia.gamelan.slendro.kanyut-mesem"
    string Name,                             // "Slendro (Kyahi Kanyut Mèsem, Solo)"
    string Tradition,                        // "Gamelan"
    string Region,                           // "Southeast Asia"
    IReadOnlyList<double> DegreeCents,       // ascending from tonic, excludes the octave
    bool Notatable,                          // authored; false for equal-step families
    IReadOnlyList<DegreeSpelling>? Spelling, // null when !Notatable or derivation fails
    string? Description,
    string Source);                          // provenance — required, validated

public readonly record struct DegreeSpelling(
    int DiatonicStep,       // 0..6, letter offset from the tonic's letter
    double Alter,           // semitones, relative to the MAJOR-SCALE degree at this index
    double ResidualCents);  // unnotated remainder after quantising Alter to a real accidental
```

**`ScaleValidation` runs in the constructor**, so the JSON loader, the `.scl` importer and the custom
scale editor all inherit it for free:

- at least 2 degrees, at most **12** (the practical notation and channel limit);
- `DegreeCents[0] == 0` exactly;
- strictly ascending;
- all degrees in `[0, 1200)` — a degree at 1200 would duplicate the tonic;
- `Source` non-empty, at least 8 characters, and matching a citation shape (an author-and-year, or
  a URL). `"TODO"` must not pass — the previous plan's provenance test was trivially satisfiable.

Without this, `n == 0` reaches `d % n` and throws `DivideByZeroException`, and non-monotonic or
≥1200 degrees silently corrupt both the quantiser and the offset model.

Examples: Gong `[0,200,400,700,900]`; Maqam Rast `[0,200,350,500,700,900,1050]`; Slendro (idealized)
`[0,240,480,720,960]`; Thai 7-equal (Ellis) `[0,171.43,342.86,514.29,685.71,857.14,1028.57]`.

**Tuning fidelity is computed, never hand-tagged.** `TuningFidelity` returns the max deviation
between a scale's true cents and its 12-TET quantisation, badged Exact (≤5¢) / Close (≤25¢) /
Approximate (>25¢). The thresholds are authored constants and are documented as such — 5¢ is
conventionally near the just-noticeable difference for melodic pitch, 25¢ is half a quarter-tone.

**12-TET quantisation rule** — a cascade, not a single push:

```csharp
q[0] = Round(d[0]);
for i in 1..n-1:  q[i] = Math.Max(Round(d[i]), q[i-1] + 100);
if (q[n-1] >= 1200) reject the scale for 12-TET mode, with a stated reason;
```

The cascade is required for three degrees inside 100¢ (which `.scl` imports produce), and the octave
guard is required because `[…,1160]` otherwise rounds to a degree at 1200, duplicating the tonic and
emitting two identical pitches per octave.

### Scale library (~170 scales, region-grouped)

| Region | Contents | Count |
|---|---|---|
| East Asia | China Wusheng (Gong, Shang, Jiao, Zhi, Yu) in 12-TET and Pythagorean; Japan (In, Yo, Hirajōshi, Iwato, Kumoi — **variants labelled by source**); Korea (Pyeongjo, Gyemyeonjo) | ~19 |
| South Asia | 10 Hindustani thaats; **all 72 Carnatic melakarta (generated)** | 82 |
| Middle East & Persia | Arabic maqamat (Rast, Bayati, Hijaz, Sikah, Saba, Huzam, Nahawand, Kurd…), several in both notated and cited-microtonal forms; ~20 principal Turkish makams in **AEU commas**; 7 Persian dastgāhs + 5 āvāz | ~45 |
| Africa | Ethiopian kiñit (Tizita, Bati, Ambassel, Anchihoye); West African equiheptatonic/Silaba; Central & Southern equidistant pentatonic and overtone scales | 10 |
| Southeast Asia | Slendro (2 cited measured + 1 idealized); Pelog (2 cited); Thai/Khmer 7-equal (Ellis idealization, labelled) | ~6 |
| Europe & Balkans | 7 diatonic modes; harmonic & melodic minor; Double Harmonic (Byzantine), Hungarian Minor, Ukrainian Dorian | 12 |
| Americas | Blues, minor pentatonic, Native American flute variants | 4 |

**Duplicates are a real hazard and are tested against.** Several published Japanese variants collide:
Hon Kumoi Joshi `0,100,500,700,800` is pitch-identical to In; Han Kumoi `0,200,300,700,800` matches
one published Hirajōshi; the Sachs/Slonimsky Hirajōshi `0,100,500,600,1000` is identical to Iwato.
The library must either name them as variants of one entry or ship them as distinct entries whose
`Name` and `Source` disambiguate — never as separate entries that look independent. Tests assert Id
uniqueness and report pitch-set duplicates across the fully merged library.

**Melakarta are generated, with the loop order stated explicitly**, because getting it wrong
misaligns all 72 canonical names:

```csharp
RIGA  = [(100,200),(100,300),(100,400),(200,300),(200,400),(300,400)];
DHANI = [(800,900),(800,1000),(800,1100),(900,1000),(900,1100),(1000,1100)];
for (maIdx = 0; maIdx < 2; maIdx++)          // Ma is the OUTERMOST loop
  for (rg = 0; rg < 6; rg++)                 // Ri-Ga is the chakra digit
    for (dn = 0; dn < 6; dn++)               // Dha-Ni varies fastest
      mela = maIdx * 36 + rg * 6 + dn + 1;   // 1..72
```

Melas 1–36 use Ma1 (500¢), 37–72 use Ma2 (600¢). Verified: #1 Kanakangi
`[0,100,200,500,700,800,900]`; #15 Mayamalavagowla `[0,100,400,500,700,800,1100]`; #29
Dheerasankarabharanam `[0,200,400,500,700,900,1100]`; #65 Mechakalyani
`[0,200,400,600,700,900,1100]`. The plan's earlier Ri-Ga-outermost order gave a different scale at
index 15 and would have shifted every name. The 72 names ship as an array and are tested — chakra 6
is **Rutu**, and #56 is canonically **Chamaram**, not the popular "Shanmukhapriya".

### Degree spelling — two frames, kept distinct

A general-purpose MIDI transcriber guesses pitch spelling from key context. This app does not: it
*chose* the target scale, so the scale carries its own spelling.

**`Alter` is relative to the major-scale degree at the same index**, which makes it
tonic-independent. It is **not** the MusicXML `<alter>` value, which is an absolute alteration of the
natural letter. On a D tonic, Hijaz's step 2 has `Alter = 0` yet notates as F♯ needing
`<alter>1</alter>`. Conflating the two frames is the trap; the conversion is explicit:

```csharp
// letter and absolute alteration, for MusicXML and the v1.1 staff view
letter        = (tonic.Letter + degreeIndex) % 7;                    // with octave carry
absoluteCents = tonicCents + Major[degreeIndex] + Alter * 100;
absAlter      = (absoluteCents - NaturalCents(letter, octave)) / 100;
```

`RestyleSettings.TonicSpelling` records the target tonic's **letter and alteration** — MIDI 61 may be
C♯ or D♭, and every letter downstream depends on which. Nothing in the previous plan recorded it.

`DiatonicSpeller` branches on degree count, because the two cases follow different rules:

- **Exactly 7 degrees** — `step = degreeIndex`, `Alter = (DegreeCents[i] − Major[i]) / 100` with
  `Major = [0,200,400,500,700,900,1100]`. A heptatonic scale uses all seven letters exactly once;
  that is how Western notation works. **Reject only if `|Alter| > 2`** — double accidentals are
  legitimate notation and legal MusicXML. A `1.5` threshold rejects **22 of the 72 melakartas**,
  because G1 (200¢) at index 2 and N1 (900¢) at index 6 both give `Alter = −2`; mela #1 Kanakangi is
  `C D♭ E𝄫 F G A♭ B𝄫`, which is its standard Western rendering.
- **Any other count** — nearest diatonic step, **ties taking the higher step** (yielding flats, which
  matches this library's conventions). A step **may repeat** when the two degrees differ in `Alter`
  and the pitch sequence stays strictly ascending; reject only on an identical `(step, alter)` pair.
  Without the repeat allowance the blues scale `[0,300,500,600,700,1000]` is rejected, because 600
  and 700 both claim G — yet everyone spells it `C E♭ F G♭ G B♭`.
- **More than 7 degrees** — no 7-letter spelling exists. Return `null` **with a diagnostic**, never
  silently. Several Persian dastgāhs and Turkish makams are 8–9 notes.

The nearest-step rule must never be used for heptatonic scales: a melakarta containing both R3 (300¢)
and G3 (400¢) has two degrees nearest to E, and the collision check would reject a scale that spells
cleanly as `C D♯ E F G…`.

**`Alter` is quantised to a real accidental.** Comma-based scales otherwise produce values no
renderer can draw — AEU Rast derives `[0, +0.038, −0.151, −0.019, +0.019, +0.057, −0.132]`, and
`<alter>-0.151</alter>` is meaningless. So `Alter` is snapped to the nearest multiple of 0.5
(natural, half, whole, sesqui) and the remainder is stored in `ResidualCents`. If any residual
exceeds **25¢**, `Spelling` is `null`. AEU Rast's worst residual is 15.1¢, so it spells as plain
naturals with the comma deviation preserved in `ResidualCents` for the v1.1 staff view to render as
comma marks. Authored `Notatable = false` always wins over derivation.

| Scale | Spelling on C |
|---|---|
| Gong | `C D E G A` — steps 0 1 2 4 5, no alterations |
| Japanese In | `C D♭ F G A♭` — ties resolve to flats |
| Blues | `C E♭ F G♭ G B♭` — step 4 repeats with different alters |
| Hijaz (notated) | `C D♭ E F G A♭ B♭` — `Alter = −1` on steps 1, 5, 6 |
| Rast | `C D E½♭ F G A B½♭` — `Alter = −0.5` on steps 2 and 6 |
| Melakarta #1 Kanakangi | `C D♭ E𝄫 F G A♭ B𝄫` — `Alter = −2` on steps 2 and 6 |
| Slendro, Thai 7-equal | `Notatable = false` → `Spelling = null` |

### Key detection — Krumhansl-Schmuckler

Duration-weighted 12-bin pitch-class profile over all non-drum notes, correlated against the 24
rotated Krumhansl-Kessler profiles (values from Krumhansl 1990, pp. 37, 81–96 — the 1982 paper gives
them only as a figure):

- Major: `6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88`
- Minor: `6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17`

The algorithm around them needs specifying, because the raw correlation is a poor confidence signal:
a plain C major scale with equal durations scores C major 0.7564 against A minor 0.7121 — a margin of
0.044 on an unambiguous input — while atonal input produces four candidates tied at exactly 0.0680.
Reporting `r` would claim 76% certainty on ambiguity; reporting the margin would claim 4% on
certainty.

```csharp
public sealed record KeyEstimate(int PitchClass, bool IsMinor, double R, double Margin);
// Confidence shown to the user is Margin = r[0] - r[1], not r[0].
```

- **No non-drum notes, or zero variance in the profile** → return `NoKeyDetected`. The Pearson
  denominator is zero and all 24 correlations are `NaN`; do not silently default to C major, since
  the detected tonic also defaults the *target* tonic.
- **Margin < 0.05** → the UI reports "ambiguous" and offers the top 3 without a winner.
- **Ties** break deterministically: lower pitch class first, then major before minor.
- Known weaknesses are surfaced rather than hidden: relative major/minor share all seven pitch
  classes, and K-S has a documented tendency to pick the dominant as tonic. Detection is always a
  suggestion, never a silent decision.

### Mapping strategies

```csharp
public interface IPitchMapper { Pitch Map(Pitch source, MappingContext ctx); }
```

**The source scale is selectable, not assumed diatonic.** K-S only reports major or minor, but a file
may already be pentatonic or in a maqam, in which case degree indices computed against a 7-note
source are wrong. `RestyleSettings.SourceScale` is any library scale, defaulting to the detected
major/minor. Without it the app can restyle *into* ~170 scales but only *out of* two.

**`ScaleDegreeMapper`** (default) — decompose the source note into a signed absolute degree index `d`
within the source scale, then re-emit at the same index in the target:

```csharp
int  n     = target.DegreeCents.Count;
int  oct   = (int)Math.Floor(d / (double)n);   // floor, NOT integer division
int  i     = ((d % n) + n) % n;                // positive modulo
double cents = targetTonicCents + oct * 1200 + target.DegreeCents[i];
```

**Floor division and positive modulo are required, not decorative.** C# truncates toward zero and
`%` keeps the sign, so `-1 / 5 == 0` and `-1 % 5 == -1` — a plain reading indexes `DegreeCents[-1]`
and throws. Notes below the tonic are routine: any bass line has them.

Wraparound is what lets a 7-note source map into a 5-note target with ascending lines staying
ascending — contour survives. Notes outside the source scale follow a policy:
`SnapToNearestSourceDegree` (default), `PassThrough`, or `Drop`.

**`NearestPitchMapper`** — precompute the target's allowed pitches across all octaves, snap each note
to the nearest, ties away from zero. Preserves absolute register, flattens contour. It uses neither
the source scale nor the detected key, so the UI **dims both controls with a reason** when this
strategy is selected; leaving them live implies an influence they do not have.

### Range policy

Degree mapping **changes the range of the piece by `n_target / n_source`** — exactly 1.4× for 7→5,
so output must never be assumed to fit 0–127.

**Corrected 2026-08-26**, after two independent derivations agreed. The 88-key piano range
(MIDI 21–108) into Slendro on a C4 tonic reaches **4.80 … 127.20**, and **127.20 rounds to MIDI
127** — so that case fits and drops nothing. The earlier claim that it produced
`(SevenBitNumber)130` was wrong: the cents figure was right, the conclusion drawn from it was not.
Genuine overflow needs either material wider than a piano — the full 0–127 range reaches MIDI
**−24** and **154**, dropping 36 notes — or a shifted target tonic, where a C3 tonic drops 3. The
inverse is real too: a 12-degree target compresses the whole range into under two octaves.

Two further findings from implementing this, both worth knowing before writing a test against it:
**`ShiftIntoRange` can never actually drop a note** (0–127 spans 10.67 octaves, so some octave
transposition always lands inside — the drop path is guarded but unreachable), and **a 5→7 mapping
never needs a range policy at all**, since compression pulls everything inward.

`MappingOptions.RangePolicy`, applied in `RestyleEngine` before output and asserted again in the
exporter:

- **`ShiftIntoRange`** (default) — shift the offending note by whole octaves (±1200¢) until it fits;
  if no octave fits, drop it and count it.
- **`FoldOctave`** — reflect back into range rather than shifting, preserving contour direction less
  faithfully but keeping every note.
- **`Drop`** — drop out-of-range notes and report the count.

Dropped notes are reported in the status bar with their cause, and drawn in the piano roll as dashed
outlines, exactly like policy-dropped notes.

### Collision resolution

Two simultaneous notes on one channel can map to the same pitch, producing overlapping Note On/Off
pairs on that pitch — ambiguous MIDI and stuck notes. `CollisionResolver` handles overlapping
same-channel same-pitch notes with a policy: `Merge` (default — keep the longest) or
`DisplaceOctave`. A correctness requirement, not polish; needs its own tests.

### Microtonal output — offsets and clustering

`OutputMode` is `Auto` (default), `TwelveTet`, or `Microtonal`. **Auto picks 12-TET when
`max|offset| ≤ tolerance`** — not when every offset is exactly zero, which was both a float-equality
test and blind to the tolerance.

**Offsets are a property of the scale, never of a note.** Because the target tonic is a 12-TET pitch
and octaves are exactly 1200¢ (assumption 8), each degree's offset is fully determined by
`DegreeCents`:

```csharp
offset(i) = DegreeCents[i] - Math.Round(DegreeCents[i] / 100.0, MidpointRounding.AwayFromZero) * 100.0;
```

Compute once per scale. Deriving offsets from absolute note cents instead makes the channel count
depend on tonic and octave — the failure described under `Pitch`.

**`OffsetClusterer` — the algorithm is specified, because the count depends on it.**

```csharp
// greedy, span-bounded. Deterministic and order-independent: the input is sorted.
sort offsets ascending;
while (unassigned remain) {
    start a cluster at the lowest unassigned offset;
    extend while (candidate - clusterMin) <= tolerance;   // span-bounded, NOT chained
    cluster bend = arithmetic mean of its members;
}
```

Span-bounded and single-linkage genuinely differ. Pythagorean Gong's offsets are
`0, 1.955, 3.910, 5.865, 7.820` — **every adjacent gap is 1.955¢** — so chaining adjacent pairs
within 5¢ yields **one** cluster while span-bounding yields **two**. Across ~20 Turkish makams the
choice swings channel demand by up to 2× (AEU Rast: 2 vs 3; Uşşak: 2 vs 4). Span-bounded is chosen
because it bounds the *error* (≤ tolerance within any cluster) rather than the gap.

### The channel budget and adaptive tolerance

This is the tightest constraint in the design and it binds in ordinary use. One MIDI port has 16
channels; excluding channel 9 (0-indexed drums) leaves **15**. Allocation is keyed on
**`(track, channel, offsetCluster)`** — matching the `(track, channel)` scope model everywhere else —
because two Format 1 tracks may legally share a channel with different programs, and one track may
use several channels. The budget is therefore `Σ over (track, channel) of clusterCount`, not
`tracks × offsets`.

Slendro needs 5 clusters at 5¢, so **four pitched track-channels need 20** and three need exactly 15
with no headroom.

**The resolution is to raise the tolerance for the whole project until it fits**, never to give
different tracks different tunings:

```csharp
foreach (tol in [userTolerance, 10, 15, 25, 35, 50]) {
    clusters = OffsetClusterer.Cluster(offsets, tol);
    if (trackChannelCount * clusters.Count <= ceiling) return (tol, clusters);
}
// At 50¢ every scale collapses toward one cluster. If trackChannelCount alone exceeds the
// ceiling, exclude the lowest-note-count track-channels from PLAYBACK and name them.
```

Slendro collapses to 3 clusters at 25¢ and 2 at 50¢, so 5 track-channels fit at ±~10¢ of error and
7 fit at ±~20¢. The UI reports the effective tolerance and worst-case error: *"tuning accuracy
reduced to ±20¢ to fit 7 tracks."*

**Why uniform and not per-track.** Degrading individual tracks to 12-TET produces *bitonality*, not
graceful degradation: a 12-TET Slendro track against a true Slendro track beats **40¢** apart on
degrees 1 and 4 and 20¢ on degrees 2 and 3 — audibly worse than either uniform choice. Raising the
tolerance keeps every track in one tuning, so the result is always internally consistent.

If even one cluster per track-channel does not fit, the excess track-channels are **muted in preview,
not retuned** — muting is honest; mixing tunings is not. Note that more than 15 pitched channels
cannot be played through a single port in any case, microtonal or not.

**Export uses the same ceiling and the same code path**, so preview and exported file are always
identical. The previous plan's multi-port export via the `FF 21` MIDI Port meta event is **removed**:
`FF 21` appears nowhere in the MIDI 1.0 specification or any of its addenda, was never endorsed by
the MMA, and is honoured by almost nothing — MuseScore 4 writes a no-op `FF 21 01 00` on every
track, REAPER preserves it without routing on it, and Ableton ignores ports entirely. When ignored,
`(port, channel)` collapses to `channel` and tracks silently stomp each other's pitch bend and RPN
state, which corrupts exactly the output the app exists to produce.

### Source channel state

Per allocated channel, emit at the start, at every source program change, **and after any source
`CC121` (Reset All Controllers) or GM-reset SysEx**. Those reset the pitch wheel to centre and may
reset the RPN bend range; without re-emission everything after that point is silently detuned.
**`CC123` is not in this list** — All Notes Off turns off sounding notes and nothing else, which is
precisely why the A/B switch sends CC123 *and* a separate bend reset.

- **RPN pitch-bend sensitivity**: `CC101=0, CC100=0, CC6=<range>, CC38=0`, then RPN-null
  (`CC101=127, CC100=127`). Default range ±2 semitones.
- **Bank Select `CC0` then `CC32`, immediately before the Program Change.** A Program Change without
  its bank select selects a *different instrument* on any GS/XG device or bank-aware soft synth —
  the same class of bug as the sustain-pedal one below, and just as silent.
- **Program Change** copied from the source channel.
- **Pitch Bend** = `8192 + (int)Math.Round(offsetCents / (range * 100) * 8192, MidpointRounding.AwayFromZero)`.
  At range 2, −50¢ → `6144`; resolution is ~0.0244¢. Offsets are bounded to ±50¢ by construction, so
  no bend overflow is reachable.
- **Duplicate all channel-wide controllers and channel pressure** from the source channel — not a
  whitelist. Volume (7), pan (10), expression (11) and sustain (64) are the ones that break loudest,
  but modulation (1), portamento (5/65), reverb (91), chorus (93) and aftertouch are equally lost on
  derived channels. Excluded from duplication: CC121 and CC123, which are handled above.
- **Existing source pitch bend** in Microtonal mode is **summed** with the tuning offset and clamped
  to the RPN range; if the sum would exceed the range, the UI warns and names the track. In 12-TET
  mode source bends pass through untouched.

The Microsoft GS Wavetable Synth honours pitch bend at the default ±2 range, so A/B preview is
accurate. Whether it honours RPN 0 to *change* that range is unverified — bench-check before making
bend range configurable, or preview and export could diverge silently.

---

## UI design

Full design with live mockups:
<https://claude.ai/code/artifact/8912edc9-5630-4969-a574-75675ec37bf8>

The layout encodes a sentence — left rail is *what* gets restyled, right rail is *how*, centre is
the result. Four decisions bind the implementation:

1. **The scale list is not in a dropdown.** It is the largest element in the right rail, always
   open, with sticky region headers. The five set-once policy controls (mapping strategy,
   non-scale-note policy, collision policy, range policy, output mode) plus the bend tolerance
   collapse into one disclosure, closed by default. Browsing scales *is* the app.
2. **Channel 10 renders as locked, not merely unchecked**, with a tooltip explaining that the note
   number selects the drum rather than the pitch.
3. **The fidelity badge is contextual.** Deviation from 12-TET is neutral information, always shown
   calmly. It becomes a *warning* only when output mode is 12-TET **and** deviation exceeds 25¢.
4. **A view that cannot render explains itself; an export that cannot proceed is disabled.** In
   v1.1, `View > Staff` is never greyed — selecting it on a non-notatable scale shows why and offers
   the Degrees view. `File > Export MusicXML` *is* greyed, with the reason stated in the menu.

**Performance budget, implied by decision 1.** Arrow-keying down the scale list re-runs
`RestyleEngine` per keystroke, so the transform should complete well inside a frame — the target is
**under 16 ms for a 20,000-note file**. It is a per-note pure function, so ~1 ms is realistic; the
budget exists to rule out re-parsing the file per keystroke, not to forbid allocating a fresh
immutable `RestyleResult`, which the architecture does by design.

Regions: a **menu bar** (`File / View / Scales / Help`), three panes, and a **status bar**.

- **Left** — track list: name, channel, instrument, note count, range, per-track "restyle"
  checkbox (locked for channel 10); plus the metadata header (format, division, duration, tempo map,
  time signature, markers).
- **Centre** — piano roll. Original notes as muted ghosts, restyled notes solid on top. Scroll/zoom
  both axes; playhead line. Microtonal notes draw at their true cents offset, visibly between
  semitone rows. Dropped notes dashed. **No view-tab strip in v1** — one view; the strip arrives with
  the v1.1 staff and cipher views.
- **Right** — style picker: suggested source key (overridable, dimmed under Nearest-snap); source
  scale (likewise); **target tonic with its letter spelling** — a per-file setting, so it sits
  *outside* the disclosure as a peer; target scale searchable and grouped by region with its
  fidelity badge; then the policies disclosure.
- **Status bar** — output mode and channel count; effective tolerance and worst-case error when
  adaptive tolerance has engaged; muted track-channels; dropped and merged note counts; warnings for
  pre-existing pitch bend; playhead position. Everything the engine decides silently has its home
  here.
- **Toolbar** — Open, Export, Play/Stop, A/B toggle.

**Piano roll:** a custom `Control` overriding `Render(DrawingContext)`, not a panel of per-note
elements. Cull to the visible tick and pitch range before drawing; avoid per-frame allocation.

### UI testability

`MidiRestyle.App.Tests` exists from phase 3, because a dozen invariants live in the view layer and
manual steps are the wrong place for them. MVVM was chosen to make these assertable, so each is a
ViewModel property with a test: `IsChannelLocked`, `IsSourceKeyEnabled`, `IsSourceScaleEnabled`,
`FidelitySeverity`, `EffectiveToleranceMessage`, `MutedTracksMessage`, `DroppedNoteCount`,
`CanExportMusicXml` + `ExportDisabledReason`.

### A/B playback

DryWetMIDI's `Playback` is built from a fixed event collection, so the mechanism must be chosen
rather than discovered. **Two `Playback` instances share one `OutputDevice`, with exactly one
started at a time.** On toggle: read the running instance's current time, stop it, send CC123 plus a
bend reset to every allocated channel, `MoveToTime` on the other, start. Target switch gap **under
30 ms**, measured in `MidiRestyle.Playback.Tests`.

The two rejected alternatives, and why: two instances running concurrently drift (independent
clocks) and both need the device; one merged sequence with muted channel groups **doubles the
channel budget**, which is already the binding constraint.

Changing the target scale during playback rebuilds the restyled sequence and re-seeks. Arrow-key
browsing is **debounced at 150 ms** so keystroke spam does not thrash the rebuild — the 16 ms
transform budget covers the transform only, not sequence construction.

**Playback threading:** DryWetMIDI raises playback events on a background thread. Marshal playhead
updates via `Dispatcher.UIThread.Post` on a ~60 Hz timer, *not* per MIDI event.

---

## File format handling

- **Format 1** (multi-track) is the primary case.
- **Format 0** puts all sixteen channels in one track. Since the drum rule is per-*channel* but the
  UI checkbox is per-*track*, a single checkbox could not exclude channel 10. `MidiFileLoader`
  therefore **splits Format 0 into per-channel pseudo-tracks at load**, so scope selection is
  uniformly `(track, channel)` everywhere downstream.
- **Format 2** holds independent sequences rather than one song. DryWetMIDI reads it without
  complaint, so this is a presentation decision, not an error path: show the sequence count and open
  the first, with the others selectable.
- **SMPTE time division** (`SmpteTimeDivision`) is a real case with **no PPQN**. The metadata header
  shows frame rate and sub-frame resolution instead; the tempo map is absent by definition.
- **Malformed input** reports the exception type, the chunk id, and expected/actual sizes where
  available, and leaves any loaded project intact. It does **not** promise a byte offset: no
  DryWetMIDI exception carries stream position, and a truncated file yields
  `NotEnoughBytesException` with `ExpectedCount = 0, ActualCount = 0`. If a position is wanted later,
  wrap the input in a position-tracking `Stream` and report on throw.

### Scala `.scl` import

The four rules that actually break `.scl` parsers, all from the official spec:

1. **`1/1` is implicit and absent from the file, while the declared count *includes* the trailing
   `2/1`.** `Scale.DegreeCents` excludes the octave, so the parser must **prepend 0 and strip a
   trailing 1200¢**. Omit the prepend and every import loses its tonic; omit the strip and the octave
   duplicates in every octave — silently wrong output no round-trip test would catch.
2. **A value containing a period is cents; otherwise it is a ratio.** So bare `700` means the ratio
   700/1 ≈ **11,304¢**, not 700 cents — an error of ~10,600¢. Also legal: `408.` (trailing period =
   cents), `-5.0` (negative cents), `10/20` (sub-unity, −1200¢). **Negative ratios are a read
   error.** Anything after a valid pitch value is ignored (`100.0 cents`, `5/4  E\`). `!` comments
   may appear between pitch lines. Files are **Latin-1**, not UTF-8.
3. **The last entry need not be `2/1`** — `bohlen-p.scl` ends `3/1`. Per assumption 8, a period other
   than 1200 ± 1¢ is **rejected with a stated reason**, not silently accepted.
4. **Cardinality is unbounded in the format.** A 31-EDO or 22-shruti import would need 31 or 22
   channels against 15, and produces duplicate and non-monotonic 12-TET quantiser output.
   `ScaleValidation`'s 12-degree cap rejects these with an explanatory message.

---

## Persistence and path probing

`SettingsService` and `ScaleLibraryService` are built in **phase 3** — they gate the first runnable
app — and their precedence rules are decided here, not at the keyboard.

- **Path resolution uses `AppContext.BaseDirectory` or `Environment.ProcessPath`.**
  `Assembly.Location` returns `''` under single-file publishing and emits warning IL3000; it is
  banned via the analyzer alongside the Multimedia namespace.
- **Writability is probed by attempting a write and catching**, never by inspecting attributes —
  attribute checks are wrong under ACLs, and a non-elevated app writing to Program Files simply
  throws `UnauthorizedAccessException` with no file virtualization.
- **Settings precedence**: beside-the-exe wins over `%APPDATA%` when both exist, and the status bar
  says which is in use. This makes the USB case predictable after the file has been run from a
  writable folder.
- **Scale precedence**: user scales > beside-the-exe `scales/` > embedded resources. If the
  first-run write to `scales/` fails, the app falls back to `%APPDATA%\MIDIRestyle\scales` **and says
  so**, so users do not drop JSON into a folder nothing reads.
- **Id collisions** across embedded, beside-exe, `%APPDATA%`, generated melakartas, `.scl` imports
  and user scales are resolved by that precedence, logged, and surfaced in the scale editor. A test
  asserts Id uniqueness across the fully merged library.

## Custom scale editor

Entry by **cents or ratios**, converted on input.

**The disambiguation rule differs from the Scala reader's, deliberately.** A `.scl` file says a value
without a decimal point is a *ratio*, so a bare `700` there means 700/1 — about 11,344 cents. That
rule is correct for a file format and wrong for a text field a person is typing into, where `200`
plainly means 200 cents. So the editor's rule is: **a `/` means a ratio, anything else means cents.**
The ratio-to-cents arithmetic and the "non-positive ratio is a hard error" rule are shared with the
reader, because those are facts about ratios rather than about the surface.

**"Mid-edit" is defined, not left to feel.** A half-typed degree must not read as an error, so a row
is *pending* exactly when its text is a strict prefix of something still completable (`5/`, `-`), and
an *error* as soon as it cannot become valid by appending characters (`abc`, `-5/4`). While any row is
empty or pending, the `Scale` constructor is never invoked and the message uses neutral
"still being entered" wording rather than the constructor's rejection phrasing.

**Ids are namespaced structurally, not by validation.** The user types a slug; the id is always
`"user." + slug`. There is no way to type an id outside that namespace, so accidentally shadowing a
shipped scale is impossible rather than merely checked for.

**A manual `Notatable` override is sticky.** It is derived from the degrees by default and refreshed
as they change — but once the user sets it explicitly, further degree edits do not silently recompute
it. A control that keeps overriding an explicit "no" the moment a cents value is nudged would be
maddening.

---

## Portability

```powershell
dotnet publish src/MidiRestyle.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none
```

**The default publish does not actually produce a single file**, and the reason is worth stating
precisely because it is easy to get wrong. DryWetMIDI ships its native binaries as plain MSBuild
`None` / `CopyToOutputDirectory` items, not RID-graph native assets. So:

- With `IncludeNativeLibrariesForSelfExtract`, the two `.dll` natives are bundled but
  **`Melanchall_DryWetMidi_Native64.dylib` is still copied out loose** — a macOS binary in a Windows
  publish folder.
- Without the flag, all three natives sit loose beside the exe **and the app still runs**, because
  P/Invoke's default probing finds them. The earlier claim that omitting the flag causes a
  `DllNotFoundException` only on a clean machine is **wrong**.

So the project file must `Remove` the `.dylib` (and the 32-bit native on a 64-bit-only build), and
**a build gate asserts the publish folder contains exactly one file.** Note the tension:
`IncludeAllContentForSelfExtract` would bundle everything but changes `AppContext.BaseDirectory` to
the extraction directory, breaking settings-beside-the-exe — so it must not be used.

- **Scales** are embedded as assembly resources *and* written to a `scales/` folder beside the exe
  on first run, so a lone copied `.exe` works while users can still add scale JSON without a rebuild.
- **Settings** as specified under [Persistence](#persistence-and-path-probing).

Mac/Linux later: Avalonia and DryWetMIDI file IO port cleanly. **DryWetMIDI ships no Linux native**
(`Native32.dll`, `Native64.dll`, `Native64.dylib` only), confirming there is no device support there;
Linux needs an alternative `IPlaybackEngine` (ALSA, or a bundled soft-synth such as MeltySynth).
`NullPlaybackEngine` keeps the app fully functional minus audio meanwhile.

---

## Build order

Each phase ends in something runnable or testable. TDD throughout for `Core`
(`superpowers:test-driven-development`).

| # | Phase | Done when |
|---|---|---|
| 0 | Repo setup: `git init`, `.gitignore`, `CLAUDE.md`, plan and review in `.claude/` | **Complete — review gate** |
| 1 | Solution scaffold, 5 projects, `Directory.Build.props` with pinned packages, BannedApiAnalyzers | `dotnet build` and `dotnet test` pass; a `using Melanchall.DryWetMidi.Multimedia` in Core fails the build |
| 2 | `Pitch`, `Scale` + `ScaleValidation`, `MidiProject`, `MidiFileLoader` incl. Format 0 splitting and SMPTE | Loads Format 0/1/2 and an SMPTE file; asserts tracks/channels/notes/tempo/division |
| 3 | Avalonia shell: menu bar, status bar, open file, track list, metadata; `SettingsService`, `ScaleLibraryService`, `App.Tests` | **First runnable app** — opens a MIDI file and describes it; settings survive a restart from a read-only folder |
| 4 | `PianoRoll` control, original notes only | Renders a 20k-note file; zoom/scroll allocate nothing per frame |
| 5a | Scale code: melakarta generator, `.scl` reader, `TuningFidelity`, `DiatonicSpeller`, JSON store | All parser, spelling, quantiser and validation tests pass against fixture scales |
| 5b | Scale data: ~95 hand-authored definitions with citations, region by region | Library loads ~170 scales; provenance, Id-uniqueness and duplicate-pitch-set tests pass |
| 6 | `KeyDetector` + `KeyEstimate` + selectable source scale | Detects known keys; returns `NoKeyDetected` on empty; ambiguity reported as ambiguous |
| 7 | `RestyleEngine`, both mappers, `RangePolicy`, collision resolver, 12-TET export | Golden tests incl. below-tonic notes and both range extremes |
| 8 | `OffsetClusterer`, `ChannelBudget`, `ChannelAllocator`, `PitchBendEncoder`, microtonal export | Rast → 2 clusters; a 5-track Slendro project fits by raising tolerance to 25¢ and reports it (unit-tested headless) |
| 9 | Style picker UI + restyled overlay in piano roll | Change a scale, see the notes move |
| 10 | `IPlaybackEngine` + DryWetMIDI implementation + `AbSwitcher` | **Core value delivered** — hear original vs restyled; switch gap under 30 ms |
| 11 | Custom scale editor | Define, save, reload a user scale; invalid input is refused with a reason |
| 12 | Portable publish + README | Publish folder contains exactly one file; it runs on a clean machine from a USB stick |

Phase 10 is the point at which the app does what it exists to do; 11–12 are completion. **Phase 5b is
the schedule risk** — it is musicology, not coding, and is tracked per region with a checklist.

**Deferred to v1.1**, all sharing one quantiser: MusicXML export; the in-app grand staff for keyboard
tracks (GM programs 1–8 and 17–24) with quantisation and hand splitting; and the degree/cipher view
for non-notatable scales, with the centre-pane view-tab strip.

---

## Verification

**Automated** — `dotnet test` from the repo root.

*Pitch and rounding*
- Cents round-tripping; the 12-TET quantiser's cascade (three degrees inside 100¢) and its octave
  guard (`[…,1160]` must be rejected, not rounded to a degree at 1200).
- **Every quarter-tone degree resolves to the same offset in every octave and under every tonic.**
  Rast yields exactly two clusters regardless of transposition — the banker's-rounding regression.

*Scales and spelling*
- Fidelity: Gong Exact; Turkish AEU Close (worst 18.9¢); Slendro 40¢, Rast 50¢, Thai 42.9¢ Approximate.
- Melakarta generator: exactly 72 distinct pitch-class sets; **#1 Kanakangi, #15 Mayamalavagowla,
  #29 Dheerasankarabharanam and #65 Mechakalyani** each assert exact cents *and* name; chakra 6 is
  Rutu; #56 is Chamaram.
- **All 72 melakarta return non-null `Spelling`**, and Kanakangi's alters are `[0,−1,−2,0,0,−1,−2]`
  — the guard against the `|Alter| ≤ 1.5` regression.
- Blues spells `C E♭ F G♭ G B♭` (repeated step, differing alters); Japanese In resolves ties to flats;
  Hijaz, Rast and Gong per the spelling table; an 8-degree scale returns `null` **with a diagnostic**;
  `Notatable = false` returns `null` regardless of derivation; JSON overrides beat derivation.
- **Frame conversion**: Hijaz on D notates `D E♭ F♯ G A B♭ C`, and on B♭ correctly — the guard
  against conflating `Alter` with MusicXML's `<alter>`.
- Every shipped `Spelling`'s alters are multiples of 0.5, with residuals ≤ 25¢.
- `ScaleValidation` rejects: 0 and 1 degree, 13 degrees, non-zero first degree, non-ascending,
  duplicate, negative, ≥1200, and `Source = "TODO"`.
- **Provenance**: every shipped non-generated scale has a `Source` of ≥8 characters matching a
  citation shape; one named scale per region asserts exact cents against its cited source.
- **Id uniqueness** and **pitch-set duplicate reporting** across the fully merged library.

*`.scl` parsing*
- Cents (`386.31`), trailing-period (`408.`), ratio (`5/4`), **bare integer treated as a ratio**,
  negative cents, sub-unity ratio, interleaved comments, trailing text ignored, Latin-1 bytes.
- **Negative ratio throws**; count mismatch throws; a non-1200¢ period is rejected with a reason; a
  31-degree file is rejected by the cardinality cap.
- **Prepend-0 / strip-trailing-2:1**: `meanquar.scl` (declared 12) yields 12 `DegreeCents` starting
  at 0 with no 1200 entry.

*Mapping*
- Golden tests per strategy: C major → Gong, Hijaz, Rast, Slendro; each non-scale-note policy; the
  degree-count wraparound; **notes a fourth below the tonic** (floor-division regression).
- **Range**: MIDI 21–108 into Slendro under each `RangePolicy` — `ShiftIntoRange` keeps every note
  in 0–127, `Drop` reports the count; asserts at MIDI 0, 1, 126, 127 for both 7→5 and 5→7.
- Collision resolver under `Merge` and `DisplaceOctave`.

*Output*
- **Clustering**: Pythagorean Gong → **2** clusters at 5¢ span-bounded and 5 at 1¢; AEU Rast and
  Uşşak assert their span-bounded counts; clustering is order-independent.
- **Channel counts**: Rast 2, Slendro 5, Thai 7-equal 7. Drums stay on channel 9, untouched.
- **Adaptive tolerance**: a 5-track Slendro project resolves to 3 clusters at 25¢ and reports the
  effective tolerance and worst-case error; a 7-track project resolves to 2 at 50¢; a 16-pitched-
  channel project mutes the excess and names it. **No test may ever produce two different tunings in
  one project.**
- Allocation keys on `(track, channel, cluster)` — asserted with a two-track/one-channel file.
- **Pitch bend**: −50¢ at range 2 → 6144; RPN sequence in order; **CC0 and CC32 precede every
  Program Change**; all channel-wide CCs and channel pressure duplicated; bend re-emitted after
  `CC121` and after a GM-reset SysEx, and **not** after `CC123`; source bend summed and clamped in
  Microtonal mode, untouched in 12-TET.
- Round-trip: load → restyle → export → reload; note count, timing, tempo map and bend events survive.

*Key detection*
- Empty and drums-only input → `NoKeyDetected`, not a default.
- Whole-tone input → reported ambiguous, with a deterministic top 3.
- C major scale with equal durations → C major first, A minor second, margin ≈ 0.044.
- A pentatonic source file asserts **exact expected pitches** both with its source scale set and with
  it left at the detected major — the two sets must differ, and both are asserted, not merely
  described as "correct" and "incorrect".

*Performance*
- A 20,000-note file restyles in under 16 ms as a **warm, best-of-5 benchmark with a 50 ms ceiling**,
  or as a BenchmarkDotNet job outside `dotnet test`. A cold single-shot assertion on a shared CI
  runner would be flaky and prove nothing.

*Build hygiene*
- A `using Melanchall.DryWetMidi.Multimedia;` in Core fails the build.
- A reference to `Assembly.Location` fails the build.
- The publish folder contains exactly one file.

**Manual** — run the published exe:

1. Open a multi-track file with drums; confirm metadata, track list, and that channel 10 is locked
   with an explanatory tooltip. Repeat with Format 0 (per-channel split), Format 2 (sequence count)
   and an SMPTE file (frame rate, no PPQN).
2. Confirm the suggested key is sensible and its ambiguity honestly reported; override it.
3. Restyle to Chinese Gong — notes collapse onto five pitch classes, contour recognisable, badge Exact.
4. Restyle to Maqam Rast — badge Approximate; the neutral 3rd is audibly *between* minor and major.
5. Restyle to Slendro on a 2-track file, then switch to 12-TET. **Verify mechanically, not only by
   ear**: export both and assert the note numbers and bend values differ as expected. The audible
   check is confirmation, not proof.
6. Restyle a 5-track file to Slendro; confirm the status bar reports the raised tolerance and the
   resulting accuracy, and that **every track is in the same tuning**.
7. Switch degree-mapping ↔ nearest-snap; confirm audibly different results and that the source key
   and source scale controls both dim under Nearest.
8. A/B playback: toggle repeatedly mid-playback; confirm no stuck notes, no stale bend, no restart.
9. Arrow-key down the scale list during playback; confirm it stays responsive and does not thrash.
10. Export a microtonal file; reopen it in the app and in a DAW; confirm the tuning matches preview.
11. Copy the single exe alone to a machine with no .NET installed and repeat steps 1–10.

---

## Revision log

**2026-08-26 (first pass) — self-review.** Sixteen findings applied: channel exhaustion recognised as
the common case; MusicXML's quantiser dependency; the mapper's truncating division; banker's
rounding; the speller's nearest-step rule; Format 0's per-channel drum exclusion; plus selectable
source scale, tolerance-aware `Auto`, bend re-emission, required provenance, and several factual
corrections.

**2026-08-26 (second pass) — independent fresh-context review.** Full findings in
`.claude/review/REVIEW-2026-08-26-independent.md`. Seven blocking, twenty-three significant, eleven
minor. The most consequential:

1. **`FF 21` is not a MIDI standard** and is honoured by almost nothing — so the first pass's
   resolution of channel exhaustion was built on a false premise. **Replaced entirely** with adaptive
   uniform tolerance.
2. **Partial per-track degradation produces bitonality**, clashing 20–40¢ on the same scale degree.
   Removed; the project now always shares one tuning.
3. **No MIDI range clamp** — 7→5 mapping expands range 1.4×, so a full-range file into Slendro
   overflows and `(SevenBitNumber)130` throws at export. Added `RangePolicy`.
4. **The clustering algorithm was unspecified**, and span-bounded vs single-linkage differ by up to
   2× on real scales. Now specified as greedy span-bounded.
5. **`|Alter| ≤ 1.5` rejected 22 of the 72 melakartas**, which legitimately need double flats.
   Raised to 2.
6. **The melakarta loop order was inside-out**, which would have misaligned all 72 canonical names.
   Now stated explicitly with four asserted spot-checks.
7. **`Alter` is not the MusicXML `<alter>` value** — two frames were conflated, and no tonic *letter*
   was recorded. Added `TonicSpelling` and an explicit conversion. (The review's stronger claim, that
   the stored spelling is wrong on non-C tonics, was itself miscomputed and is rejected — Hijaz on D
   recomputes correctly.)

Also applied: CC123 removed from the bend-reset triggers (it does not reset bend); Slendro, Pelog,
Thai, Hijaz and the Japanese variants reworked as cited tunings with labelled idealizations; the
1200¢-period assumption made explicit and validated at import; the four `.scl` rules that break
parsers; `ScaleValidation` in the constructor; allocation keyed on `(track, channel, cluster)`;
the preview-vs-export contradiction resolved; the non-heptatonic speller allowing repeated steps;
`Alter` quantised with a stored residual; `KeyEstimate` with a defined confidence and `NoKeyDetected`;
phase 8's gate made headless; the error contract made deliverable; the publish command corrected
(the single-file claim was false) with a build gate; `FluentAssertions` replaced over its commercial
licence; bank select and full CC duplication; `App.Tests` and `Playback.Tests`; the microtonal share
corrected from 45% to ~31%; the quantiser cascade and octave guard; persistence precedence and path
probing; the scale editor specified; A/B playback specified; a status bar added and target tonic
moved out of the disclosure; `BannedApiAnalyzers` enforcing the Core boundary; phase 5 split into
code and data; and the performance test made a warm benchmark.
