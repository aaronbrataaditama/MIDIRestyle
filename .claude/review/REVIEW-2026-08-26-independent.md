# Independent plan review — 2026-08-26

Fresh-context adversarial review of `.claude/plan/PLAN-midi-restyle.md` (587 lines) and
`CLAUDE.md`, conducted blind to the authoring session's reasoning. The reviewer was instructed to
verify rather than trust, and to skip the sixteen findings already listed in the plan's revision log.

**Verdict: not sound enough to start implementing.** Arithmetic and .NET semantics mostly stood up;
the failures are concentrated in externally-verifiable claims that the authoring session asserted
without checking.

Status legend: **OPEN** = accepted, not yet applied · **APPLIED** · **TEMPERED** = partly wrong,
see note.

---

## Blocking

### B1 — `FF 21` is not a MIDI standard and is honoured by almost nothing · OPEN
Plan lines 30, 366–367, 488, 527–529; `CLAUDE.md` 116–118.

The plan's resolution of its own #1 blocking issue rests on the MIDI Port meta event with the claim
"most DAWs honour it". **That claim was never verified and is false.**

Evidence:
- `FF 21` appears nowhere in *The Complete MIDI 1.0 Detailed Specification* 96.1 3rd ed. Its SMF
  meta-event list is `FF 00, 01–07, 20, 2F, 51, 54, 58, 59, 7F`. The addenda appendix (current to
  Dec 2013) enumerates every post-1996 SMF addition — no port event, ever.
- It is a Cakewalk invention. Glatt's MIDI reference: "The MMA would like you to know that they
  never endorsed their use… Use the Device (Port) Name Meta-Event instead."
- Actual support: Cakewalk/SONAR (opt-in, off by default); MuseScore (reads/writes, but MuseScore 4
  emits `FF 21 01 00` on every track — a no-op); REAPER preserves it as data but does not route on
  it; Ableton ignores ports entirely; no evidence either way for Cubase, Logic, Pro Tools, Studio
  One, FL Studio, Sibelius.
- The MMA's sanctioned mechanism is RP-019 `FF 09` Device Name, which the plan never mentions.
- `FF 21` does not widen channel numbering — status bytes stay 4-bit.
- Failure mode when ignored is not graceful: `(port, channel)` collapses to `channel`, so Program
  Changes fight and pitch bend, RPN state and CC7/10/11/64 merge and stomp each other. The
  microtonal output — the product's entire purpose — silently becomes wrong.

Consequence: **the channel-budget problem is unresolved.** The plan believes it is fixed.

### B2 — No MIDI range clamp; degree mapping expands range by `n_target / n_source` · OPEN
Plan lines 161–166, 286–296.

`Pitch` is unbounded and the mapper has no clamp. Expansion for 7→5 is exactly 1.4×.
- C major → Gong: MIDI 108 → **exactly 127**, no headroom.
- Full piano range (21–108) → Slendro: **4.80 .. 127.20** — overflows.
- Source 0–127 → **−24 .. 153**.
- Verified on .NET 10: `(SevenBitNumber)130` throws `ArgumentOutOfRangeException`. Export throws,
  it does not degrade.
- Inverse case: a 31-degree `.scl` target compresses MIDI 21–108 into **51–71** — seven octaves
  squashed into 1.6.

Golden tests cover a note a fourth below the tonic but nothing near the range limits.

### B3 — The clustering algorithm is unspecified, and the plan's asserted count depends on it · OPEN
Plan lines 330–335, 526; `CLAUDE.md` 112–114.

Pythagorean Gong offsets are `0, 1.955, 3.910, 5.865, 7.820` — **every adjacent gap is 1.955¢**.

| algorithm | @5¢ | max error |
|---|---|---|
| single-linkage (chain adjacent within tolerance) | **1 cluster** | 3.91¢ |
| greedy span-bounded (cluster width ≤ tolerance) | **2 clusters** | 1.96¢ |

Only greedy-span yields the plan's asserted 2. Single-linkage — the more natural reading of
"cluster within a tolerance" — yields 1 and fails the headline test. It diverges across the library:
Turkish AEU Rast 2 vs 3, Uşşak 2 vs 4. With ~20 makams shipping, a factor-of-two swing in channel
demand rides on an unstated choice.

### B4 — The heptatonic spelling rule rejects 22 of the 72 melakartas · OPEN
Plan lines 236–237, 537.

The rule rejects `|Alter| > 1.5`. Generating all 72 and applying it, **22 produce `|Alter| = 2.0`**
and return `null`: melas 1–7, 13, 19, 25, 31, 37–43, 49, 55, 61, 67. Cause: G1 = 200¢ at diatonic
index 2 → −2; N1 = 900¢ at index 6 → −2.

Mela #1 Kanakangi `[0,100,200,500,700,800,900]` → alters `[0,−1,−2,0,0,−1,−2]` = `C D♭ E𝄫 F G A♭ B𝄫`,
which is the standard Western rendering. Max `|Alter|` over all 72 is exactly 2.0. Double flats are
legitimate notation and legal MusicXML, so the **threshold** is wrong, not the spelling. The 10
thaats all pass (max 1.0), which is why this survived.

Verification claim "all 72 melakarta spell without collision" is false as specified.

### B5 — The melakarta generator's stated iteration order misaligns the name array · OPEN
Plan lines 225–227, 514.

Combinatorics are right (6 × 6 × 2 = 72, all distinct — verified). **The nesting is backwards.**
Canonical order has Ma outermost: melas 1–36 use Ma1 (500¢), 37–72 use Ma2 (600¢); Ri-Ga is the
chakra digit; Dha-Ni varies fastest. Verified against all 12 chakra names.
- Ma-outermost → index 15 = `[0,100,400,500,700,800,1100]` ✓ Mayamalavagowla.
- The plan's literal order → index 15 = `[0,100,300,500,700,800,1000]` ✗ — a different mela, and
  every name in the array is then wrong.

### B6 — `Alter` is not the MusicXML `<alter>` value, and no tonic *letter* is recorded · TEMPERED
Plan lines 192–194, 236, 250–256.

The reviewer's stated conclusion — "spelling is wrong on any tonic but C" — **is incorrect**, and
its Hijaz-on-D worked example is miscomputed: it applied the stored alters to *natural* letters
instead of to the major-scale degrees the formula is defined against. Recomputed correctly, Hijaz on
D gives `D E♭ F♯ G A B♭ C` = `0,100,400,500,700,800,1000` — right. The stored value is
tonic-independent and sound.

The underlying issue is real, though:
- `Alter` is *relative to the major-scale degree*. MusicXML's `<alter>` is an **absolute alteration
  of the natural letter**. On D, step 2 has `Alter = 0` but MusicXML needs `<alter>1</alter>` for F♯.
  So the plan's claim that this field "is exactly the type MusicXML's `<alter>` element takes" is
  true of the type and false of the value. A conversion step is required and unspecified.
- Nothing in `Scale` or `RestyleSettings` records the tonic's **letter**. MIDI 61 may be C♯ or D♭,
  and the spelling depends on which.

### B7 — Partial degradation to 12-TET makes tracks fight 20–40¢ apart · OPEN
Plan lines 368–374, 528, 552.

The arithmetic is right (4 tracks × 5 offsets = 20 > 15; degrading 2 gives 12 ≤ 15). The *result* is
not degradation, it is bitonality:

| Slendro degree | microtonal | degraded | clash |
|---|---|---|---|
| 1 | 240¢ | 200¢ | **40¢** |
| 2 | 480¢ | 500¢ | 20¢ |
| 3 | 720¢ | 700¢ | 20¢ |
| 4 | 960¢ | 1000¢ | **40¢** |

A degraded bass and a microtonal melody on the same scale degree beat 40 cents apart — markedly
worse than either uniform choice. The plan presents this as the graceful path and makes it a manual
acceptance test with no mention that the output is dissonant.

---

## Significant

| # | Finding | Lines |
|---|---|---|
| S1 | **CC123 does not reset pitch bend** — only CC121 and GM-reset SysEx do. Spec: CC123 "turns off all notes… " and nothing more. The plan also contradicts itself, since line 430 sends CC123 *plus* an explicit bend reset precisely because CC123 doesn't touch it. | 340–342 vs 430–432 |
| S2 | **Slendro is not 5-equal**, and real gamelan **stretch the octave to 1203–1212¢**. Measured sets: Kyahi Kanyut Mèsem `0,223,475.6,711.8,937.1`; Udan Mas `0,266.9,510.4,745.8,996.1`. Surjodiningrat et al. (1972) measured 28 gamelan: steps run 206–268.5¢. Stretched octaves are **not representable** in the "offsets belong to the scale, octaves are exactly 1200¢" model. Manual test 5 demonstrates 5-EDO, not any documented gamelan. | 74–75, 197–198, 550 |
| S3 | **Hijaz ships as 12-TET Phrygian dominant** (all offsets zero, one channel) — the caricature the plan exists to avoid. maqamworld: the 2nd is raised and 3rd lowered from notated; common range 125–135 / 375–385, Scala's 11-limit gives 150.6. Also: Rast's descending 7th is 1000, not 1050, and the neutral 3rd clusters 347–370 in practice — so the headline "Rast → offsets `{0,−50}`" test is hard-coded to an idealization. | 254 |
| S4 | **`.scl` parser spec is missing the four rules that break parsers.** (1) 1/1 is implicit and the declared count *includes* the trailing 2/1 — so the parser must prepend 0 and strip the octave, or every import is silently wrong. (2) A bare `700` is the **ratio 700/1 ≈ 11,304¢**, not 700 cents; `408.` is cents; negative ratios must error; trailing text is ignored. (3) The last entry need not be 2/1 — `bohlen-p.scl` ends `3/1`, violating the 1200¢ invariant. (4) **Cardinality is unbounded** — a 31-EDO import needs 31 channels and produces non-monotonic quantiser output. | 25, 63, 485, 516 |
| S5 | Allocation is keyed on source **channel** while budget and opt-out are per **track**. Two Format 1 tracks may share channel 0 with different programs, collapsing into one key; one track may use several channels, so "4 tracks × 5 offsets" assumes one channel per track. | 336 vs 441, 52, 369 |
| S6 | **Direct self-contradiction**: line 108 says degradation "must apply to both" paths; lines 372–374 say the two "can legitimately differ". Decides whether `ChannelBudget` is a pure function or target-parameterised. | 108 vs 368–374 |
| S7 | The **non-heptatonic** spelling rule cannot spell the blues scale: `[0,300,500,600,700,1000]` → 600 ties to G, 700 is G → collision → `null`, for a scale everyone spells `C E♭ F G♭ G B♭`. Same bug class the prior review fixed in the *other* branch. Structurally, **8+ degree scales cannot be spelled at all** by a 7-step rule — and several dastgāhs and makams are 8–9 notes. | 222, 238 |
| S8 | Derived `Alter` is **uninterpretable for ~32 Turkish and Persian comma scales**. AEU Rast gives `[0,+0.038,−0.151,−0.019,+0.019,+0.057,−0.132]`; no renderer draws `<alter>-0.151`. These are `Notatable = true`, so noise reaches the exporter verbatim. | 236, 258–259 |
| S9 | **Key detection**: the two K-S profile arrays are correct digit-for-digit, but the algorithm around them is underspecified. Empty/drums-only input → Pearson denominator 0 → `r = NaN` for all 24 and arbitrary sort order. Whole-tone input → 4+ candidates tie at exactly 0.0680. Plain C major, equal durations → C 0.7564 vs A minor 0.7121, gap 0.044. So reporting `r` says "76%" on ambiguity and reporting the gap says "4%" on certainty — "confidence" is undefined and either number misinforms. Detected tonic also *defaults the target tonic*, so a relative-minor miss shifts output by a third. | 261–270, 519 |
| S10 | **Phase 8's gate depends on Phase 10.** "degrades on playback" needs `IPlaybackEngine`, built two phases later. | 488 |
| S11 | **DryWetMIDI cannot report a byte offset**, so line 444's error contract is undeliverable. Every public exception type was enumerated; none carries stream position. A truncated file yields `NotEnoughBytesException` with `ExpectedCount = 0, ActualCount = 0`. Also: **Format 2 does not fail** — it reads fine with default settings; and `SmpteTimeDivision` files have no PPQN, which the metadata header assumes. | 444 |
| S12 | **The publish command does not produce a single file.** Ran it: output contains `pubtest.exe` (37.9 MB) **plus a loose `Melanchall_DryWetMidi_Native64.dylib`**, because DryWetMIDI ships natives as plain `None`/`CopyToOutputDirectory` items, not RID-graph native assets. Without the flag, all three natives sit loose beside the exe **and it still runs** — so the claimed `DllNotFoundException`-only-on-a-clean-machine rationale is wrong. Breaks manual test 10 ("copy the single exe alone"). Note `IncludeAllContentForSelfExtract` would bundle the dylib but changes `AppContext.BaseDirectory` to the extraction dir, breaking settings-beside-exe — the two goals are in tension. | 451–460 |
| S13 | **`FluentAssertions` "current" needs a paid licence.** v8.0 (Jan 2025) replaced Apache-2.0 with a proprietary Xceed licence: **$130/developer/year** commercial. 7.x remains Apache-2.0. | 152 |
| S14 | **Source pitch bend, bank select and most CCs unhandled.** Derived channels get Program Change but **not CC0/CC32 bank select** — on any GS/XG device that selects a *different instrument*. Source bends have no specified interaction with the tuning bend. CC1, CC5/65, CC91, CC93 and aftertouch are silently lost. | 76–77, 349–351 |
| S15 | **No UI test project**, yet a dozen invariants live there (channel-10 lock, source-key dimming, fidelity severity, degradation message, export-disabled reason). MVVM was chosen to make these testable, then nothing tests them. | 136–137 |
| S16 | **"45% of the library cannot be expressed in 12-TET" is arithmetically impossible.** Census of the plan's own region table: 166 scales, ~52 microtonal = **31.3%**. 110 are 12-TET by construction (South Asia 82, Europe 12, East Asia 12, Americas 4), so the hard ceiling is 33.7%. The omitted useful figure: **94 hand-authored scales** each needing a citation. | 38 |
| S17 | **The 12-TET quantiser has no cascade and no octave guard.** `[…,1160]` → a degree at **1200**, duplicating the tonic and producing two identical pitches per octave. Three degrees inside 100¢ need a chained push; the rule pushes once. | 208–210, 507 |
| S18 | **Settings/scale persistence is in no phase** and its precedence is undefined: how "read-only" is detected (only try-write-and-catch is reliable); split-brain when settings exist both beside the exe and in `%APPDATA%`; scale-source precedence across embedded / beside-exe / `%APPDATA%`; and Id collisions across six sources with no uniqueness test. | 462–465, 484–492 |
| S19 | **The custom scale editor is one line**, and with no validation contract the mapper crashes: 0 degrees → `d % n` → `DivideByZeroException`; non-ascending, duplicate, negative or ≥1200 degrees silently misbehave. Also unresolved: what fills the non-nullable `Source` for a user scale. | 490 |
| S20 | **A/B playback — the "core value delivered" milestone — is unspecified.** DryWetMIDI's `Playback` takes a fixed event collection, so it's one of three designs with different flaws: two instances (clock drift, one device), one rebuilt and re-seeked (audible gap), or one merged sequence with muted groups (**doubles the channel budget**, already the binding constraint). Plus: arrow-key scale browsing during playback means rebuild+re-seek at keystroke rate; the 16 ms budget covers only the transform. | 421–422, 490 |
| S21 | Two UI blockers: **no status bar exists** in the layout, yet the degradation report must appear in one; and **target tonic is in two mutually exclusive places** (inside the closed disclosure at 387–389, and as a peer of it at 418–420) — and it isn't set-once, since it defaults per file. | 370, 552 vs 411–422 |
| S22 | **The Core→Multimedia boundary is unenforceable.** `Melanchall.DryWetMidi.Multimedia` is not a separate package — **180 types in that namespace live inside the same DLL Core references**. Nothing prevents a `using`, no compiler error results, no test exists. | 116, 141–143 |
| S23 | **Phase 5 conflates 94 cited scale definitions with four code components.** ~20 Turkish makams in AEU commas, 7 dastgāhs + 5 āvāz with contested cents, Ethiopian and African tunings — musicology, not coding, and the item most likely to blow the phase. The provenance test ("`Source` non-empty") is **trivially satisfied by `Source = "TODO"`** and unfalsifiable with respect to what matters. | 200–202, 485, 517 |

---

## Minor

- **M1** — The stated reason for deferring MusicXML is wrong: `<type>` is `minOccurs="0"`, i.e.
  optional, and exact durations need no quantiser (set `<divisions>` = PPQN). The *conclusion* holds
  — measure splitting, rest inference and voice assignment are the hard parts. Fix the justification.
- **M2** — The pitch-bend formula's `round` is the one rounding in the plan not pinned to
  `AwayFromZero`. Reachable ties are vanishingly rare (~0.0122¢) but the two modes do differ there.
- **M3** — **The library contains pitch-duplicate entries.** Hon Kumoi Joshi `0,100,500,700,800` is
  identical to In; Han Kumoi `0,200,300,700,800` is identical to one published Hirajōshi; the
  Sachs/Slonimsky Hirajōshi `0,100,500,600,1000` is identical to Iwato. Four of five Japanese
  entries collapse onto three sets. No duplicate or Id-uniqueness test exists.
- **M4** — `SmpteTimeDivision` unhandled; such files have no PPQN.
- **M5** — The 16 ms assertion will be flaky on CI. The transform is ~1 ms for 20k notes; make it a
  warm best-of-N with a generous ceiling, or move it to BenchmarkDotNet.
- **M6** — Several verification steps cannot fail: "restyles incorrectly if left at the detected
  major" is not assertable; "smoothly"; "sounds like Slendro" is billed as *the* proof of
  microtonality and proves nothing mechanically.
- **M7** — The 72 names are authored data with no test. Chakra 6 is **Rutu**; mela #56's canonical
  name is **Chamaram**, not the popular "Shanmukhapriya".
- **M8** — The plan's provenance discipline isn't applied to its own magic numbers: the K-S profiles
  (correct, but sourced from Krumhansl 1990 pp. 37, 81–96, not the 1982 paper), the 5/25¢ fidelity
  thresholds, and the 5¢ clustering tolerance are all uncited.
- **M9** — Turkish AEU scales deviate at most **18.9¢** from 12-TET, so ~20 makams each burn 2–4
  channels for ≤19¢ — while the plan calls 7.8¢ "inaudible, and a waste of a scarce resource".
  Worse, performed Turkish practice sits 30–60¢ off (performed Segâh ≈ 340–370 vs AEU's 384.9), so
  AEU itself cannot represent what makes makam sound like makam.
- **M10** — "Thai/Khmer 7-equal" conflates two traditions. 171.43¢ is Ellis (1885), whose own
  measurements were 32.5–63.3¢ off; Garzoli, *"The Myth of Equidistance in Thai Tuning"* (AAWM 4.2,
  2015) found deviations of −80 to +40¢ with deliberate octave stretching. Khmer tuning is
  documented as non-equidistant (Miller & Sam-Ang Sam, 1995).
- **M11** — "Rules out re-allocating the whole model per keystroke" contradicts the
  immutable-`RestyleResult` architecture, which allocates a fresh result on every change by design.

---

## Verified correct — do not re-litigate

**Arithmetic and theory.** Rast → exactly 2 offsets `{0,−50}`. The banker's-rounding argument is
precisely right and was reproduced on .NET 10: `350/100 → 4` under both modes, but `1050/100 → 10`
under the default and `11` under `AwayFromZero`. Slendro → 5 offsets; Thai → 7 offsets, max
deviation **42.86¢** (so the revised 43¢ is right and the old 29¢ was wrong). Pythagorean Gong max
deviation 7.82¢, 5 clusters at 1¢. Fidelity badges: Gong Exact, Slendro/Rast/Thai Approximate,
Turkish Close (worst 18.9¢). 12-TET Slendro = `0,200,500,700,1000`. Melakarta combinatorics: 72, all
distinct pitch-class sets, all containing Sa=0 and Pa=700; **Mayamalavagowla #15 verified two ways**.
Channel budget: 3 tracks × 5 = exactly 15; 4 tracks → minimum 2 degraded. Pitch bend −50¢ at ±2 =
**6144**; resolution 0.0244¢; offsets bounded to ±50¢ so no bend overflow is reachable. Japanese In
tie-resolution → `C D♭ F G A♭`; Gong → `C D E G A`; Rast → −0.5 on steps 2 and 6. **K-S profiles
correct digit-for-digit** against Humdrum `keycor` and music21. Region table totals 166, so "~165"
is accurate. MusicXML `<alter>` is `xs:decimal` with microtones explicitly supported.

**MIDI spec.** The RPN bend-sensitivity sequence is correct and complete. `FF 21 01 pp` is the
correct byte encoding (its *status* is the problem, not its form). CC121 does reset pitch bend.
Channel 9 is GM percussion, leaving 15. Format 0/1/2 semantics as stated.

**.NET and libraries.** `-1 / 5 == 0` and `-1 % 5 == -1` confirmed — the floor-division requirement
is real, and the plan's replacement expressions behave as claimed. `Math.Round(double,
MidpointRounding)` is a valid overload. `readonly record struct` gives value equality, and
degree-derived cents compare bit-identically. **Avalonia 12.1.1 is current stable and lists both
`net8.0` and `net10.0`.** .NET 10 is LTS; .NET 8 ends November 2026. **DryWetMIDI 8.0.3 is MIT** and
ships **no Linux native** — direct evidence for the no-Linux-devices claim. `PortPrefixEvent` and
`DeviceNameEvent` both exist. `AppContext.BaseDirectory` returns the exe's directory under
single-file, so settings-beside-exe works — but `Assembly.Location` returns `''` and emits IL3000.

## Could not verify

- Whether the Microsoft GS Wavetable Synth honours **RPN 0** (bend sensitivity) or is fixed at ±2.
  Harmless while offsets are bounded to ±50¢, but if bend range becomes configurable, preview and
  export could silently diverge. Bench-check on the target machine.
- Whether Avalonia's **`NativeMenuBar` renders a usable in-window menu on Windows**, or is
  macOS-only with a fallback. The conventional Windows choice is the `Menu` control. Verify before
  Phase 3 — it delivers the menu bar.
- Real-world DAW behaviour for `FF 21`. The specification finding is conclusive; the "Cubase/Logic/
  Pro Tools ignore it" part is absence of evidence, not tested behaviour. Test one exported file in
  the two DAWs that matter before committing to any multi-port design.
- Whether a DryWetMIDI **device call** actually fails without `IncludeNativeLibrariesForSelfExtract`
  (the publish test loaded managed types only). S12's finding about the loose `.dylib` holds either
  way.
- Albrecht & Shanahan (2013) key profiles as a K-S alternative — could not retrieve.
