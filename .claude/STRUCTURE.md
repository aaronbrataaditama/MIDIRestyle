# Project structure

Generated map of the MIDIRestyle repository. **Update this file whenever a project, folder or
significant file is added** — it is the fast orientation document for a new session, and a stale
structure map is worse than none.

- Authoritative spec: [`plan/PLAN-midi-restyle.md`](plan/PLAN-midi-restyle.md)
- Load-bearing invariants: [`../CLAUDE.md`](../CLAUDE.md) — read before changing anything here
- Progress and handover: [`PROGRESS.md`](PROGRESS.md)

**State: v1 complete, plus the v1.1 notation work.** All twelve planned phases are built, and so are
the three features the plan deferred — MusicXML export, the staff view and the degree/cipher view —
together with the rhythm quantiser and beam grouper they share. Where the plan says a feature is
deferred to v1.1, that is now history.

## Top level

```
MIDIRestyle/
├─ MIDIRestyle.slnx              # .NET 10 XML solution format (NOT .sln — see note below)
├─ Directory.Build.props         # shared TFM/nullable/analyzer settings for every project
├─ Directory.Packages.props      # central package management — ALL versions pinned here
├─ global.json                   # SDK pin + Microsoft.Testing.Platform opt-in for `dotnet test`
├─ README.md
├─ .gitignore / .gitattributes
├─ CLAUDE.md                     # agent guidance + load-bearing invariants
├─ .claude/
│  ├─ STRUCTURE.md               # this file
│  ├─ PROGRESS.md                # phase-by-phase state + handover
│  ├─ plan/PLAN-midi-restyle.md  # the authoritative spec
│  └─ review/
│     ├─ REVIEW-2026-08-26-independent.md   # pre-implementation review, folded into the plan
│     └─ REVIEW-2026-08-28-notation.md      # review of the notation layer; findings fixed
├─ src/
│  ├─ MidiRestyle.Core/          # pure domain — no UI, no Multimedia, headlessly testable
│  ├─ MidiRestyle.Playback/      # the ONLY platform-bound assembly
│  └─ MidiRestyle.App/           # Avalonia UI (MVVM)
└─ tests/
   ├─ MidiRestyle.Core.Tests/    # 35 files
   ├─ MidiRestyle.Playback.Tests/# 8 files
   └─ MidiRestyle.App.Tests/     # 22 files
```

**Why `.slnx`, not `.sln`.** The .NET 10 SDK emits the XML solution format by default. It works with
`dotnet build`/`dotnet test` and with VS 2022 17.14+ and current Rider. If you need the classic
format for older tooling, `dotnet sln migrate` converts it.

## `src/MidiRestyle.Core` — the domain

Pure, deterministic, no UI dependency. Referencing DryWetMIDI is fine here; touching
`Melanchall.DryWetMidi.Multimedia` is a **build error** (see [Enforcement](#enforcement)).

```
Tuning/      MidiRounding.cs        THE rounding gate - every cents/semitone conversion,
                                    always MidpointRounding.AwayFromZero
             Pitch.cs               cents-based pitch; MidiNote + BendCents + PitchClass

Scales/      Scale.cs               the scale record, validated in its constructor
             ScaleValidationException.cs  carries the downstream failure it prevents
             DegreeSpelling.cs      (DiatonicStep, Alter, ResidualCents) - Alter is relative to
                                    the major-scale degree, NOT the MusicXML <alter>
             TonicSpelling.cs       the tonic's letter + alteration; MIDI 61 may be C# or Db
             DiatonicSpeller.cs     derives a staff spelling, or says why none exists
             MelakartaGenerator.cs  the 72 Carnatic melakarta, generated not authored
             ScalaFileReader.cs     .scl import (bare integer = ratio, unlike the editor)
             ScaleJsonStore.cs      JSON load/save
             ScaleLibrary.cs        merged library, precedence, Id collision reporting
             TuningFidelity.cs      computed from deviation, never hand-tagged
             TwelveTetQuantiser.cs  PITCH quantiser (cents -> semitone), not the rhythm one

Model/       Note.cs, MidiProject.cs, TrackInfo.cs, TempoChange.cs, TimeDivision.cs,
             MidiFileFormatKind.cs, RestyleSettings.cs, RestyleResult.cs

Analysis/    KeyDetector.cs         Krumhansl-Schmuckler; a suggestion, never a silent decision
             PitchClassProfile.cs   duration-weighted, drums excluded
             KeyEstimate.cs         carries Margin, not raw r

Mapping/     IPitchMapper.cs, ScaleDegreeMapper.cs (floor division + positive modulo),
             NearestPitchMapper.cs, CollisionResolver.cs, MappingOptions.cs

Restyle/     RestyleEngine.cs       pure function of (project, settings); under 16 ms / 20k notes

Output/      OffsetClusterer.cs     greedy SPAN-BOUNDED, not single-linkage
             ChannelBudget.cs       adaptive tolerance, uniform across the project
             ChannelAllocator.cs    the single path shared by playback and export
             PitchBendEncoder.cs    SetupSequence / RetuneSequence
             OutputMode.cs

Io/          MidiFileLoader.cs      incl. Format 0 per-channel splitting and SMPTE
             MidiFileExporter.cs, MidiFileLoadException.cs, MidiFileExportException.cs
             PlaybackSequenceBuilder.cs, GeneralMidi.cs

Notation/    -- the v1.1 layer. One NotationScore feeds staff, degrees and MusicXML alike. --
             NoteValue.cs           written values; MusicXmlType(), FlagCount(), IsHollow()
             Tuplet.cs              actual:normal ratio; None is 1:1, never null
             NotatedDuration.cs     value + dots + tuplet
             DurationDecomposer.cs  tick span -> tied written durations; reports WrittenTicks
             QuantiseOptions.cs     resolution, TupletBias, MinimumTupletOnsets
             RhythmQuantiser.cs     per-beat straight/triplet/sextuplet decision; BeatRuler
             MeasureGrid.cs         time-signature map -> barlines; the ONE source of them
             StaffLayout.cs         grand staff for keyboards spanning middle C; clef choice
             NoteSpeller.cs         SpelledNote; the Alter -> <alter> frame conversion
             DegreeReading.cs       cipher/jianpu degree + octave marks, for non-notatable scales
             BeamGrouper.cs         beam groups and levels, incl. hooks; never crosses a beat
             NotationModel.cs       NotationScore/Part/Measure/Entry, BeamState, TieState, Clef
             NotationBuilder.cs     the orchestrator: quantise, split, pack voices, infer rests,
                                    spell, beam. Everything branches AFTER this.
             MusicXmlExporter.cs    MusicXML 4.0 partwise
             MusicXmlExportException.cs
```

## `src/MidiRestyle.Playback` — the only platform-bound assembly

DryWetMIDI's device API is Windows/macOS only, **not Linux**. `IPlaybackEngine` exists so that fact
never leaks upward.

```
IPlaybackEngine.cs            the seam
DryWetMidiPlaybackEngine.cs   the real one; all four Track* flags on, and that is load-bearing
NullPlaybackEngine.cs         fallback when there is no device — a normal state, not an error
AbSwitcher.cs                 original vs restyled, under 30 ms
```

## `src/MidiRestyle.App` — Avalonia UI (MVVM)

```
Program.cs, App.axaml(.cs)

Controls/    PianoRoll.cs + PianoRollGeometry.cs    custom Render, culled, no per-frame allocation
             StaffView.cs + StaffGeometry.cs        staff notation as a wrapped page of systems;
                                                    click any bar to seek
             StaffGlyphs.cs                         clef and quarter-rest outlines, verbatim from
                                                    public-domain SVGs; filled, no music font
             DegreeView.cs + DegreeGeometry.cs      scale wheel: degrees placed at their true cents
                                                    against 12-TET ticks, for any scale

ViewModels/  MainWindowViewModel.cs   project, restyle, notation score, transport, view tabs
             StylePanelViewModel.cs   scale list, source/target key, policies
             TrackViewModel.cs, MetadataViewModel.cs, StatusBarViewModel.cs,
             ScaleEditorViewModel.cs

Views/       MainWindow.axaml(.cs)    menu, toolbar, three panes, view-tab strip, status bar
             ScaleEditorWindow.axaml(.cs)
             Icons.axaml

Services/    ScaleLibraryService.cs   reads embedded assets — needs an initialised Avalonia runtime
             SettingsService.cs, AppSettings.cs, PathProbe.cs, ThemeService.cs

Assets/      app-icon.ico             9 sizes, 16-256; 16/20/24 deliberately omit the grid lines
             scales/*.json            9 regional files, 99 authored scales (+72 generated melakarta)
```

The three controls follow one rule worth repeating: a custom `Control` overriding
`Render(DrawingContext)`, culled to the visible range, **allocating nothing per frame**. A dense
file is tens of thousands of notes.

## Tests

`dotnet test` — **do NOT add `--nologo`**; the SDK forwards it to the test platform, which rejects it
and reports "Zero tests ran" while everything passes.

Notable fixtures:

- `tests/MidiRestyle.App.Tests/ChordStemRenderTests.cs` — counts tall ink columns on a rendered page
  to pin one-stem-per-chord, comparing against a single-note render so the clef, barlines and part
  name cancel. Avalonia exposes no recording `DrawingContext`, so pixels are the only way in.
- `tests/MidiRestyle.App.Tests/AvaloniaRenderFixture.cs` — an **assembly** fixture owning the single
  thread allowed to touch Avalonia. `Dispatcher.UIThread` binds to whichever thread reaches Avalonia
  first and stays bound, and with xunit running classes in parallel that used to be a worker thread
  belonging to another class. Do not initialise Avalonia anywhere else in that project.
- `NotationEndToEndTests` — writes a real `.mid`, loads, restyles, notates, exports, and parses the
  XML back. Runs over clean *and* jittered timing.
- The fuzz tests in `NotationBuilderTests` are seeded, so a failure reproduces. They generate
  overlapping notes dense enough to need 5–9 voices; if you tune the generator, check it still
  exceeds four, or it stops guarding the bug it exists for.

## Enforcement

Two rules are enforced by the build, not by discipline, and both are verified by deliberately
violating them:

- `src/MidiRestyle.Core/BannedSymbols.txt` — `Melanchall.DryWetMidi.Multimedia` in Core is a build
  error, as is `System.Reflection.Assembly.Location` (returns `""` under `PublishSingleFile`).
- `AssertSingleFilePublish` runs `AfterTargets="Publish"` and fails the build if the publish folder
  holds anything but exactly one file.

`TreatWarningsAsErrors` is on across the solution.
