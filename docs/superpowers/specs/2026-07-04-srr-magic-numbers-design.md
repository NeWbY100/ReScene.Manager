# Eliminate Magic Numbers — Phase 2: SRR namespace (Design)

**Date:** 2026-07-04
**Status:** Draft (pending review)
**Scope:** `ReScene.Lib/ReScene/SRR/*.cs`.
**Nature:** Behaviour-preserving refactor. Zero functional change; only numeric literals become named.
**Predecessor:** Phase 1 (RAR) — merged. This phase replays the same recipe (adopt existing enums →
consolidate duplicated consts into a layout class → name the rest → byte-exact verification) and
additionally **reuses the `Rar4HeaderLayout` built in Phase 1** where `SRRWriter` parses embedded RAR4
headers.

## Background & goal

The SRR namespace has ~76 hex literals plus decimal offsets/sizes across `SRRWriter.cs`,
`SRRFileParser.cs`, `SRRBlock.cs`, `SRRFile.cs`, `SRRVerifier.cs`, `SrrBlockWriter.cs`, `SRREditor.cs`.
As in RAR, the domain constants largely already exist as enums; the code bypasses them with raw
literals, and several classes redeclare the same private consts. Goal: name the SRR domain literals so
the code reads in domain terms and a wrong constant is caught at one definition site.

### Sequencing

Implemented on `refactor/magic-numbers-srr` (branched off `main` after Phase 1 merged). Internal-only
refactor; no public API change; no release. Byte-exact — the existing suites are the safety net.

## Key finding: infrastructure largely exists (adopt + consolidate)

- `SRRBlock.cs` defines `SRRBlockType` (`Header=0x69, StoredFile=0x6A, OSOHash=0x6B, RARPadding=0x6C,
  RARFile=0x71`), `CustomPackerType`, and the SRR flag enums (`SRRHeaderFlags` incl. `AppNamePresent=
  0x0001`; `SRRBlockFlags` incl. `None=0x0000, SkipIfUnknown=0x4000, LongBlock=0x8000`). Code writes
  raw `0x69`/`0x6A`/`0x71`/`0x8000` instead of these.
- `SRRVerifier.cs` has PRIVATE consts for the five CRC sentinels (`HeaderSentinel=0x6969,
  StoredFileSentinel=0x6A6A, OSOSentinel=0x6B6B, RARPaddingSentinel=0x6C6C, RARFileSentinel=0x7171`) —
  so writer/editor paths (`SRRWriter`, `SrrBlockWriter`, `SRREditor`) redeclare them as raw literals.
- Three classes (`SRRVerifier`, `SrrBlockWriter`, `SRREditor`) each independently declare
  `BaseHeaderSize=7`, `AddSizeFieldLength=4`, `NameLengthFieldLength=2` (SRRVerifier lacks the last).
- `Rar4HeaderLayout` (Phase 1, `ReScene/RAR/Rar4HeaderLayout.cs`) already has the RAR4 header offsets
  `SRRWriter` needs; `RARUtils.Rar4Marker`/`Rar5Marker` give marker lengths; `AsciiDigitZero=0x30`
  exists.

The exact enum member names (`SRRHeaderFlags`/`SRRBlockFlags`) must be confirmed against `SRRBlock.cs`
during implementation before adoption.

## Work breakdown

### A. Adopt existing SRR enums (block types + flags)

Replace raw literals that duplicate an enum member:
- Block-type bytes: `SRRFileParser.cs:12` (the `is 0x69 or 0x6A or 0x6B or 0x6C or 0x71` type guard →
  `(byte)SRRBlockType.*`), `:296` (`>= 0x69 and <= 0x71` range → `(byte)SRRBlockType.Header` min /
  `(byte)SRRBlockType.RARFile` max); `SRRWriter.cs:425/442/456` (`0x69`/`0x71`/`0x6B`);
  `SrrBlockWriter.cs:26` (`0x6A`). (`SRREditor.cs:238` already uses the enum — leave.)
- Flag words: `SRRWriter.cs:414` (`0x0001`→`SRRHeaderFlags.AppNamePresent`, else `0x0000`→`.None`),
  `:443/:457` (`0x0000`→`SRRBlockFlags.None`); `SrrBlockWriter.cs:27` (`0x8000`→`SRRBlockFlags.LongBlock`).

### B. New `SrrBlockLayout` static class — consolidate framing consts + CRC sentinels + OSO sizes

New file `ReScene/SRR/SrrBlockLayout.cs` (`internal static class`), the single source of truth:
- Framing: `BaseHeaderSize=7` (CRC 2 + type 1 + flags 2 + size 2), `AddSizeFieldLength=4`,
  `NameLengthFieldLength=2`.
- CRC sentinels (`ushort`): `HeaderSentinel=0x6969, StoredFileSentinel=0x6A6A, OSOSentinel=0x6B6B,
  RARPaddingSentinel=0x6C6C, RARFileSentinel=0x7171`.
- OSO block field sizes: `OsoFileSizeLength=8`, `OsoHashLength=8`, `OsoFixedPayloadSize=
  OsoFileSizeLength + OsoHashLength + NameLengthFieldLength` (=18).

Then DELETE the duplicated private consts in `SRRVerifier`, `SrrBlockWriter`, `SRREditor` and point
them (plus `SRRWriter`, `SRRFile`, `SRRFileParser`) at `SrrBlockLayout`. Adoption sites: sentinels at
`SRRWriter.cs:424/441/455`, `SrrBlockWriter.cs:25`, `SRREditor.cs:237`; framing at `SRRWriter.cs:416/
439/453`, `SRRFileParser.cs:125`, `SRRFile.cs:498/530` (SRR `BaseHeaderSize`) and `SRRFile.cs:522`
(the SRR ADD_SIZE read `+ 4` → `SrrBlockLayout.AddSizeFieldLength`); OSO sizes at `SRRFileParser.cs:50/56`,
`SRRWriter.cs:453`.

### C. Reuse `Rar4HeaderLayout` for `SRRWriter`'s embedded RAR4 header parsing (cross-phase)

`SRRWriter` (and a few `SRRFileParser` sites) read raw RAR4 headers. Adopt the Phase-1 layout:
- ADD_SIZE offset `blockStart + 7` (`SRRWriter.cs:560`) → `Rar4HeaderLayout.AddSize`.
- HIGH_PACK_SIZE: `headerSize >= 36` (`:574`) → `Rar4HeaderLayout.HighPackSizeOffset +
  Rar4HeaderLayout.AddSizeFieldLength` (32+4); `ToUInt32(headerBytes, 32)` (`:576`) →
  `Rar4HeaderLayout.HighPackSizeOffset`.
- METHOD/NAME/filename: `headerBytes[25]` (`:649`) → `headerBytes[Rar4HeaderLayout.Method]` (**ADD
  `Method=25` to `Rar4HeaderLayout`**); `0x30` (`:650`, and `SRRFileParser.cs:561/569/650`) →
  `Rar4HeaderLayout.AsciiDigitZero`; `ToUInt16(headerBytes, 26)` (`:656/:675`) →
  `Rar4HeaderLayout.NameSize`; filename base `32` (`:657-658/:681`) → `Rar4HeaderLayout.FixedFieldsEnd`.
- Marker lengths: `7` (`:508/:514`) → `RARUtils.Rar4Marker.Length`; `8` (`:694/:700`,
  `SRRFileParser.cs:281/:282`) → `RARUtils.Rar5Marker.Length`. RAR4 base-header guards `7`
  (`SRRWriter.cs:529/:542`, inside `ProcessRar4Volume`) → `Rar4HeaderLayout.BaseHeaderSize`. NOTE:
  `SRRFile.cs:498/:530` are SRR-BLOCK framing `7`s (in `SRRFile.Load`'s SRR walk), NOT RAR4 — they
  belong to §B → `SrrBlockLayout.BaseHeaderSize`. Do not map them to `Rar4HeaderLayout` (both equal 7
  and `SRRFile.cs` already imports `ReScene.RAR`, so a wrong mapping would compile silently — this is
  the four-way-`7` trap).
- CMT sub-type: `headerSize < 35` (`:670`) and the `3`/`"CMT"` length (`:676/:681`) → a method-local
  `const int CmtSubTypeLength = 3` (narrow scope); `35 = FixedFieldsEnd + CmtSubTypeLength`.
- `headerSize < 26` guard (`:644`) → expressed via `Rar4HeaderLayout.Method` (need ≥ Method+1 bytes).

Add to `Rar4HeaderLayout` (Phase-1 file, `ReScene/RAR/`): `Method = 25` (and `UnpVer = 24` if a site
needs it). This is a small, in-scope extension of the Phase-1 layout; the RAR suites still guard it.

### D. Packer sentinels + RAR-version + misc

- `0xFFFFFFFFFFFFFFFF` / `0xFFFFFFFF` custom-packer sentinels (`SRRFileParser.cs:379/:386`) → named
  consts next to `CustomPackerType` in `SRRBlock.cs` (e.g. `internal const ulong
  PackerSentinelAllOnes = 0xFFFFFFFFFFFFFFFFUL; internal const uint PackerSentinelMaxUint32 = 0xFFFFFFFFU;`).
- RAR version `50` (`SRRFileParser.cs:285/:563/:571`) → a `private const int RarVersion50 = 50;` local
  to `SRRFileParser`.

### E. Explicit non-goals (leave as literals)

- `<< 32` shift (`SRRWriter.cs:577`) — a shift, not an offset.
- `1024 * 1024` sanity cap (`SRRFileParser.cs:518`) — implementation limit, not a format constant.
- `i += 8` ulong stride in `OSOHashCalculator` — tied to `sizeof(ulong)`, not a format field.
- Trivial `0/1/2`, loop counters, display/string literals (`"0x69"` in messages).
- **Boundary for `NameLengthFieldLength=2`:** the const is introduced for the block-FRAMING
  size expressions (writer + `SRRFileParser.cs:125`). The many inline name-length `+ 2` reads in the
  parser (`SRRFileParser.cs:30,58,63,91,96,137,143,177,182`) are deliberately LEFT as raw `2` — they
  are per-field read strides, not the framing constant. This split is intentional; a later `git grep`
  of "replaced literals" that finds these `2`s is NOT an incompleteness.
- `SRRFileParser.cs:317` `4 + 1` RAR5 framing approximation — low value/borderline; may name the `4`
  (`Rar5CrcFieldLength`) or leave; implementer's call, documented either way.
- Anything outside `SRR/` except the small `Rar4HeaderLayout.Method` addition (C).

## Overloaded literals — the review lens (name by INTENT, not value)

- **`7` — FOUR meanings, all value 7 (highest risk):** (1) SRR block base-header size →
  `SrrBlockLayout.BaseHeaderSize`; (2) RAR4 marker byte-length → `RARUtils.Rar4Marker.Length`; (3) RAR4
  block base-header size → `Rar4HeaderLayout.BaseHeaderSize`; (4) RAR4 ADD_SIZE field OFFSET →
  `Rar4HeaderLayout.AddSize`. The reviewer must confirm each `7` site got the name matching its intent —
  SRR-framing vs RAR4-embedded, marker vs header vs ADD_SIZE.
- **`32`:** `Rar4HeaderLayout.HighPackSizeOffset` (LARGE) vs `FixedFieldsEnd` (filename base) — as in
  Phase 1.
- **`8` — three meanings:** OSO file-size length / OSO hash length (`SrrBlockLayout.OsoFileSizeLength` /
  `OsoHashLength`) vs RAR5 marker length (`RARUtils.Rar5Marker.Length`). Watch the adjacent `7+8+8+2`
  arithmetic at `SRRWriter.cs:453`.
- **`26`:** `Rar4HeaderLayout.NameSize` (offset) vs a `< 26` guard derived from `Method + 1`.

## Byte-exact verification (zero behaviour change)

- Every named constant equals the literal it replaces; the per-task reviewer verifies value AND intent
  at each site (especially the overloaded `7`/`8`/`32`).
- The existing suites are the safety net — all green, NO new failures (do not gate on an absolute
  count). SRR round-trip/parse tests (`SRRWriter`/`SRRFile`/`SRRFileParser`/`SRRVerifier`/`SRREditor`
  tests, incl. the Phase-1-added real-SRR-with-embedded-RAR round-trip and verifier tests) plus the RAR
  suites (which guard the `Rar4HeaderLayout.Method` addition) must stay green.
- **Verification blind spots — pin these BEFORE renaming (as Phase 1 did for EXT_TIME display):**
  - **OSO write path** — `WriteOSOHashBlock` (the `7 + 8 + 8 + 2` framing at `SRRWriter.cs:453`) has
    NO test asserting its emitted bytes (the existing OSO writer test asserts *no* block is emitted;
    the OSO parse round-trip uses the test-local `SRRTestDataBuilder.AddOSOHash`, not production
    `WriteOSOHashBlock`). Add a characterization test driving `CreateAsync(…, ComputeOSOHashes=true)`
    over a hashable fixture (or asserting `WriteOSOHashBlock` bytes) — a mis-mapped OSO size const
    here would otherwise be undetected.
  - **CMT write detection** — `IsRar4CmtServiceBlock` (`SRRWriter.cs:667-683`, the `< 35` / `3` / `32`
    reads) is only exercised on the *parse* path; add a create-time characterization test before
    renaming its constants.
  - Already covered (no new test needed): the custom-packer sentinel branch
    (`0xFFFFFFFFFFFFFFFF`/`0xFFFFFFFF` at `SRRFileParser.cs:379/386`) is tested by `SRRFileTests`'
    three `CustomPackerType` cases; the compressed-file/`headerBytes[25]`/`0x30` path by
    `SRRWriterTests`; SRR framing/sentinels by `SRRVerifierTests` (incl. the real-embedded-RAR case).
- 0-warning build (both TFMs, `-p:BaseOutputPath=bin2/ --no-incremental`).

## Naming conventions

- Follow Phase 1: `internal static class SrrBlockLayout` with `public const`; adopt existing
  `[Flags]` enums / `SRRBlockType`; sentinels as `const ushort`; packer sentinels `const ulong`/`uint`.
- Reuse Phase-1 names (`Rar4HeaderLayout.*`, `RARUtils.Rar4Marker/Rar5Marker`, `AsciiDigitZero`) rather
  than minting SRR-local duplicates of the same value.

## File structure

- Create: `ReScene/SRR/SrrBlockLayout.cs`.
- Extend: `ReScene/RAR/Rar4HeaderLayout.cs` (add `Method=25`, maybe `UnpVer=24`); `ReScene/SRR/
  SRRBlock.cs` (packer-sentinel consts).
- Modify: `SRRWriter.cs`, `SRRFileParser.cs`, `SRRFile.cs`, `SRRVerifier.cs`, `SrrBlockWriter.cs`,
  `SRREditor.cs` (only domain-literal sites).
- Remove duplicates: the private framing/sentinel consts in `SRRVerifier`, `SrrBlockWriter`, `SRREditor`
  (migrated into `SrrBlockLayout`).
- Tests: existing suites pin behaviour; add characterization tests for any untested rename site found
  during planning.

## Success criteria

- SRR-namespace domain literals (block types, CRC sentinels, flags, framing offsets/sizes, embedded
  RAR4 offsets, OSO sizes, packer sentinels, RAR-version tag) are named; a `git grep` of the replaced
  literals in `SRR/*.cs` returns only definitions.
- Zero behavioural change: all suites green (no new failures), 0 warnings.
- Exactly one definition per constant (the three duplicated framing/sentinel const sets consolidated
  into `SrrBlockLayout`; no SRR-local duplicate of a Phase-1 RAR constant).
- The overloaded `7`/`8`/`32`/`26` are disambiguated by intent at every site.
