# Eliminate Magic Numbers — Phase 3: SRS namespace (Design)

**Date:** 2026-07-04
**Status:** Draft (pending review)
**Scope:** `ReScene.Lib/ReScene/SRS/**/*.cs` (incl. `Handlers/`, `Rebuilders/`).
**Nature:** Behaviour-preserving refactor. Zero functional change; only numeric literals become named.
**Predecessor:** Phases 1 (RAR) + 2 (SRR), merged. Same recipe: adopt existing enums/consts →
consolidate duplicated consts into per-format id/layout classes → name the rest → byte-exact.
**Inventory:** `docs/superpowers/specs/2026-07-04-srs-magic-numbers-inventory.md` (the exhaustive per-site source of truth; the
implementation plan draws its site lists from it — line numbers are approximate anchors, re-locate by
value+context).

## Background & goal

SRS is the largest namespace: **~257 hex literals** plus decimal offsets/sizes across ~15 files and
seven container formats (RIFF/AVI, MKV/EBML, MP4, ASF/WMV, FLAC, MP3, Stream) with format-native block
framing (SRSF/SRST/SRSP). Much already exists as constants/enums the code bypasses with raw literals,
and several classes redeclare the same values. Goal: name the SRS domain literals so each reads in
domain terms and a wrong constant is caught at one definition site.

### Sequencing

Implemented on `refactor/magic-numbers-srs` (off `main` after Phase 2). Internal-only; no public API
change; no release. Byte-exact — the existing SRS suites are the safety net.

## Key finding: adopt + consolidate, then name per format

Existing infrastructure to ADOPT (do not re-create): `EBMLIds` (24 `const ulong`, EBMLWriter.cs) +
`EBMLIds.IsContainer`; `EBMLLaceType` enum (None=0, Xiph=2, Fixed=4, EBML=6); `SRSContainerType`;
`TrackInfo.SignatureSize=256`; `MP4Atoms.ContainerAtoms`; `SRSRebuilder.SearchBufferSize=0x10000`;
`SignatureScanner.DefaultBufferSize=64*1024`; the `MP3TagReader` tag-size privates
(`Id3v2HeaderSize=10`, `Id3v1TagSize=128`, `ApeTagHeaderSize=32`, `Lyrics3v2FooterSize=15`,
`MaxLyrics3v1Size=5100`); `SRSFile.AsfDataObjectHeaderLength=26`; `MKVContainerHandler.MaxSignatureBlocks=40`.

Duplicates to DELETE and re-point: `EBMLHeaderStripping.cs:11-15` (5 verbatim `EBMLIds` copies);
`FlacMetadataReader.Id3v2HeaderSize` (dup of `MP3TagReader`); the 3-way ASF pseudo-GUID fields
(`SRSFile`/`WMVHandler`/`WMVRebuilder`); the two `26` ASF-data-object consts
(`SRSFile.AsfDataObjectHeaderLength` vs `WMVRebuilder.DataObjectHeaderLength`).

## Work breakdown (8 tasks — follows the inventory §6 decomposition, low→high risk)

Each task creates/extends the named constant homes and adopts them at the inventory's sites for that
concern. New homes follow the Phase-1/2 per-format pattern.

- **T1 — EBML ID consolidation (pure dedup).** Delete the 5 `EBMLHeaderStripping` private-const dups
  + the MKV `Handler` private id fields; adopt `EBMLIds` everywhere raw EBML IDs appear (Handler,
  Rebuilder `IsKnown*`, SRSFile name switch); ADD the §1b new members (`FileData`, `FileName`,
  `FileMimeType`, `Timestamp`, `PrevSize`, `Position`, `CRC32Element`, `Void`, `TrackUID`, `TrackType`,
  `CodecID`, `BlockDuration`, `ReferenceBlock`), §1c display-only IDs, and `ContentCompAlgoHeaderStripping=3`.
- **T2 — EBML VINT / writer internals.** Extend `EBMLVInt` (EBMLLacing.cs) with the tier limits
  (`0x7F`/`0x3FFF`/`0x1FFFFF`/`0x0FFFFFFF`/`0x07FFFFFFFF`), `MaxByteWidth=8`, `Marker1..4`
  (0x80/0x40/0x20/0x10), `FiveByteMinWidth=5`; `EBMLIds` ID byte-width bounds (`0x100`/`0x10000`/
  `0x1000000`); `EBMLLacing.XiphContinuation=0xFF` (unify the `255`/`0xFF` split).
- **T3 — MKV block-flags lacing normalisation (BEHAVIOUR-SENSITIVE — highest risk).** Reconcile the
  three coexisting lacing idioms — `flags & 0x06` → `EBMLLaceType`; `(flags>>1)&0x03`; raw `==1`
  (Xiph)/`==3` (EBML) — onto `EBMLLaceType` + `MkvBlockFlags.LacingMask=0x06`, PRESERVING the exact
  lace-type decision at each site. Add `MkvBlockLayout.FixedHeaderOverhead=3`,
  `MKVContainerHandler.SignatureAsciiWindowSize=64`/`AsciiBoundary=0x80`,
  `MKVContainerRebuilder.PreTrackSkipMargin=4096`, `TrackInfo.CompressionAlgoUnknown=-1`.
- **T4 — SRS block framing + FourCCs + flags (cross-format core).** New `SrsBlockLayout.HeaderSize=8`,
  `SrsFourCC` (SrsFile/SrsTrack/SrsPadding/Strm), `[Flags] SrstFlags` (None/BigFile=0x4/
  BigTrackNumber=0x8), `[Flags] SrsfFlags` (None/SimpleBlockFix=0x1/AttachmentsRemoved=0x2),
  `SrstLayout.TrackNumberWidthThreshold=0x10000`, and shared `SrsConstants.BigFileSizeThreshold=
  0x80000000L` (replaces the 6 copies across all handlers).
- **T5 — MP4 atom layout.** New `Mp4AtomTypes` (Ftyp, `AtomHeaderSize=8`, `AtomExtendedHeaderSize=16`,
  `ExtendedSizeSentinel=1`, `ToEndSentinel=0`, `TkhdTrackIdOffsetV0=12`/`V1=20`/`FieldSize=4`).
- **T6 — RIFF/AVI + Stream framing.** New `RiffFourCC` (Riff, `ChunkHeaderSize=8`, `SizeOffset=4`),
  `StreamFourCC` (Strm, M2ts); adopt `SrsBlockLayout`.
- **T7 — ASF/WMV GUIDs + object framing.** New `AsfGuids` (HeaderObjectPrefix, DataObjectPrefix,
  `ObjectHeaderSize=24`, `GuidSize=16`, `DataObjectFileIdSize=16`, `DataObjectHeaderLength=26`),
  `AsfSrsGuids` (consolidate the 3-way pseudo-GUID dup); unify the two `26` consts; name the WMV
  virtual track number.
- **T8 — FLAC + MP3/ID3 tag constants.** New `FlacBlockType` enum (Streaminfo…Picture, `MaxStandardType`),
  `FlacSrsBlockType` (Srsf/Srst/Fingerprint), `FlacConstants` (Marker, MarkerSize=4, BlockHeaderSize=4,
  BlockSizeFieldWidth=3, LastBlockFlag=0x80, BlockTypeMask=0x7F, MaxSrsBlockCount=3); delete the
  `FlacMetadataReader.Id3v2HeaderSize` dup; new `Mp3Constants` (Id3v2Magic, Id3v1MagicSize=3,
  Lyrics3BeginMagicSize=11, SyncByte0=0xFF, SyncMask1=0xE0) + MP3TagReader-local sub-field/marker
  lengths + `SyncSafeByteMask=0x7F` + `ApeV2Version=2000`; `ISOMediaExtractor.VobTitlePrefixLength=6`.

## Overloaded literals — name by INTENT (the review lens)

The inventory §4 lists every site. The highest-risk:
- **MKV lacing 2-bit field (O-06/03) — behaviour-sensitive, T3:** `flags & 0x06` (→0/2/4/6 =
  `EBMLLaceType`) vs `(flags>>1)&0x03` (→0/1/2/3) vs raw `==1`/`==3`. Three encodings of the SAME field
  coexist; the refactor must normalise them onto `EBMLLaceType` WITHOUT changing which lace type any
  input maps to. Requires per-lacing-type characterization tests first (see verification).
- **`8` (O-8):** SRS block header (tag+LE size) `SrsBlockLayout.HeaderSize` vs MP4 atom header
  `Mp4AtomTypes.AtomHeaderSize` vs RIFF/AVI chunk header `RiffFourCC.ChunkHeaderSize` vs ASF size-field
  width vs EBML VINT max byte width `EBMLVInt.MaxByteWidth` vs bits-per-byte shift `*8` (OUT OF SCOPE).
- **`0x80` (O-0x80):** EBML VINT marker `EBMLVInt.Marker1` vs VINT length-probe `0x80>>i` (keep
  computed) vs FLAC last-block `FlacConstants.LastBlockFlag` vs MKV ASCII boundary `AsciiBoundary`.
- **`0xFF` (O-0xFF):** Xiph continuation `EBMLLacing.XiphContinuation` (protocol) vs data-bit strip
  mask `0xFF>>vintLen` (inline) vs byte fill/extract in VINT emit (inline) vs MP3 sync byte0
  `Mp3Constants.SyncByte0`.
- **`16` (O-16):** ASF GUID width `AsfGuids.GuidSize` vs ASF data-object fileId width
  `AsfGuids.DataObjectFileIdSize` vs a magic-detect read clamp (out of scope).
- Also `3`, `4`, `9`, `6`, `10`, `1`, and the 64 KiB trio (`0x10000` search window / `64*1024` I/O
  buffer / `65536` SRST threshold — only the last is a domain const; keep the three SEPARATE).

## Byte-exact verification (zero behaviour change)

- Every named constant equals the literal it replaces; the per-task reviewer verifies value AND intent
  at each overloaded site.
- The existing SRS suites are the safety net — all green, NO new failures. Per-format round-trip/parse
  tests (MKV/MP4/AVI/WMV/FLAC/MP3/Stream create + rebuild, SRSFile parse, SRSPayloadSerializer) must
  stay green.
- **Verification blind spots — pin BEFORE renaming (this phase's Task-1-style pins):**
  - **T3 MKV lacing** — before normalising the three idioms, add characterization tests that drive an
    MKV sample of EACH lacing type (None/Xiph/Fixed/EBML) through the affected read/rebuild paths and
    assert the exact reconstructed bytes, so a wrong `EBMLLaceType` mapping fails loudly.
  - **AVI / MP4 / WMV rebuild paths** — these are documented KNOWN LIMITATIONS
    (`docs/known-limitations.md`, audit #12/#13/#14): the rebuilders do not byte-exactly reconstruct
    pyrescene samples of those formats, and may be under-tested. For any rename site in
    `AVIContainerRebuilder`/`MP4ContainerRebuilder`/`WMVContainerRebuilder`, confirm a covering test
    exists (create-then-rebuild round-trip over THIS codebase's own SRS); if not, add a characterization
    test before renaming — a value-preserving rename can't change bytes, but the pin guards intent and
    documents coverage. Map each rename site to a covering test during planning.
- 0-warning build (both TFMs, `-p:BaseOutputPath=bin2/ --no-incremental`).

## Naming conventions

- Follow Phases 1/2: per-format id/layout `internal static class`es (`Mp4AtomTypes`, `RiffFourCC`,
  `AsfGuids`, `FlacConstants`, `SrsBlockLayout`, `SrstLayout`, `Mp3Constants`, …); `[Flags]` enums for
  `SrstFlags`/`SrsfFlags`; `enum FlacBlockType`. FourCCs/GUIDs as `ReadOnlySpan<byte>` properties or
  `static readonly byte[]`/`u8` literals matching existing style. Reuse existing (`EBMLIds`,
  `EBMLLaceType`, `EBMLVInt`, `TrackInfo.SignatureSize`, `MP3TagReader` privates) — no local
  duplicates of an existing value.

## File structure

- Create: per-format constant files under `ReScene/SRS/` (e.g. `Mp4AtomTypes.cs`, `RiffFourCC.cs`,
  `StreamFourCC.cs`, `AsfGuids.cs`, `AsfSrsGuids.cs`, `FlacBlockType.cs`, `FlacSrsBlockType.cs`,
  `FlacConstants.cs`, `SrsFourCC.cs`, `Mp3Constants.cs`, `SrsBlockLayout.cs`, `MkvBlockLayout.cs`,
  `SrstLayout.cs`, `SrsConstants.cs`, `SrstFlags.cs`, `SrsfFlags.cs`, `MkvBlockFlags.cs`). Group related
  ones per file where natural (e.g. FLAC constants together).
- Extend: `EBMLWriter.cs` (`EBMLIds`), `EBMLLacing.cs` (`EBMLVInt`, `XiphContinuation`).
- Modify: the handlers/rebuilders/SRS-core files at the inventory's adoption sites.
- Remove duplicates: `EBMLHeaderStripping` id dups, `FlacMetadataReader.Id3v2HeaderSize`, the 3-way ASF
  GUID fields, the second `26` const.
- Tests: existing suites pin behaviour; add the MKV-lacing and any missing AVI/MP4/WMV-rebuild
  characterization tests (identified in planning) before their renames.

## Success criteria

- SRS-namespace domain literals (element IDs, FourCCs, atom types, GUIDs, block-types, framing
  offsets/sizes, signature sizes, flags, thresholds, tag sizes) are named; a `git grep` of the replaced
  literals in `SRS/**/*.cs` returns only definitions + the documented out-of-scope set (§5).
- Zero behavioural change: all suites green (no new failures), 0 warnings.
- Exactly one definition per constant (all listed duplicates consolidated).
- Overloaded literals (`8`/`0x80`/`0xFF`/`16`/`3`/`4`/`9`/lacing) disambiguated by intent at every
  site; the MKV lacing normalisation preserves every lace-type decision (proved by the T3 pins).
