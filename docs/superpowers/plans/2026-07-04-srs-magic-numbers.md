# SRS Magic-Number Elimination (Phase 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the domain numeric literals in `ReScene.Lib/ReScene/SRS/**/*.cs` with named constants — a behaviour-preserving refactor (zero output-byte change), replaying the Phase-1/2 recipe across seven container formats.

**Architecture:** Adopt existing infrastructure (`EBMLIds`, `EBMLLaceType`, `EBMLVInt`, `SRSContainerType`, `TrackInfo.SignatureSize`, the `MP3TagReader` tag-size privates); consolidate duplicated consts into per-format id/layout classes; name the rest. Every substitution is value-identical; the existing SRS suites are the safety net.

**Tech Stack:** .NET (net8.0;net10.0 lib), xUnit. **Spec:** `docs/superpowers/specs/2026-07-04-srs-magic-numbers-design.md`. **Inventory (exhaustive per-site source):** `docs/superpowers/specs/2026-07-04-srs-magic-numbers-inventory.md` — each task adopts its concern's constants at ALL sites the inventory lists; line numbers are approximate anchors, re-locate by value+context.

## Global Constraints

- **Behaviour-preserving:** every named constant MUST equal the literal it replaces; no logic changes. Name by INTENT at each overloaded site.
- **Overloaded literals** (inventory §4 — choose the right name per site):
  - **MKV lacing 2-bit field** (`flags & 0x06` / `(flags>>1)&0x03` / raw `==1`/`==3`) → normalise onto `EBMLLaceType` (None=0, Xiph=2, Fixed=4, EBML=6). **The extraction AND the comparison constant must change TOGETHER at each idiom-B/C site** (0→None, 1→Xiph, 2→Fixed, 3→EBML); leaving a `==1` behind while switching extraction to `flags & 0x06` makes Xiph silently never match. This is Task 3, pinned first.
  - **`8`:** `SrsBlockLayout.HeaderSize` (SRS tag+LE size) vs `Mp4AtomTypes.AtomHeaderSize` vs `RiffFourCC.ChunkHeaderSize` vs ASF size-field width vs `EBMLVInt.MaxByteWidth` (only `EBMLWriter.cs:84` + `EBMLReader.cs:23/29/66/71`) vs bits-per-byte shift `*8` (LEAVE raw, incl. `EBMLWriter.cs:88`).
  - **`0x80`:** `EBMLVInt.Marker1` (encode) vs `0x80>>i` VINT probe (leave computed) vs `FlacConstants.LastBlockFlag` vs `MKVContainerHandler.AsciiBoundary`.
  - **`0xFF`:** `EBMLLacing.XiphContinuation` (protocol) vs `0xFF>>vintLen` strip mask (inline) vs VINT emit byte fill (inline) vs `Mp3Constants.SyncByte0`.
  - **`16`:** `AsfGuids.GuidSize` vs `AsfGuids.DataObjectFileIdSize` vs a magic-detect read clamp (leave raw).
  - Also `3`/`4`/`9`/`6`/`10`/`1` per inventory §4; the 64 KiB trio stays THREE separate consts.
- **Scope:** `ReScene.Lib/ReScene/SRS/**/*.cs` only. NOT other namespaces. Out-of-scope literals: inventory §5 (trivial ints, buffer sizes, shifts, display strings).
- **Build/test ONLY with `-p:BaseOutputPath=bin2/`.** NEVER kill the app. After verifying, delete bin2: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- **No-warning build:** `dotnet build ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental` → 0 warnings / 0 errors.
- **Verification per task = existing suites stay green, NO new failures:** `dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/`. Baseline at branch start: 1198 pass. The per-format round-trip suites (`SRSRebuilderTests` — MKV/MP4/AVI/WMV/FLAC/MP3/Stream own-SRS create+rebuild, incl. multi-track and output-matches-original), `EBMLLacingTests`, `MP3TagReader`/`FlacMetadataReader` parse tests prove no output byte changed.
- **CRITICAL repo:** nested submodule `E:/Projects/ReScene.NET/ReScene.Lib` (path contains `ReScene.NET`). The decoy `E:/Projects/ReScene.Lib` must NEVER be touched. Commit on lib `main`.
- Commit trailer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. App branch: `refactor/magic-numbers-srs`; gitlink bumped at the end.

## File structure

- Create per-format constant files under `ReScene/SRS/`: `Mp4AtomTypes.cs`, `RiffFourCC.cs`, `StreamFourCC.cs`, `AsfGuids.cs`, `AsfSrsGuids.cs`, `FlacConstants.cs` (+ `FlacBlockType`/`FlacSrsBlockType` enums), `SrsFourCC.cs`, `Mp3Constants.cs`, `SrsBlockLayout.cs`, `MkvBlockLayout.cs`, `SrstLayout.cs`, `SrsConstants.cs`, `SrstFlags.cs`, `SrsfFlags.cs`, `MkvBlockFlags.cs`. Group naturally (FLAC/MP3 consts per file).
- Extend: `EBMLWriter.cs` (`EBMLIds`), `EBMLLacing.cs` (`EBMLVInt`, `XiphContinuation`).
- Modify: handlers/rebuilders/SRS-core at the inventory's adoption sites.
- Remove duplicates: `EBMLHeaderStripping` id dups, `FlacMetadataReader.Id3v2HeaderSize`, the 3-way ASF GUID fields, the second `26` const.

---

### Task 1: EBML ID consolidation (pure dedup)

Adopt/extend `EBMLIds`; delete the duplicates. Inventory §1a/§1b/§1c + §2.

**Files:** `EBMLWriter.cs` (extend `EBMLIds`), `EBMLHeaderStripping.cs` (delete `:11-15` dups), `Handlers/MKVContainerHandler.cs`, `Rebuilders/MKVContainerRebuilder.cs`, `SRSFile.cs` (GetEBMLElementName switch).

- [ ] **Step 1: Add the new `EBMLIds` members** (values from inventory §1b/§1c): `FileData=0x465C`, `FileName=0x466E`, `FileMimeType=0x4660`, `Timestamp=0xE7`, `PrevSize=0xAB`, `Position=0xA7`, `CRC32Element=0xBF`, `Void=0xEC`, `TrackUID=0x73C5`, `TrackType=0x83`, `CodecID=0x86`, `BlockDuration=0x9B`, `ReferenceBlock=0xFB`; the §1c display IDs (`EBMLVersion=0x4286`, `EBMLReadVersion=0x42F7`, `EBMLMaxIDLength=0x42F2`, `EBMLMaxSizeLength=0x42F3`, `DocType=0x4282`, `DocTypeVersion=0x4287`, `DocTypeReadVersion=0x4285`); and `ContentCompAlgoHeaderStripping=3`. Match the existing `EBMLIds` member style (`const ulong`).

- [ ] **Step 2: Delete the 5 `EBMLHeaderStripping` private-const dups** (`:11-15` — `IdContentEncodings` etc.) and re-point their uses at `EBMLIds.ContentEncodings/ContentEncoding/ContentCompression/ContentCompAlgo/ContentCompSettings`.

- [ ] **Step 3: Adopt `EBMLIds` at the raw EBML-ID sites** in `MKVContainerHandler`, `MKVContainerRebuilder` (`IsKnownMKVElementId`, the `0x465C`/`0x466E`/etc. reads), and `SRSFile.GetEBMLElementName` — per inventory §1a/§1b. **CAUTION:** `MKVContainerHandler._mKVSrsContainers` is a DISTINCT 4-element set {Cluster, BlockGroup, Attachments, AttachedFile}; RENAME its 4 hex literals to `EBMLIds.*` in place and KEEP the set — do NOT replace it with `EBMLIds.IsContainer` (a different, 10-element set).

- [ ] **Step 4: Build + full lib suite green (no new failures).** The MKV own-SRS round-trip (`SRSRebuilderTests`) + EBML tests guard this.

- [ ] **Step 5: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRS/
git commit -m "refactor(srs): consolidate EBML element IDs onto EBMLIds; drop dups"
```

---

### Task 2: EBML VINT / writer internals

Extend `EBMLVInt` + `EBMLIds` bounds + `XiphContinuation`. Inventory §4 (VINT masks), §5 (tier thresholds).

**Files:** `EBMLLacing.cs` (`EBMLVInt`), `EBMLWriter.cs`, `EBMLReader.cs`.

- [ ] **Step 1: Extend `EBMLVInt`** with: tier limits `OneByteSizeLimit=0x7F`, `TwoByteSizeLimit=0x3FFF`, `ThreeByteSizeLimit=0x1FFFFF`, `FourByteSizeLimit=0x0FFFFFFF`, `FiveByteSizeMax=0x07FFFFFFFF`; markers `Marker1=0x80, Marker2=0x40, Marker3=0x20, Marker4=0x10`; `FiveByteMinWidth=5`; `MaxByteWidth=8`. Add to `EBMLIds`: ID byte-width bounds `OneByteBound=0x100`, `TwoByteBound=0x10000`, `ThreeByteBound=0x1000000`. Add `EBMLLacing.XiphContinuation=0xFF`.

- [ ] **Step 2: Adopt them** at `EBMLWriter.MakeEBMLUInt`/`MakeEBMLId` (tier limits, markers, ID bounds; `:60-115`), `EBMLReader` (`MaxByteWidth` at `:23/29/66/71`), `EBMLLacing`/`MKVContainerRebuilder` (`XiphContinuation` — the `255`/`0xFF` at `EBMLLacing:98`, `Rebuilder:325`). **Leave raw:** `MaxByteWidth` sites are `EBMLWriter.cs:84` + `EBMLReader` ONLY — the `8` at `EBMLWriter.cs:88` and all `*8`/`>>8`/`0x80>>i`/`0xFF>>vintLen`/`<<21`/`<<14`/`<<7` shifts stay literal (inventory §5, O-08bits, O-0x80, O-0xFF).

- [ ] **Step 3: Build + full lib suite green.** MKV round-trip + any EBML writer/reader unit tests guard this. (If `MakeEBMLUInt`/`MakeEBMLId`/`ReadUnsigned` lack direct unit tests, they are exercised by the MKV round-trip; if a rename here is not round-trip-covered, add a small known-vector test first.)

- [ ] **Step 4: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRS/
git commit -m "refactor(srs): name EBML VINT tier limits, markers, and byte bounds"
```

---

### Task 3: MKV block-flags lacing normalisation (BEHAVIOUR-SENSITIVE — pin first)

Reconcile the three lacing idioms onto `EBMLLaceType`, byte-exact. Inventory §4 (O-06/03), §2.

**Files:** Test: `ReScene.Lib/ReScene.Tests/SRSRebuilderTests.cs` (or the MKV rebuild test file). Then `Rebuilders/MKVContainerRebuilder.cs`, `Handlers/MKVContainerHandler.cs`, `EBMLLacing.cs`, `TrackInfo.cs`.

- [ ] **Step 1: PIN — add Fixed- and EBML-lacing rebuild round-trips.** `MKVContainerRebuilder.ReadLacingHeaderSize` (`:294`, idioms B/C) is only round-tripped for Xiph today (`Rebuild_MKVWithXiphLacing_RoundTrip_ByteMatch`). Mirror that test for **Fixed** and **EBML** lacing: build an MKV sample using each lacing type, create-then-rebuild, and `Assert` the rebuilt bytes equal the original. Run them — they must PASS against current behaviour.

- [ ] **Step 2: Verify the pins fail if the mapping is wrong (optional sanity).** These pins are the guard; proceed only once all three lacing round-trips (Xiph existing + new Fixed/EBML) pass.

- [ ] **Step 3: Normalise the idioms onto `EBMLLaceType`.** At each site, change extraction AND comparison TOGETHER: `flags & 0x06` → `(EBMLLaceType)(flags & MkvBlockFlags.LacingMask)` (add `MkvBlockFlags.LacingMask=0x06`); `(flags>>1)&0x03` sites → the same `EBMLLaceType` extraction, and their `==0/1/2/3` tests → `== EBMLLaceType.None/Xiph/Fixed/EBML` (0→None, 1→Xiph, 2→Fixed, 3→EBML). Verify `ReadLacingHeaderSize`'s `if (laceType==0)` → `== EBMLLaceType.None`, `==1`(Xiph), `==3`(EBML), the Fixed(2) fall-through, and `RebuildEBMLFromSRS`'s `laceType != 0` → `!= EBMLLaceType.None` all preserve the exact decision.

- [ ] **Step 4: Add the remaining MKV consts + adopt.** `MkvBlockLayout.FixedHeaderOverhead=3` (2 timecode + 1 flags; Handler `:175/414`, Rebuilder `:197/525/772`), `MKVContainerHandler.SignatureAsciiWindowSize=64` (`:517`), `MKVContainerHandler.AsciiBoundary=0x80` (`:555`), `MKVContainerRebuilder.PreTrackSkipMargin=4096` (`:595`), `TrackInfo.CompressionAlgoUnknown=-1` (Handler `:159`).

- [ ] **Step 5: Build + full lib suite green (no new failures).** ALL lacing round-trips (Xiph/Fixed/EBML from Step 1) + the MKV signature/rebuild tests are the guards — they MUST stay green.

- [ ] **Step 6: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRS/ ReScene.Lib/ReScene.Tests/
git commit -m "refactor(srs): normalise MKV lacing onto EBMLLaceType (pinned); name MKV layout consts"
```

---

### Task 4: SRS block framing + FourCCs + flags (cross-format core)

Inventory §1f, §2 (framing `8`), §3 (threshold), §4 (SRST/SRSF flags, BigFileSizeThreshold).

**Files:** Create `SrsBlockLayout.cs`, `SrsFourCC.cs`, `SrstFlags.cs`, `SrsfFlags.cs`, `SrstLayout.cs`, `SrsConstants.cs`. Modify `SRSFile.cs`, `SRSWriter.cs`, `SRSPayloadSerializer.cs`, and ALL 7 handlers (bigFile predicate).

- [ ] **Step 1: Create the constant homes.**

```csharp
internal static class SrsBlockLayout { public const int HeaderSize = 8; }          // 4-byte tag + 4-byte LE size
internal static class SrstLayout { public const int TrackNumberWidthThreshold = 0x10000; }
internal static class SrsConstants { public const long BigFileSizeThreshold = 0x80000000L; }  // 2 GiB
[Flags] internal enum SrstFlags { None = 0, BigFile = 0x4, BigTrackNumber = 0x8 }
[Flags] internal enum SrsfFlags { None = 0, SimpleBlockFix = 0x1, AttachmentsRemoved = 0x2 } // write-only
internal static class SrsFourCC { /* SrsFile "SRSF", SrsTrack "SRST", SrsPadding "SRSP", Strm "STRM" — match existing byte[]/u8 style */ }
```

- [ ] **Step 2: Adopt `SrsBlockLayout.HeaderSize`** at the `8`/`4+4` SRS-block-header sites (inventory §2 first `8` row: `SRSFile`, `SRSPayloadSerializer:120/135`, `StreamHandler:72`, `MP3Rebuilder:49`) — NOT the MP4/RIFF/ASF `8`s (those are Tasks 5/6/7).

- [ ] **Step 3: Adopt the flags + threshold + FourCCs.** SRST flags `0x8`/`0x4` (`SRSFile:243/258` read, `SRSPayloadSerializer:62/58` write) → `SrstFlags`; SRSF `0x0003` (`SRSPayloadSerializer:26`) → `SrsfFlags.SimpleBlockFix | AttachmentsRemoved`; `0x10000`/`65536` track-num threshold (`SRSPayloadSerializer:54`) → `SrstLayout.TrackNumberWidthThreshold`; `"SRSF"`/`"SRST"`/`"SRSP"`/`"STRM"` FourCCs (inventory §1f) → `SrsFourCC.*`.

- [ ] **Step 4: Replace ALL SEVEN `BigFileSizeThreshold` copies** with `SrsConstants.BigFileSizeThreshold`: `FlacContainerHandler.cs:149`, `MKVContainerHandler.cs:474`, `MP3ContainerHandler.cs:93`, `AVIContainerHandler.cs:206`, `WMVContainerHandler.cs:181`, `MP4ContainerHandler.cs:92`, `StreamContainerHandler.cs:81`. Each compares against a `long sampleSize` → `long >= long`, value-identical.

- [ ] **Step 5: Build + full lib suite green (no new failures).** SRS round-trips (all formats) + `SRSPayloadSerializer` tests guard this.

- [ ] **Step 6: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRS/
git commit -m "refactor(srs): add SrsBlockLayout/SrsFourCC/SrstFlags/SrsConstants; unify big-file threshold"
```

---

### Task 5: MP4 atom layout

Inventory §1d (ftyp), §2 (MP4 atom headers, sentinels, tkhd).

**Files:** Create `Mp4AtomTypes.cs`. Modify `Handlers/MP4ContainerHandler.cs`, `Rebuilders/MP4ContainerRebuilder.cs`, `SRSFile.cs` (MP4 branch).

- [ ] **Step 1: Create `Mp4AtomTypes`.**

```csharp
internal static class Mp4AtomTypes
{
    public const int AtomHeaderSize = 8;          // 4-byte BE size + 4-byte type
    public const int AtomExtendedHeaderSize = 16; // 8 + 8 (u64 size)
    public const int ExtendedSizeSentinel = 1;    // size32==1 → u64 follows
    public const int ToEndSentinel = 0;           // size32==0 → runs to EOF
    public const int TkhdTrackIdOffsetV0 = 12;
    public const int TkhdTrackIdOffsetV1 = 20;
    public const int TkhdTrackIdFieldSize = 4;
    // Ftyp "ftyp" FourCC — match existing byte[]/u8 style.
}
```

- [ ] **Step 2: Adopt** at inventory §1d (`ftyp` at `SRSFile:133`, `SRSWriter:287`) and §2 MP4 rows (`AtomHeaderSize` at `MP4Handler:69/133/161/...`, `MP4Rebuilder:42/55`, `SRSFile:660`; `AtomExtendedHeaderSize`/`ExtendedSizeSentinel`/`ToEndSentinel`; tkhd offsets/field at `MP4Handler:263/271/275`). These `8`/`16` are MP4-atom, distinct from SRS/RIFF/ASF `8`/`16`.

- [ ] **Step 3: Build + full lib suite green.** MP4 round-trip (`SRSRebuilderTests:425`) guards this.

- [ ] **Step 4: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRS/
git commit -m "refactor(srs): add Mp4AtomTypes for MP4 atom framing"
```

---

### Task 6: RIFF/AVI + Stream framing

Inventory §1d, §2 (RIFF chunk header, Stream FourCCs).

**Files:** Create `RiffFourCC.cs`, `StreamFourCC.cs`. Modify `Handlers/AVIContainerHandler.cs`, `Rebuilders/AVIContainerRebuilder.cs`, `Handlers/StreamContainerHandler.cs`, `SRSFile.cs`.

- [ ] **Step 1: Create the FourCC classes.** `RiffFourCC` (`Riff` "RIFF", `ChunkHeaderSize=8`, `SizeOffset=4`); `StreamFourCC` (`Strm` "STRM", `M2ts` "M2TS"). Match existing FourCC byte style.

- [ ] **Step 2: Adopt** at inventory §1d/§2 RIFF+Stream rows (`RIFF` at `SRSFile:109`/`SRSWriter:269`; `ChunkHeaderSize=8` at `AVIHandler:83-87/183-186`, `AVIRebuilder:27/155/...`; `SizeOffset=4` at `AVIHandler:90`/`AVIRebuilder:91`; STRM/M2TS at `SRSFile:117/119`). Also adopt `SrsBlockLayout.HeaderSize` where the Stream SRS block header `8` appears (`StreamHandler`).

- [ ] **Step 3: Build + full lib suite green.** AVI + Stream round-trips guard this.

- [ ] **Step 4: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRS/
git commit -m "refactor(srs): add RiffFourCC/StreamFourCC for RIFF/AVI + Stream framing"
```

---

### Task 7: ASF/WMV GUIDs + object framing

Inventory §1d/§1g, §2 (ASF object header, GUID sizes, the two `26` consts).

**Files:** Create `AsfGuids.cs`, `AsfSrsGuids.cs`. Modify `Handlers/WMVContainerHandler.cs`, `Rebuilders/WMVContainerRebuilder.cs`, `SRSFile.cs`.

- [ ] **Step 1: Create the ASF constant homes.** `AsfGuids` (`HeaderObjectPrefix` `30 26 B2 75`, `DataObjectPrefix` `36 26 B2 75`, `ObjectHeaderSize=24`, `GuidSize=16`, `DataObjectFileIdSize=16`, `DataObjectHeaderLength=26`). `AsfSrsGuids` (`GuidSRSFile` "SRSFSRSFSRSFSRSF", `GuidSRSTrack` "SRSTSRSTSRSTSRST", `GuidSRSPadding` "PADDINGBYTESDATA" — consolidate the 3-way dup at `SRSFile:752-754`, `WMVHandler:9-10`, `WMVRebuilder:13-15`).

- [ ] **Step 2: Adopt + consolidate.** Replace the raw ASF GUIDs/prefixes + the `24`/`16` object-header sizes (inventory §2 ASF rows) with `AsfGuids.*`; point all three GUID field sets at `AsfSrsGuids`; DELETE the duplicated `WMVContainerRebuilder.DataObjectHeaderLength=26` and `SRSFile.AsfDataObjectHeaderLength=26` in favour of ONE `AsfGuids.DataObjectHeaderLength`. Watch O-16: `GuidSize` (GUID width) vs `DataObjectFileIdSize` (fileId width at `WMVHandler:62`) are distinct constants of the same value. Name the WMV virtual track number (`WMVHandler:75`).

- [ ] **Step 3: Build + full lib suite green.** WMV round-trips (`SRSRebuilderTests:206/226`, incl. output-matches-original) guard this.

- [ ] **Step 4: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRS/
git commit -m "refactor(srs): add AsfGuids/AsfSrsGuids; consolidate ASF GUID + object-header consts"
```

---

### Task 8: FLAC + MP3/ID3 tag constants

Inventory §1a-Flac, §1e/§1f, §1h, §2 (tag sizes), §4 (FLAC/MP3 masks), §5.

**Files:** Create `FlacConstants.cs` (+ `FlacBlockType`/`FlacSrsBlockType` enums), `Mp3Constants.cs`. Modify `Handlers/FlacContainerHandler.cs`, `Rebuilders/FlacContainerRebuilder.cs`, `FlacMetadataReader.cs`, `MP3TagReader.cs`, `SRSFile.cs`, `SRSWriter.cs`, `ISOMediaExtractor.cs`.

- [ ] **Step 1: Create the FLAC + MP3 homes.**

```csharp
internal enum FlacBlockType { Streaminfo=0, Padding=1, Application=2, Seektable=3, VorbisComment=4, Cuesheet=5, Picture=6 }
internal enum FlacSrsBlockType : byte { Srsf=0x73, Srst=0x74, Fingerprint=0x75 }
internal static class FlacConstants
{
    public const int MarkerSize = 4; public const int BlockHeaderSize = 4; public const int BlockSizeFieldWidth = 3;
    public const byte LastBlockFlag = 0x80; public const byte BlockTypeMask = 0x7F;
    public const int MaxStandardType = 6; public const int MaxSrsBlockCount = 3;
    // Marker "fLaC" — match existing style.
}
internal static class Mp3Constants
{
    public const int Id3v1MagicSize = 3; public const int Lyrics3BeginMagicSize = 11;
    public const byte SyncByte0 = 0xFF; public const byte SyncMask1 = 0xE0;
    // Id3v2Magic "ID3" — match existing style.
}
```

- [ ] **Step 2: Adopt FLAC.** Block types (`FlacMetadataReader:143-149`) → `FlacBlockType`; SRS block bytes `0x73`/`0x74`/`0x75` → `FlacSrsBlockType`; `fLaC`/`MarkerSize=4`/`BlockHeaderSize=4`/`BlockSizeFieldWidth=3`/`LastBlockFlag=0x80`/`BlockTypeMask=0x7F`/`MaxStandardType=6`/`MaxSrsBlockCount=3` per inventory. **DELETE** `FlacMetadataReader.Id3v2HeaderSize` (dup) → use `MP3TagReader.Id3v2HeaderSize`.

- [ ] **Step 3: Adopt MP3/ID3.** `Mp3Constants` (`ID3` magic, `Id3v1MagicSize=3`, `Lyrics3BeginMagicSize=11`, `SyncByte0`/`SyncMask1`) at `SRSFile`/`SRSWriter`; adopt existing `MP3TagReader` privates + ADD `MP3TagReader` locals `Lyrics3v2SizeFieldLength=6`, `Lyrics3v2MarkerLength=9`, `Lyrics3v1EndMarkerLength=9` (distinct strings — O-9), `SyncSafeByteMask=0x7F` (`:356-359`), `ApeV2Version=2000` (`:317`). `ISOMediaExtractor.VobTitlePrefixLength=6` (`:388`).

- [ ] **Step 4: Build + full lib suite green.** FLAC + MP3 tag-parse round-trips guard this.

- [ ] **Step 5: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRS/
git commit -m "refactor(srs): add FlacConstants/Mp3Constants; name FLAC + MP3/ID3 tag literals"
```

---

## Final verification (after all tasks)

- [ ] No-warning build (both TFMs): `dotnet build ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental` → 0/0.
- [ ] Full lib suite green, no new failures vs the branch-start baseline (1198 + Task-3 lacing pins).
- [ ] App suite green (`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/`).
- [ ] `git grep` the replaced literals in `SRS/**/*.cs` returns only definitions + the documented out-of-scope set (inventory §5).
- [ ] Delete bin2 dirs. Bump the app gitlink on `refactor/magic-numbers-srs`. Whole-branch reviewer confirms every commit is value-preserving, the MKV lacing normalisation preserved every lace-type decision, all 7 BigFileSizeThreshold copies replaced, and each overloaded `8`/`0x80`/`0xFF`/`16` got the intent-correct name.
