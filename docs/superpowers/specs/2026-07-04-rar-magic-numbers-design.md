# Eliminate Magic Numbers — Phase 1: RAR namespace (Design)

**Date:** 2026-07-04
**Status:** Reviewed (Approve-with-changes; findings folded in)
**Scope:** `ReScene.Lib/ReScene/RAR/*.cs` (NOT `RAR/Decompression/**` — that is Phase 5).
**Nature:** Behaviour-preserving refactor. Zero functional change; only numeric literals become named.

## Background & goal

The engine's binary-format code uses hundreds of bare numeric literals — flag bits, block types,
field offsets, header sizes, bit masks, method codes, markers. The RAR namespace alone has ~326 hex
literals plus many decimal offset/size literals. During the 2026-07 audit, several real bugs traced
directly to bare offsets/flags (`(flags & 0x0100)` for the LARGE flag; the HIGH_PACK_SIZE field at a
bare offset `32`; `blockStart + 7`).

Goal: replace domain numeric literals in the RAR namespace with named constants, so the code reads in
domain terms and a wrong constant is caught at one definition site rather than scattered.

This is **Phase 1 (pilot)** of a phased effort (RAR → SRR → SRS → Core+app → Decompression). It
establishes the constant organisation, naming conventions, and byte-exact verification recipe the
later phases replay. Each phase is its own branch/PR.

### Sequencing

The audit-fix branch is merged to `main`; Phase 1 is implemented on `refactor/magic-numbers-rar`
branched off that `main`.

## Key finding: the infrastructure largely already exists

The RAR namespace already defines most domain constants — the code simply uses raw literals instead
of them, and in a few cases defines the *same* constant in more than one place. So Phase 1 is **mostly
adoption and consolidation**, not new definition. Verified against the code:

- `RARFlags.cs`: `RARArchiveFlags`, `RARFileFlags` (incl. `Directory = 0x00E0`, `Large = 0x0100`,
  `Unicode = 0x0200`, `ExtTime = 0x1000`, `LongBlock = 0x8000`, and the `DictSize64…DictSize4096`
  field values), `RAREndArchiveFlags`, `TimestampPrecision`; plus a sibling static class
  `RARFlagMasks` with `DictionarySizeMask = 0x00E0`. **(pinned by `RARFlagsTests`.)**
- `RARBlockType.cs`: `RAR4BlockType` (`Marker=0x72`, `ArchiveHeader=0x73`, `FileHeader=0x74`,
  `Service=0x7A`, …).
- `RARUtils.cs`: `Rar4Marker` (`:24`) and `Rar5Marker` (`:29`) already exist as `static readonly
  byte[]` — but `RAR5HeaderReader.RAR5Marker` (`:420`) **duplicates** `Rar5Marker`, and the raw marker
  bytes are still inlined in `RARUtils.FindRarMarkerOffset` and `RARDetailedParser.IsValidRAR4Signature`.
- Partial header-offset constants already exist as `RARPatcher`'s private consts (`OffsetCRC=0 …
  OffsetAttr=28, OffsetHighPackSize=32`, `RARPatcher.cs:222-231`) and as locals in
  `RARHeaderReader.ParseFileHeader`/`ParseServiceBlock` (`baseHeaderSize=7`, `addSizeField=4`,
  `serviceFieldsSize=21`). These are **duplicated sources of truth** to consolidate.
- `RARMethod` enum exists — but in `RAR/Decompression/RARDecompressor.cs`, which is out of Phase-1
  scope (frozen until Phase 5); only one in-scope site uses it (`RARArchive.cs:273`, `0x30 + method`).

The RAR5 side is **less complete**: there is no `RAR5ArchiveFlags` enum (so `RAR5ArchiveInfo` uses raw
`0x0001…0x0010`, `:123-143`), and the vint / CompInfo bit-fields are all raw literals.

## Work breakdown

### A. Adopt existing enums (the bulk; lowest risk)

Replace raw literals that duplicate an existing enum member:
- Flag tests: `(flags & 0x0100) != 0` → `((RARFileFlags)flags).HasFlag(RARFileFlags.Large)`;
  `(flags & 0x8000)` → `…LongBlock`; the all-bits-set directory test `(flags & 0x00E0) == 0x00E0` →
  `…HasFlag(RARFileFlags.Directory)` (0x00E0 is a full-mask value, so `HasFlag` == the existing
  all-bits-set semantics — see `RARUtils.IsDirectory`).
- Block types: `type == 0x74` → `type == (byte)RAR4BlockType.FileHeader` (existing cast style).

**HasFlag hazard — never apply `HasFlag` to multi-bit *field values*.** `RARFileFlags.DictSize64…
DictSize4096` (0x0000…0x00C0) and `Directory` (0x00E0) occupy the same three bits as a numeric
*field*, not independent flags. `HasFlag(DictSize64)` (0x0000) is always true; `HasFlag(DictSize512)`
is true for any superset. The dictionary-size bits must stay mask-and-shift (`flags &
RARFlagMasks.DictionarySizeMask`, as `RARUtils.GetDictionarySize` already does), never `HasFlag`.
`HasFlag` is only a safe 1:1 replacement for `(x & bit) != 0` on genuine single-bit flags
(Large/Unicode/ExtTime/LongBlock/Salt/…) and for the `== 0x00E0` all-bits-set directory test.

### B. Consolidate & add the missing named constants

- **Markers → consolidate onto the existing `RARUtils.Rar4Marker`/`Rar5Marker`** — which are
  `ReadOnlySpan<byte>` expression-bodied properties, NOT `byte[]`. The duplicate
  `RAR5HeaderReader.RAR5Marker` (`:420`) is a real `byte[]` and is asserted on directly by
  `RAR5HeaderReaderTests.cs:58,64`, so **deleting it outright breaks the test compile** (and no suite
  can run). Either keep it as a thin alias (`=> RARUtils.Rar5Marker.ToArray()`) OR migrate those two
  tests to `RARUtils.Rar5Marker`, adjusting `Assert.Equal(byte[], …)` for the span type
  (`.ToArray()`/`SequenceEqual`). Where byte-exact behaviour allows, replace the inline marker byte
  sequences in `RARUtils.FindRarMarkerOffset` (`:335-364` — a prefix-scan that branches on byte 6/7
  for RAR4-vs-RAR5 plus a tail special-case; reuse the marker consts only where a prefix/`SequenceEqual`
  check preserves the exact scan) and `RARDetailedParser.IsValidRAR4Signature` (which can reuse the
  existing `RARDetailedHeader.cs:236` `Rar4Signature => RARUtils.Rar4Marker` alias). (Test-covered:
  `RARDetailedParserTests` + every `RARArchive`/`RARStream` fixture writes the marker.)
- **`Rar4HeaderLayout` (new static class) — consolidate the existing scattered offsets into one
  source of truth.** Migrate `RARPatcher`'s private `Offset*` consts and the `RARHeaderReader`
  locals into it; both files then reference the shared class. Members (values verified against
  those existing definitions): `Crc=0, Type=2, Flags=3, HeaderSize=5, AddSize=7`, `BaseHeaderSize=7`,
  `AddSizeFieldLength=4`, `HostOs=15, FileTime=20, NameSize=26, Attr=28`, and the two overloaded-`32`
  names below. **Do not create a parallel class** — the migration must delete the duplicated
  `RARPatcher.Offset*` consts / `RARHeaderReader` locals so there is exactly one definition.
- **`32` is overloaded (like `0x00E0`) — give it TWO intent-named constants of the same value.**
  `32` is the size of the fixed file-header fields, so it is simultaneously the HIGH_PACK_SIZE offset
  (only when LARGE is set) AND the NAME offset (when LARGE is clear): `RARPatcher.cs:488/667`
  `nameOffset = 32 + (large ? 8 : 0)`; `:843-851` copy/insert at 32. Provide
  `HighPackSizeOffset = 32` **and** `FixedFieldsEnd = 32` (used as the name base when not LARGE) —
  choose by intent at each site, never one name for both. The derived siblings — `11` (=7+4) at
  `:601,829`; `36` (=32+4) at `:264,743`; `40` (=32+8) at `:873` — should be expressed as
  `BaseHeaderSize + AddSizeFieldLength` / `FixedFieldsEnd + 4` / `FixedFieldsEnd + 8` rather than bare
  numbers. (Note `RARPatcher.cs:466` is a bare `>= 32` guard — that is a `FixedFieldsEnd` use, not a
  sibling.)
- **EXT_TIME decode constants (name the whole nibble family, not half).** `ExtTimePresentBit = 0x8`,
  `ExtTimeRoundUpBit = 0x4`, `ExtTimePrecisionMask = 0x3`, the per-field nibble arithmetic `(3 - t) *
  4` (name the `4` bits-per-nibble and `3`/`4` field count/index), and the mtime-nibble mask family
  `<< 12` / `>> 12` / `& 0x0FFF` (`RARHeaderReader.cs:812-829`, `RARPatcher.cs:355-370`). All go in
  `Rar4HeaderLayout` (or a small `Rar4ExtTime` sub-group) with a one-line comment per field.
- **`RARMethod` adoption + `AsciiBase`.** At `RARArchive.cs:273` replace `0x30` with a new in-scope
  `const byte AsciiDigitZero = 0x30` (the `RARMethod` enum's file is frozen until Phase 5, so we do
  NOT edit it now; just name the ASCII base where it is used, also at `RARHeaderReader.cs:446`).
- **Host-OS names → a small `RarHostOs` enum or named consts** for the 0–5 table in
  `RARPatcher.GetHostOSName` (`:242-251`), which currently has no name.
- **DOS date/time bit-fields → named masks/shifts.** `RARUtils.DosDateToDateTime` (`:91-101`) and
  `RARPatcher.EncodeDosDate` (`:280-287`): `0x1F`/`0x0F`/`0x7F`/`0x3F` masks, shifts `9/5/11`, and the
  `1980` epoch (`DosDateToDateTime` already names `dosEpochYear=1980`; `EncodeDosDate` re-inlines it —
  consolidate onto the one name).
- **RAR5 bit-fields (in scope).** Add a `RAR5ArchiveFlags` enum (parallel to `RARArchiveFlags`) and
  point `RAR5ArchiveInfo`'s raw `0x0001…0x0010` (`:123-143`) at it. Name the vint decode masks
  (`0x7F` data, `0x80` continuation, `63` max-shift; `:522-533`) and the CompInfo unpacking
  (`& 0x3F` version, `(>>7) & 0x07` method, `(>>10) & 0x0F` dict, `128 << power`) at
  `RAR5HeaderReader.cs:225-235,706-708` and the duplicate in `RARDetailedHeader.cs:1276-1279`.
- **Misc sentinels/masks.** The `0xFFFFFFFF` custom-packer sentinel checks
  (`RARDetailedHeader.cs:755,840,862`) and the CRC low-16 mask `0xFFFF` (`RARUtils.cs:54`) get named
  consts.

### C. Explicit non-goals (leave as literals)

- Trivial control-flow ints: loop bounds, `+1`/`-1`, `0`/`1`/`2`.
- General buffer/array sizes that are implementation choices, not format constants.
- `RAR/Decompression/**` — Phase 5 (this includes the `RARMethod` enum's file; we only *use* it).
- No API redesign; no method extraction beyond a tiny `IsDirectoryEntry`-style helper if it reads
  better than inline `HasFlag`.
- `RARVolumeNaming.cs` carries no binary-format literals (naming/regex only; its `99` is already
  named) — not a target.

## Overloaded literals — the review lens

Some literals map to more than one meaning; the reviewer must check **intent, not just value**:
- `0x00E0` = `RARFileFlags.Directory` (tested `== 0x00E0`) **and** `RARFlagMasks.DictionarySizeMask`
  (used as `& 0x00E0` to extract the dict-size bits).
- `32` = fixed-file-fields size = HIGH_PACK_SIZE offset (LARGE) = NAME offset (non-LARGE).
- The `DictSize*` enum members are field *values*, not flags (never `HasFlag` them).
Each substitution must pick the name that matches the code's intent at that site.

## Byte-exact verification (zero behaviour change)

- **Every named constant equals the literal it replaces.** A mechanical correctness requirement; the
  per-task reviewer verifies each substitution is value-identical AND intent-correct against the
  pre-refactor literal.
- **The existing suites are the safety net — require all suites green with no new failures**
  (do not gate on an absolute count; the byte-exact tests are what matter): `RARPatcherTests`
  (rewrites bytes/CRCs incl. LARGE insert/remove and EXT_TIME remainder bytes MSB-first at 1/2/3-byte
  precision), `SRRWriterTests`, `RARHeaderReaderTests`, `RARDetailedParserTests`, `RARArchiveTests`,
  `RARStreamTests`, `RARFlagsTests`.
- **Known verification blind spot — add one focused test.** The verbose EXT_TIME *display* decode in
  `RARDetailedParser.ParseRAR4FileHeader` (`:895-970` — the `0x8`/`0x4`/`0x3` nibble decode, `(3-t)*4`
  bit arithmetic, precision labels) has **no existing test**, so a fat-fingered constant there would
  go undetected. Phase 1 adds a small `RARDetailedParserTests` case asserting the rendered EXT_TIME
  field of a known header, before renaming those constants. (The RAR5 CompInfo *display* duplicate at
  `RARDetailedHeader.cs:1276-1279` is likewise display-only, but the identical masks are exercised by
  the RAR5 reader tests, so a wrong value would surface there — no extra test required.)
- Optionally add a `Rar4HeaderLayout` value-assertion test (`HighPackSizeOffset == 32`, etc.) as
  living documentation.
- The reviewer confirms the `git diff` is ONLY value-preserving substitutions + the consolidated/new
  constant definitions + the removal of the now-duplicated ones — no logic edits.

## Naming conventions

- `[Flags]` enums, PascalCase members; static-class constants `public const`/`internal const`. The
  existing markers are `ReadOnlySpan<byte>` expression-bodied properties in `RARUtils` — consolidate
  onto those, don't introduce a new `byte[]` form.
- New `ReScene/RAR/Rar4HeaderLayout.cs` (offsets/sizes/EXT_TIME), internal.
- `RAR5ArchiveFlags` added to `RARFlags.cs`; `RarHostOs` where the host-OS table lives.

## File structure

- Modify: `RARHeaderReader.cs`, `RARDetailedHeader.cs`, `RARPatcher.cs`, `RARStream.cs`,
  `RARArchive.cs`, `RAR5HeaderReader.cs`, `RARUtils.cs`, `RARFileHeader.cs` — only where they carry
  RAR-domain literals.
- Extend: `RARFlags.cs` (add `RAR5ArchiveFlags`), `RARBlockType.cs` (only if a value is genuinely
  absent).
- Create: `ReScene/RAR/Rar4HeaderLayout.cs`.
- Remove/alias duplicates: `RARPatcher.Offset*` private consts and the `RARHeaderReader` offset
  locals (migrated into `Rar4HeaderLayout`); `RAR5HeaderReader.RAR5Marker` becomes a thin alias to
  `RARUtils.Rar5Marker` OR is removed with `RAR5HeaderReaderTests.cs:58,64` migrated (see §B).
- Tests: existing suites pin behaviour; add the EXT_TIME-display test (required); adjust
  `RAR5HeaderReaderTests.cs:58,64` if the marker duplicate is removed; optionally a layout
  value-assertion test.

## Success criteria

- The RAR namespace's domain numeric literals (flags, block types, method base, markers, RAR4 header
  offsets/sizes, EXT_TIME + DOS date bit-fields, host-OS table, RAR5 flags/vint/CompInfo fields,
  sentinels) are named; a `git grep` for the specific replaced literals in `RAR/*.cs` returns only
  the constant definitions.
- Zero behavioural change: all suites green (no new failures), byte-exact tests unaffected, 0 warnings.
- Exactly one definition per constant (duplicates consolidated).
- The recipe (adopt-enums + consolidate-into-layout + intent-over-value on overloaded literals +
  byte-exact verification + fill the EXT_TIME-display test gap) is documented enough for the later
  phases to replay.
