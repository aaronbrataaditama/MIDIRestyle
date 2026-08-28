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

Phases 1–9 of the build are complete: the domain model, ~171-scale library, key detection, the
restyle engine, channel allocation for microtonal export, and the Avalonia UI (piano roll, track
list, scale picker with live restyling) all work end to end. Audio playback (phase 10) is in
progress. MusicXML export and a notated staff view are deferred to a v1.1 release.

## Where the real documentation lives

This README is deliberately short. For anything deeper:

- [`.claude/plan/PLAN-midi-restyle.md`](.claude/plan/PLAN-midi-restyle.md) — the authoritative spec
- [`.claude/STRUCTURE.md`](.claude/STRUCTURE.md) — map of the repository layout
- [`.claude/PROGRESS.md`](.claude/PROGRESS.md) — phase-by-phase implementation state and handover
  notes
- [`CLAUDE.md`](CLAUDE.md) — architecture and the load-bearing invariants behind the design
