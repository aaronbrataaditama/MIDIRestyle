# MIDIRestyle

MIDIRestyle is a Windows desktop app that loads a MIDI file and re-maps its musical scale into a
different one — Western diatonic into Chinese Gong pentatonic, Maqam Rast, Gamelan Slendro, and
around 170 other scales from world musical traditions. It also shows a piano roll, track list and
file metadata, and lets you A/B the original against the restyled version.

Restyling is **pitch remapping only** — it never touches rhythm, ornamentation or articulation.

## Getting it

MIDIRestyle ships as a single self-contained `.exe`. There is no installer: copy the file anywhere
(including a USB stick) and run it. Settings and its scale library live in a `scales/` folder
written beside the exe on first run (falling back to `%APPDATA%` if that location isn't writable,
e.g. Program Files).

## Building from source

Requires the .NET 10 SDK (pinned in `global.json`).

```powershell
dotnet build                                    # whole solution
dotnet test                                     # all tests - do NOT add --nologo, see below
dotnet run --project src/MidiRestyle.App        # launch the app
```

A single test or class:

```powershell
dotnet test --filter "FullyQualifiedName~ChannelAllocatorTests"
dotnet test --filter "FullyQualifiedName~ChannelAllocatorTests.RastAllocatesTwoChannels"
```

**Never pass `--nologo` to `dotnet test`.** The .NET 10 SDK forwards unrecognised arguments to the
underlying test application, which rejects the flag and reports "Zero tests ran" even when every
test passed. If `dotnet test` ever reports zero, read the per-assembly `Standard output:` block —
that's where the real error is.

## Building the portable release

```powershell
dotnet publish src/MidiRestyle.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

The publish folder must end up holding **exactly one file** — that's the whole portability
promise. This is enforced automatically: a gate built into `MidiRestyle.App.csproj` runs after
every publish and fails the build loudly if a second file shows up (a stray native library, a
debug symbol, anything). It caught a real bug during development — see the csproj comments for
the mechanism, and `CLAUDE.md` for the full history.

## Project status

v1 is complete, with three releases on top of it. All twelve build phases are done — the
domain model, the 171-scale library, key detection, the restyle engine, channel allocation for
microtonal export, audio playback with an A/B switch, and the portable single-file publish — as
are the three features v1 deferred: MusicXML export, the staff view and the degree view.

Since then: **v1.2** rebuilt the notation as a wrapped page of systems with a following playhead
and turned the degree view into a scale wheel; **v1.3** added click-to-seek on the staff and
real clef and rest glyph outlines; **v1.4** added the About window.

Last verified green: 1314 tests, 0 warnings, and a portable publish of exactly one 50.2 MB file.

## Where the real documentation lives

This README is deliberately short. For anything deeper:

- [`.claude/plan/PLAN-midi-restyle.md`](.claude/plan/PLAN-midi-restyle.md) — the authoritative spec
- [`.claude/STRUCTURE.md`](.claude/STRUCTURE.md) — map of the repository layout
- [`.claude/PROGRESS.md`](.claude/PROGRESS.md) — phase-by-phase implementation state and handover
  notes
- [`CLAUDE.md`](CLAUDE.md) — architecture and the load-bearing invariants behind the design

## Licence

MIDIRestyle is released under the [MIT License](LICENSE).

Note that the shipped `.exe` is self-contained and so redistributes its dependencies (Avalonia,
DryWetMIDI, SkiaSharp, HarfBuzzSharp, CommunityToolkit.Mvvm and the Inter font). Those carry their
own licences — MIT in most cases, the SIL Open Font License for Inter — and this repository does
not yet bundle their notices.
