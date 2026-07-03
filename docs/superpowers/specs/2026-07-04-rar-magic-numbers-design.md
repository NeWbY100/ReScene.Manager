# Eliminate Magic Numbers — Phase 1: RAR namespace (Design)

**Date:** 2026-07-04
**Status:** Draft (pending review)
**Scope:** `ReScene.Lib/ReScene/RAR/*.cs` (NOT `RAR/Decompression/**` — that is Phase 5).
**Nature:** Behaviour-preserving refactor. Zero functional change; only numeric literals become named.

## Background & goal

The engine's binary-format code uses hundreds of bare numeric literals — flag bits, block types,
field offsets, header sizes, bit masks, method codes, markers. The RAR namespace alone has ~326 hex
literals plus many decimal offset/size literals. During the 2026-07 audit, several real bugs traced
directly to bare offsets/flags (`(flags & 0x0100)` where a named `Large` flag existed;
`(flags & 0x00E0)` directory check with no name; `blockStart + 7`; HIGH_PACK_SIZE at offset `32`).

Goal: replace domain numeric literals in the RAR namespace with named constants, so the code reads
in domain terms and a wrong constant is caught at one definition site rather than scattered.

This is **Phase 1 (pilot)** of a phased effort (RAR → SRR → SRS → Core+app → Decompression). It
establishes the constant organisation, naming conventions, and byte-exact verification recipe that
the later phases replay. Each phase is its own branch/PR.

### Sequencing (hard dependency)

Phase 1 edits the same RAR files the current audit-fix branch (`fix/audit-2026-07-03`) just changed.
**Implementation MUST start from a branch based on `main` AFTER that bugfix branch is merged** (or,
if it is not merged, stacked on its tip). Starting from a stale `main` would conflict with the
bugfix merge. This spec can be written/reviewed now; coding waits for the merge.

## Key finding: the infrastructure largely already exists

The RAR namespace already defines most domain constants as enums — the code simply uses raw literals
instead of them. So Phase 1 is **mostly adoption**, not new definition:

- `RARFlags.cs` already has `RARArchiveFlags`, `RARFileFlags` (incl. `Directory = 0x00E0`,
  `Large = 0x0100`, `Unicode = 0x0200`, `ExtTime = 0x1000`, `LongBlock = 0x8000`, the dict-size
  flags, and `DictionarySizeMask = 0x00E0`), `RAREndArchiveFlags`, and `TimestampPrecision`.
- `RARBlockType.cs` has `RAR4BlockType` (`Marker=0x72`, `ArchiveHeader=0x73`, `FileHeader=0x74`,
  `Comment=0x75`, `Service=0x7A`, …).
- `RARMethod` enum exists; `RAR5HeaderReader.RAR5Marker` is a named `static readonly byte[]`.

## Work breakdown

### A. Adopt existing enums (the bulk; lowest risk)

Replace raw literals that duplicate an existing enum member:
- Flag tests: `(flags & 0x0100) != 0` → `flags.HasFlag(RARFileFlags.Large)`; `(flags & 0x8000)` →
  `…LongBlock`; `(flags & 0x00E0) == 0x00E0` → `flags.HasFlag(RARFileFlags.Directory)` (0x00E0 is a
  full mask value, so `HasFlag` is the correct all-bits-set test). Where a `ushort`/`int` is compared
  to a raw flag literal, cast to the enum or compare `((RARFileFlags)flags).HasFlag(...)`.
- Block types: `type == 0x74` → `type == (byte)RAR4BlockType.FileHeader` (matching the existing cast
  style already used in the codebase).
- Method codes: `(RARMethod)(0x30 + method)` — replace the bare `0x30` with a named
  `RarMethod.AsciiBase` (or reuse an existing ASCII-digit constant if present).

Each such replacement is value-identical by construction.

**Caution — `0x00E0` is overloaded.** It is BOTH `RARFileFlags.Directory` (the all-window-bits-set
value, tested as `(flags & 0x00E0) == 0x00E0`) AND `DictionarySizeMask` (used as `flags & 0x00E0` to
*extract* the dictionary-size bits). The implementer must choose the name by intent — a directory
test → `RARFileFlags.Directory`; a dict-size extraction → `DictionarySizeMask` — not blindly map the
literal to one name. This kind of context-dependence is exactly why the reviewer checks intent, not
just value, on each substitution.

### B. Add the genuinely-missing constants

Introduce named constants only where none exists:
- **`Rar4HeaderLayout` (new static class)** — the single source of truth for RAR4 header field
  offsets/sizes duplicated across `RARHeaderReader`, `RARDetailedHeader`, `SRRWriter`, `RARPatcher`,
  `RARStream`, `RARArchive`:
  - `BaseHeaderSize = 7` (CRC 2 + type 1 + flags 2 + size 2)
  - `AddSizeFieldLength = 4`
  - `HighPackSizeOffset = 32` (after ATTR, when the LARGE flag is set)
  - EXT_TIME decode constants: `ExtTimePresentBit = 0x8`, `ExtTimePrecisionMask = 0x3`, and the
    per-field nibble width used by `>> ((3 - i) * 4)` (name the `4` bits-per-nibble and the `3`
    field-count as `NibbleBits`/`ExtTimeFieldCount` where it clarifies).
- **`Rar4Marker`** — a `static readonly byte[] = [0x52,0x61,0x72,0x21,0x1A,0x07,0x00]` mirroring the
  existing `RAR5Marker`, if the RAR4 marker is currently inline anywhere.
- Any remaining local, single-use bit mask/shift that is genuinely a format constant becomes a
  `private const`/`internal const` near its use, with a one-line comment on what field it decodes.

### C. Explicit non-goals (leave as literals)

- Trivial control-flow ints: loop bounds (`for (i = 0; i < 3; …)`), `+1`/`-1` arithmetic, `0`/`1`/`2`.
- General buffer/array sizes that are implementation choices, not format constants
  (e.g. a read-chunk buffer length) — unless the size IS a format constant (then name it).
- `RAR/Decompression/**` — Phase 5.
- No API redesign, no method extraction beyond a tiny `IsDirectoryEntry`-style helper if it reads
  better than inline `HasFlag`.

## Byte-exact verification (zero behaviour change)

This refactor must not alter a single output byte or comparison result.

- **Every named constant equals the literal it replaces.** This is a mechanical correctness
  requirement; the per-task reviewer verifies each substitution is value-identical against the
  pre-refactor literal.
- **The existing suites are the safety net** — the full lib suite (1195) and app suite (341) must
  stay green, especially the byte-exact round-trip tests (`RARPatcherTests`, `SRRWriterTests`,
  `RARHeaderReaderTests`, `RARDetailedHeader`/`RARArchive`/`RARStream` tests) that pin exact bytes,
  offsets, and parsed field values.
- **No new behavioural tests** are required (behaviour is unchanged). Optionally add one small test
  asserting the new layout constants have their expected values (`Rar4HeaderLayout.HighPackSizeOffset
  == 32`, etc.) as living documentation of the format.
- A clean review pass over the `git diff`: it should contain ONLY literal→named substitutions of
  identical value plus the new constant definitions — no logic edits.

## Naming conventions

- Follow the existing style: `[Flags]` enums with PascalCase members; static-class constants as
  `public const int Xxx` / `internal const`; markers as `static readonly byte[]`.
- Layout constants live in `Rar4HeaderLayout` (new file `ReScene/RAR/Rar4HeaderLayout.cs`), internal
  unless a consumer outside the assembly needs them.
- (Phase 5 note, not this phase) Decompression constants get PascalCase names with a
  `// unrar: MAXWINSIZE` comment mapping to the upstream identifier, for sync traceability without
  tripping the C# naming analyzers.

## File structure

- Modify: `RARHeaderReader.cs`, `RARDetailedHeader.cs`, `RARPatcher.cs`, `RARStream.cs`,
  `RARArchive.cs`, `RAR5HeaderReader.cs`, `RARFileHeader.cs`, `RARUtils.cs`, `RARVolumeNaming.cs`
  (only where they carry RAR-domain literals).
- Extend (if a needed flag/type/method value is genuinely absent): `RARFlags.cs`, `RARBlockType.cs`.
- Create: `ReScene/RAR/Rar4HeaderLayout.cs` (offsets/sizes), and `Rar4Marker` (in an appropriate
  existing file or a small `RarMarkers` addition).
- Tests: existing suites pin behaviour; optionally one `Rar4HeaderLayout` value-assertion test.

## Testing & verification

- Build both TFMs of the lib with `-p:BaseOutputPath=bin2/ --no-incremental` → **0 warnings /
  0 errors** (AnalysisLevel=latest-All; EnforceCodeStyleInBuild).
- Full lib suite green (1195) and, since the app references the lib, full app suite green (341).
- Reviewer confirms the diff is a pure, value-preserving substitution.

## Success criteria

- The RAR namespace's domain numeric literals (flags, block types, method codes, markers, RAR4
  header offsets/sizes, format bit masks) are named; `git grep` for the specific replaced literals in
  `RAR/*.cs` returns only the constant definitions.
- Zero behavioural change: all suites green, byte-exact tests unaffected, 0 warnings.
- The recipe (adopt-enums + `Rar4HeaderLayout` + byte-exact verification) is documented enough for
  the later phases to replay.
