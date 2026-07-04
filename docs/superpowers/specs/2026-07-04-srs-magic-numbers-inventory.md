# SRS Namespace — Unified Magic-Number Inventory

Synthesis of five per-format audits into one refactor-driving inventory. Goal: replace
magic numbers with named constants, **byte-exact**, following the established recipe:
**(1) adopt existing enums/consts → (2) consolidate duplicated consts into layout/format
classes → (3) name the rest.**

Source groups merged: `ebml-core` (EBMLWriter/EBMLHeaderStripping/EBMLLacing/EBMLReader),
`mkv-handler-rebuilder` (MKVContainerHandler/Rebuilder), `srs-core`
(SRSFile/SRSWriter/SRSPayloadSerializer/TrackInfo/SRSRebuilder/SignatureScanner), `flac`
(FlacContainerHandler/Rebuilder/FlacMetadataReader), `wmv-asf`
(WMVContainerHandler/Rebuilder), `mp3-mp4-avi-stream`
(MP3TagReader/MP3·MP4·AVI·StreamContainerHandler+Rebuilder/ISOMediaExtractor).

Line numbers are as reported by each source audit and are approximate anchors, not
guaranteed to survive edits — re-locate by value+context before editing.

---

## 1. By Category

### Category 1 — Container Magic IDs / EBML Element IDs / RIFF FourCCs / MP4 Atom Types / ASF GUIDs / FLAC Block Types / MP3 Tag Magic

#### 1a. EBML element IDs (MKV) — already centralised in `EBMLIds`, adopt everywhere

`EBMLIds` (in EBMLWriter.cs) already defines these 24 `const ulong`s. Every raw
occurrence below should ADOPT the named member; the private duplicates should be deleted.

| Value | EBMLIds member | Raw-literal sites to replace (ADOPT) |
|---|---|---|
| `0x1A45DFA3` | `EBML` | EBMLWriter:8 (def); SRSFile:139 + SRSWriter:281 (byte-split detection); SRSFile:GetEBMLElementName; Rebuilder(mkv):IsKnownMKVElementId |
| `0x18538067` | `Segment` | EBMLWriter:9; Handler(mkv):164,380; Rebuilder(mkv):IsKnown; SRSFile:900,1051 |
| `0x114D9B74` | `SeekHead` | EBMLWriter:10; Rebuilder(mkv):IsKnown; SRSFile:GetName |
| `0x1549A966` | `Info` | EBMLWriter:11; Rebuilder(mkv):IsKnown; SRSFile:GetName |
| `0x1F43B675` | `Cluster` | EBMLWriter:12; Handler(mkv)fields:31; Rebuilder(mkv):399,IsKnown; SRSFile:GetName |
| `0x1654AE6B` | `Tracks` | EBMLWriter:13; Rebuilder(mkv):IsKnown; SRSFile:GetName |
| `0xAE` | `TrackEntry` | EBMLWriter:14; Rebuilder(mkv):IsKnown |
| `0xD7` | `TrackNumber` | EBMLWriter:15; Handler(mkv)fields:22; Rebuilder(mkv):IsKnown/912 |
| `0x6D80` | `ContentEncodings` | EBMLWriter:16; EBMLHeaderStripping:11 (private dup — DELETE); Rebuilder(mkv):IsKnown |
| `0x6240` | `ContentEncoding` | EBMLWriter:17; EBMLHeaderStripping:12 (dup — DELETE); Rebuilder(mkv):IsKnown |
| `0x5034` | `ContentCompression` | EBMLWriter:18; EBMLHeaderStripping:13 (dup — DELETE); Handler(mkv):156; Rebuilder(mkv):IsKnown |
| `0x4254` | `ContentCompAlgo` | EBMLWriter:19; EBMLHeaderStripping:14 (dup — DELETE); Handler(mkv)fields:23 |
| `0x4255` | `ContentCompSettings` | EBMLWriter:20; EBMLHeaderStripping:15 (dup — DELETE); Handler(mkv)fields:24 |
| `0xA0` | `BlockGroup` | EBMLWriter:21; Handler(mkv)fields:32; Rebuilder(mkv):IsKnown |
| `0xA1` | `Block` | EBMLWriter:22; Handler(mkv)fields:21; Rebuilder(mkv):189,520,766,IsKnown |
| `0xA3` | `SimpleBlock` | EBMLWriter:23; Handler(mkv)fields:20; Rebuilder(mkv):189,520,766,IsKnown |
| `0x1941A469` | `Attachments` | EBMLWriter:24; Handler(mkv)fields:33; Rebuilder(mkv):IsKnown; SRSFile:GetName |
| `0x61A7` | `AttachedFile` | EBMLWriter:25; Handler(mkv)fields:34; Rebuilder(mkv):IsKnown |
| `0x1C53BB6B` | `Cues` | EBMLWriter:26; Rebuilder(mkv):IsKnown; SRSFile:GetName |
| `0x1043A770` | `Chapters` | EBMLWriter:27; Rebuilder(mkv):IsKnown; SRSFile:GetName |
| `0x1254C367` | `Tags` | EBMLWriter:28; Rebuilder(mkv):IsKnown; SRSFile:GetName |
| `0x1F697576` | `ReSampleContainer` | EBMLWriter:29; Rebuilder(mkv):725,IsKnown; SRSFile:884,961,1060 |
| `0x6A75` | `ResampleFile` (SRSF) | EBMLWriter:30; Rebuilder(mkv):725,IsKnown; SRSFile:1016 |
| `0x6B75` | `ResampleTrack` (SRST) | EBMLWriter:31; Rebuilder(mkv):725,IsKnown; SRSFile:1020 |

#### 1b. EBML element IDs — NEW members to add to `EBMLIds` (currently raw)

| Value | MKV element | Raw-literal sites | Suggested name |
|---|---|---|---|
| `0x465C` | FileData / AttachedFileData | Handler(mkv):400; Rebuilder(mkv):420,749,911 | `EBMLIds.FileData` |
| `0x466E` | FileName | Rebuilder(mkv):411,909 | `EBMLIds.FileName` |
| `0x4660` | FileMimeType | Rebuilder(mkv):910 | `EBMLIds.FileMimeType` |
| `0xE7` | Timestamp | Rebuilder(mkv):903 | `EBMLIds.Timestamp` |
| `0xAB` | PrevSize | Rebuilder(mkv):904 | `EBMLIds.PrevSize` |
| `0xA7` | Position | Rebuilder(mkv):905 | `EBMLIds.Position` |
| `0xBF` | CRC-32 (element-level) | Rebuilder(mkv):906 | `EBMLIds.CRC32Element` |
| `0xEC` | Void (padding) | Rebuilder(mkv):907 | `EBMLIds.Void` |
| `0x73C5` | TrackUID | Rebuilder(mkv):913 | `EBMLIds.TrackUID` |
| `0x83` | TrackType | Rebuilder(mkv):914 | `EBMLIds.TrackType` |
| `0x86` | CodecID | Rebuilder(mkv):915 | `EBMLIds.CodecID` |
| `0x9B` | BlockDuration | Rebuilder(mkv):919 | `EBMLIds.BlockDuration` |
| `0xFB` | ReferenceBlock | Rebuilder(mkv):920 | `EBMLIds.ReferenceBlock` |

#### 1c. EBML element IDs — display-only (SRSFile.GetEBMLElementName switch, label strings)

Not parsing-critical; drive only UI labels. Fold into `EBMLIds` for completeness so the
switch reads named members. Values: `0x4286` EBMLVersion, `0x42F7` EBMLReadVersion,
`0x42F2` EBMLMaxIDLength, `0x42F3` EBMLMaxSizeLength, `0x4282` DocType, `0x4287`
DocTypeVersion, `0x4285` DocTypeReadVersion (plus the already-listed SeekHead/Info/
Tracks/Cluster/Cues/Attachments/Chapters/Tags).

#### 1d. Container-detection magic (SRSFile.cs / SRSWriter.cs first-bytes dispatch)

| Value | Meaning | Sites | Suggested home |
|---|---|---|---|
| `'R','I','F','F'` | RIFF/AVI FourCC | SRSFile:109; SRSWriter:269 | `RiffFourCC.Riff` |
| `'S','T','R','M'` | STREAM STRM magic | SRSFile:117 | `StreamFourCC.Strm` |
| `'M','2','T','S'` | STREAM M2TS magic | SRSFile:119 | `StreamFourCC.M2ts` |
| `'f','L','a','C'` | FLAC marker | SRSFile:127; SRSWriter:299; FlacMetadataReader:44; FlacHandler:38-41; FlacRebuilder:28 (`"fLaC"u8`) | `FlacConstants.Marker` |
| `'f','t','y','p'` @off 4 | MP4 ftyp atom | SRSFile:133; SRSWriter:287 | `Mp4AtomTypes.Ftyp` |
| `0x1A,0x45,0xDF,0xA3` | EBML/MKV header (== `EBMLIds.EBML`) | SRSFile:139; SRSWriter:281 | `EBMLIds.EBML` (byte view) |
| `0x30,0x26,0xB2,0x75` | ASF **Header** Object GUID prefix (WMV detect) | SRSFile:145; SRSWriter:293 | `AsfGuids.HeaderObjectPrefix` |
| `'I','D','3'` | ID3v2 tag magic | SRSFile:151; SRSWriter:305 | `Mp3Constants.Id3v2Magic` |
| `0xFF` + `0xE0` | MP3 frame sync (byte0=0xFF, mask byte1=0xE0) | SRSFile:161; SRSWriter:338 | `Mp3Constants.SyncByte0` / `SyncMask1` |
| `0x36,0x26,0xB2,0x75` | ASF **Data** Object GUID prefix | SRSFile:781; WMVHandler:53,157; WMVRebuilder:77 | `AsfGuids.DataObjectPrefix` |

#### 1e. FLAC block types (FlacMetadataReader.GetBlockTypeName)

Standard FLAC metadata block types — NEW `FlacBlockType` enum. Values `0`=STREAMINFO,
`1`=PADDING, `2`=APPLICATION, `3`=SEEKTABLE, `4`=VORBIS_COMMENT, `5`=CUESHEET, `6`=PICTURE
(FlacMetadataReader:143-149). `6` doubles as the max-standard-type boundary (`> 6` at
FlacHandler:68 → name `MaxStandardType`/`Picture`).

#### 1f. SRS custom block type-bytes & FourCCs / tag strings

| Value | Meaning | Sites | Suggested home |
|---|---|---|---|
| `0x73` (`'s'`) | FLAC SRSF block-type byte | SRSFile:529; FlacHandler:187; FlacRebuilder:46 | `FlacSrsBlockType.Srsf` |
| `0x74` (`'t'`) | FLAC SRST block-type byte | SRSFile:533; FlacHandler:199; FlacRebuilder:46 | `FlacSrsBlockType.Srst` |
| `0x75` (`'u'`) | FLAC fingerprint block-type byte (rebuilder only) | FlacRebuilder:46 | `FlacSrsBlockType.Fingerprint` |
| `"SRSF"` | SRS file-data block FourCC / atom / chunk / tag | SRSFile:157,307-08,385,611,621,701,707; SRSPayloadSerializer:118; MP4Handler:313-317 (byte-split); AVIHandler:266; MP4Rebuilder:83; MP3Rebuilder:59 | `SrsFourCC.SrsFile` |
| `"SRST"` | SRS track-data block FourCC / atom / chunk / tag | SRSFile:312-13,389,620,622,706,708; SRSPayloadSerializer:133; MP4Handler:326-330; AVIHandler:280; MP4Rebuilder:83; MP3Rebuilder:59 | `SrsFourCC.SrsTrack` |
| `"SRSP"` | SRS padding block FourCC (MP3/Stream) | SRSFile:372,391,395; MP3Rebuilder:59 | `SrsFourCC.SrsPadding` |
| `"STRM"u8` | STREAM SRS container framing FourCC | StreamHandler:70 | `SrsFourCC.Strm` |

#### 1g. ASF pseudo-GUIDs (16-byte ASCII) — three-way duplicated fields

| Value | Meaning | Duplicate field sites |
|---|---|---|
| `"SRSFSRSFSRSFSRSF"` | SRSF object GUID in ASF | SRSFile:752 `_guidSRSFile`; WMVHandler:9; WMVRebuilder:13 |
| `"SRSTSRSTSRSTSRST"` | SRST object GUID in ASF | SRSFile:753; WMVHandler:10; WMVRebuilder:14 |
| `"PADDINGBYTESDATA"` | SRS padding object GUID in ASF | SRSFile:754; WMVRebuilder:15 (Handler lacks it) |

Consolidate to one shared `AsfSrsGuids` (`GuidSRSFile`/`GuidSRSTrack`/`GuidSRSPadding`).

#### 1h. Misc format-version markers

| Value | Meaning | Site | Suggested name |
|---|---|---|---|
| `2000` | APEv2 version number (LE u32 in footer) | MP3TagReader:317 | `MP3TagReader.ApeV2Version` |

---

### Category 2 — SRSF/SRST Block Framing Offsets & Field Sizes

Per-format framing header sizes. The recurring value `8` means *four different structures*
across formats (see Overloaded §O-8); name per format rather than one shared `8`.

| Value | Meaning | Sites | Suggested home |
|---|---|---|---|
| `8` | Stream/MP3/RIFF SRS block header = 4-byte ASCII tag + 4-byte LE size | SRSFile:292,302,304,363,375,378,400,569,574; SRSPayloadSerializer:120(`4+4`),135; StreamHandler:72; MP3Rebuilder:49 | `SrsBlockLayout.HeaderSize = 8` |
| `4`+`4` vs `8` | Same 8-byte header spelled two ways in one file | SRSPayloadSerializer:120 vs 135 | unify to `SrsBlockLayout.HeaderSize` |
| `8` | MP4 normal atom header = 4-byte BE size + 4-byte type | MP4Handler:69,133,161,164,191,207,311,324; MP4Rebuilder:42,55; SRSFile:660 | `Mp4AtomTypes.AtomHeaderSize = 8` |
| `16` | MP4 extended-size atom header = 8 + 8 (u64 size) | MP4Handler:161,164; MP4Rebuilder:64; SRSFile:681 | `Mp4AtomTypes.AtomExtendedHeaderSize = 16` |
| `1` | MP4 extended-size sentinel (size32==1 → u64 follows) | MP4Handler:147; MP4Rebuilder:58; SRSFile:671 | `Mp4AtomTypes.ExtendedSizeSentinel = 1` |
| `0` | MP4 to-EOF sentinel (size32==0 → atom runs to end) | MP4Handler:162; MP4Rebuilder:65; SRSFile:685 | `Mp4AtomTypes.ToEndSentinel = 0` |
| `8` | RIFF/AVI chunk header = 4-byte FourCC + 4-byte LE size | AVIHandler:83-87,183-186; AVIRebuilder:27,155,163,183 | `RiffFourCC.ChunkHeaderSize = 8` |
| `4` | RIFF size-field offset (after FourCC) | AVIHandler:90; AVIRebuilder:91 | `RiffFourCC.SizeOffset = 4` |
| `4` | FLAC marker length | FlacHandler:38-41; FlacRebuilder:29; FlacMetadataReader:38,39,49 | `FlacConstants.MarkerSize = 4` |
| `4` | FLAC metadata block header = 1 type byte + 3-byte BE24 size | FlacHandler:60; FlacRebuilder:49,55-56; FlacMetadataReader:53 | `FlacConstants.BlockHeaderSize = 4` |
| `3` | FLAC BE24 size-field width | FlacMetadataReader:121 | `FlacConstants.BlockSizeFieldWidth = 3` |
| `24` | ASF object header = 16-byte GUID + 8-byte LE64 size | WMVHandler:25,31,38,42,45,59,136,141-45,150,206(`16+8`),215; WMVRebuilder:41,49,103; SRSFile:762,767,769 | `AsfGuids.ObjectHeaderSize = 24` |
| `16` | ASF GUID field width / offset-to-size within object header | WMVHandler:36,144,206,215; SRSFile:765,848 | `AsfGuids.GuidSize = 16` |
| `8` | ASF object size-field width (LE64) | WMVHandler:205-207,214-216; WMVRebuilder:39,68-69 | derive from `ObjectHeaderSize - GuidSize` |
| `26` | ASF Data Object sub-header = fileId(16)+packetCount(8)+reserved(2) | WMVHandler:55,58,59,165,167-168; WMVRebuilder:19 (`DataObjectHeaderLength`); SRSFile:758 (`AsfDataObjectHeaderLength`) | consolidate to one const (see Existing) |
| `16` | ASF Data Object fileId field width (offset-to-packetCount) | WMVHandler:62 | `AsfGuids.DataObjectFileIdSize = 16` (distinct from GuidSize — Overloaded §O-16) |
| `vintLen+2+1` | MKV block header = track VINT + 2-byte timecode + 1-byte flags | Handler(mkv):175,414; Rebuilder(mkv):197,525,772 | `MkvBlockLayout.FixedHeaderOverhead = 3` (2 timecode + 1 flags) |
| `8` | Stream first-frame embedded size field value (STRM/M2TS header-only) | SRSFile:118,120 | == `SrsBlockLayout.HeaderSize` (sentinel reuse) |

#### MP3 / ID3 tag header sizes

| Value | Meaning | Sites | Home |
|---|---|---|---|
| `10` | ID3v2 header (magic 3 + ver 2 + flags 1 + syncsafe size 4) | SRSFile:349,496-499; SRSWriter:308,311; MP3TagReader:13 (`Id3v2HeaderSize`); FlacMetadataReader:16 (dup) | adopt `MP3TagReader.Id3v2HeaderSize`; delete FLAC dup |
| `128` | ID3v1 fixed tag size | SRSWriter:344; MP3TagReader:14 (`Id3v1TagSize`) | adopt existing |
| `3` | ID3v1 "TAG" magic size | SRSFile:427 | `Mp3Constants.Id3v1MagicSize = 3` |
| `11` | Lyrics3 "LYRICSBEGIN" length | SRSFile:443,460 | `Mp3Constants.Lyrics3BeginMagicSize = 11` |
| `32` | APEv2 tag header size | SRSFile:478; MP3TagReader:15 (`ApeTagHeaderSize`) | adopt existing |
| `15` | Lyrics3v2 footer (6-byte size + "LYRICS200") | MP3TagReader:16 (`Lyrics3v2FooterSize`) | existing; invariant `6+9==15` |
| `6` | Lyrics3v2 6-byte ASCII decimal size sub-field | MP3TagReader:202,208,215 | `MP3TagReader.Lyrics3v2SizeFieldLength = 6` |
| `9` | "LYRICS200" marker length | MP3TagReader:202,215 | `MP3TagReader.Lyrics3v2MarkerLength = 9` |
| `9` | "LYRICSEND" marker length (Lyrics3v1) | MP3TagReader:237-241 | `MP3TagReader.Lyrics3v1EndMarkerLength = 9` (distinct string — Overloaded §O-9) |
| `5100` | Lyrics3v1 back-search limit | MP3TagReader:17 (`MaxLyrics3v1Size`) | existing |

#### MP4 tkhd track-ID layout

| Value | Meaning | Site | Home |
|---|---|---|---|
| `12` | tkhd v0 track-ID offset (also min payload guard) | MP4Handler:263,271 | `Mp4AtomTypes.TkhdTrackIdOffsetV0 = 12` |
| `20` | tkhd v1 track-ID offset | MP4Handler:271 | `Mp4AtomTypes.TkhdTrackIdOffsetV1 = 20` |
| `4` | tkhd track-ID field width (u32) | MP4Handler:275 | `Mp4AtomTypes.TkhdTrackIdFieldSize = 4` |

---

### Category 3 — Signature Sizes / Track-Data Field Widths

| Value | Meaning | Sites | Status |
|---|---|---|---|
| `256` | `TrackInfo.SignatureSize` — leading track bytes captured as signature | TrackInfo:12 (def); Handler(mkv):8; FlacHandler:99; WMVHandler:85; (referenced by name) | EXISTS — adopt only |
| `64` | Trailing ASCII check window in MKV `MinimumSignatureSize` (pyrescene inner slice) | Handler(mkv):517 | NEW `MKVContainerHandler.SignatureAsciiWindowSize = 64` |
| `8` | SRST `MatchOffset` field width (u64) in size formula | SRSPayloadSerializer:68 | field-width arithmetic; borderline |
| `2` | SRST flags + sigLen field widths (u16) in size formula | SRSPayloadSerializer:68 | field-width arithmetic; borderline |
| `65536` | SRST big-track-number threshold (TrackNumber ≥ 65536 → 4-byte field) | SRSPayloadSerializer:54 | NEW `SrstLayout.TrackNumberWidthThreshold = 0x10000` (domain, NOT a buffer) |

---

### Category 4 — Bit Masks / Flags

#### SRST flag bits (read + write, currently un-shared)

| Value | Meaning | Sites | Suggested |
|---|---|---|---|
| `0x8` | SRST bigTrackNumber flag (4-byte track num) | SRSFile:243 (read); SRSPayloadSerializer:62 (write) | `[Flags] SrstFlags.BigTrackNumber = 0x8` |
| `0x4` | SRST bigFile flag (8-byte data length) | SRSFile:258 (read); SRSPayloadSerializer:58 (write) | `[Flags] SrstFlags.BigFile = 0x4` |
| `0x0003` | SRSF flags = SimpleBlockFix(0x1) \| AttachmentsRemoved(0x2) | SRSPayloadSerializer:26 | `[Flags] SrsfFlags { None=0, SimpleBlockFix=0x1, AttachmentsRemoved=0x2 }` |

#### Big-file size threshold (2 GiB) — 5-way duplicated, HIGH priority

| Value | Meaning | Sites | Suggested |
|---|---|---|---|
| `0x80000000` | sampleSize ≥ 2 GiB ⇒ SRST uses 8-byte DataLength | MP3Handler:93; StreamHandler:81; AVIHandler:201; MP4Handler:93; WMVHandler:181; FlacHandler:149 | shared `SrsConstants.BigFileSizeThreshold = 0x80000000L` |

#### MKV block-flags lacing extraction (two incompatible idioms — see Overloaded §O-06/03)

| Value | Meaning | Sites |
|---|---|---|
| `0x06` | lacing mask, `(EBMLLaceType)(flags & 0x06)` → 0/2/4/6 | Handler(mkv):189,424; Rebuilder(mkv):809 |
| `0x03` | lacing mask after `>>1`, `(flags>>1)&0x03` → 0/1/2/3 | Rebuilder(mkv):296,777 |
| `1`/`3` | Xiph==1, EBML==3 tests under the shifted scheme | Rebuilder(mkv):310 |

Suggest `MkvBlockFlags.LacingMask = 0x06` and normalise both idioms onto `EBMLLaceType`.

#### EBML VINT marker / probe masks (Overloaded §O-0x80, §O-0xFF)

| Value | Meaning | Sites | Suggested |
|---|---|---|---|
| `0x80` | 1-byte VINT marker OR-mask (encode) | EBMLWriter:62 | `EBMLVInt.Marker1 = 0x80` |
| `0x40` | 2-byte VINT marker | EBMLWriter:67 | `EBMLVInt.Marker2 = 0x40` |
| `0x20` | 3-byte VINT marker | EBMLWriter:72 | `EBMLVInt.Marker3 = 0x20` |
| `0x10` | 4-byte VINT marker | EBMLWriter:77 | `EBMLVInt.Marker4 = 0x10` |
| `0x80` | VINT length-probe mask `0x80>>i` (decode) | EBMLLacing:268; EBMLReader:21,64 | keep computed; same MSB value as Marker1 |
| `0xFF` | data-bit strip mask `0xFF>>vintLen` | EBMLLacing:188 | inline byte-mask |
| `0xFF` | byte fill / low-byte extract in VINT emit | EBMLWriter:87,94 | inline byte-mask |

#### FLAC block-header flag/mask bits

| Value | Meaning | Sites | Suggested |
|---|---|---|---|
| `0x80` | FLAC last-metadata-block flag (MSB of type byte) | FlacHandler:52,160; FlacRebuilder:39; FlacMetadataReader:118 | `FlacConstants.LastBlockFlag = 0x80` |
| `0x7F` | FLAC 7-bit block-type mask | FlacRebuilder:40; FlacMetadataReader:119 | `FlacConstants.BlockTypeMask = 0x7F` |

#### MP3 / ID3 masks & alignment

| Value | Meaning | Sites | Suggested |
|---|---|---|---|
| `0xE0` | MP3 sync mask on byte 1 (top 3 bits) | SRSFile:161; SRSWriter:338 | `Mp3Constants.SyncMask1 = 0xE0` |
| `0xFF` | MP3 sync byte 0 / redundant `& 0xFF` | SRSFile:161; SRSWriter:338 | `Mp3Constants.SyncByte0 = 0xFF` |
| `0x7F` | ID3v2 syncsafe 7-bit-per-byte mask (×4) | MP3TagReader:356-359 | `MP3TagReader.SyncSafeByteMask = 0x7F` |
| `0x80` | MKV ASCII boundary (`>= 0x80` non-ASCII) | Handler(mkv):555 | `MKVContainerHandler.AsciiBoundary = 0x80` |
| `% 2 != 0` | RIFF word-alignment (odd chunks padded) | AVIHandler:113,154,222,253; AVIRebuilder:112,165,194,225 | standard RIFF rule; low priority |

---

### Category 5 — Other Format Constants

| Value | Meaning | Sites | Suggested |
|---|---|---|---|
| `3` | MKV ContentCompAlgo == 3 → header-stripping | EBMLHeaderStripping:114; Handler(mkv):306 | `EBMLIds.ContentCompAlgoHeaderStripping = 3` (or small `EBMLContentCompAlgo` enum) |
| `-1` | MKV "compression element seen, algo not yet read" sentinel | Handler(mkv):159 | `TrackInfo.CompressionAlgoUnknown = -1` |
| `0xFF`/`255` | Xiph lacing continuation sentinel (add 255, read next) | EBMLLacing:98; Rebuilder(mkv):325 (`255`) | `EBMLLacing.XiphContinuation = 0xFF` (unify hex) |
| `40` | pyrescene max signature-scan loops | Handler(mkv):14 (`MaxSignatureBlocks`) | EXISTS |
| `20` | scan-alignment tolerance | Rebuilder(mkv):649 (`MaxHeaderOverlap`) | EXISTS (local) |
| `4096` | pre-track skip margin heuristic | Rebuilder(mkv):595 | NEW `MKVContainerRebuilder.PreTrackSkipMargin = 4096` (borderline) |
| `6` | ISOMedia VobTitlePrefix length ("VTS_"+2 digits) | ISOMediaExtractor:388 | `ISOMediaExtractor.VobTitlePrefixLength = 6` |

#### EBML VINT encoding tier thresholds & widths (EBMLWriter.MakeEBMLUInt / MakeEBMLId)

| Value | Meaning | Site | Suggested |
|---|---|---|---|
| `0x7F` | 1-byte VINT tier limit | EBMLWriter:60 | `EBMLVInt.OneByteSizeLimit` |
| `0x3FFF` | 2-byte VINT tier limit | EBMLWriter:65 | `EBMLVInt.TwoByteSizeLimit` |
| `0x1FFFFF` | 3-byte VINT tier limit | EBMLWriter:70 | `EBMLVInt.ThreeByteSizeLimit` |
| `0x0FFFFFFF` | 4-byte VINT tier limit | EBMLWriter:75 | `EBMLVInt.FourByteSizeLimit` |
| `5` | ≥5-byte VINT starting width | EBMLWriter:82 | `EBMLVInt.FiveByteMinWidth` |
| `0x07FFFFFFFF` | 5-byte VINT max value (2^35−1) | EBMLWriter:83 | `EBMLVInt.FiveByteSizeMax` |
| `8` | Max EBML VINT byte width | EBMLWriter:84,88; EBMLReader:23,29,66,71 | `EBMLVInt.MaxByteWidth = 8` (Overloaded §O-08bits) |
| `0x100` | ID <1 byte bound | EBMLWriter:105 | `EBMLIds.OneByteBound` |
| `0x10000` | ID <2 byte bound | EBMLWriter:110 | `EBMLIds.TwoByteBound` |
| `0x1000000` | ID <3 byte bound | EBMLWriter:115 | `EBMLIds.ThreeByteBound` |

#### Existing "same-value, different-concept" 64 KiB trio (do NOT merge)

| Value | Meaning | Site | Status |
|---|---|---|---|
| `0x10000` | SRSRebuilder ±64 KiB hint search window | SRSRebuilder:88 (`SearchBufferSize`) | EXISTS |
| `64*1024` | SignatureScanner sliding I/O buffer | SignatureScanner:8 (`DefaultBufferSize`) | EXISTS |
| `65536` | SRST track-number width threshold (domain) | SRSPayloadSerializer:54 | NEW (Category 3) — keep separate |

---

## 2. Existing Infrastructure (ADOPT — do not re-create)

| Name | Kind | Location | Members / Value |
|---|---|---|---|
| `EBMLIds` | static class (24 `const ulong`) | EBMLWriter.cs:6-47 | EBML, Segment, SeekHead, Info, Cluster, Tracks, TrackEntry, TrackNumber, ContentEncodings, ContentEncoding, ContentCompression, ContentCompAlgo, ContentCompSettings, BlockGroup, Block, SimpleBlock, Attachments, AttachedFile, Cues, Chapters, Tags, ReSampleContainer, ResampleFile(0x6A75), ResampleTrack(0x6B75) + `IsContainer(ulong)` |
| `EBMLLaceType` | enum | EBMLLacing.cs:7-28 | None=0, Xiph=2, Fixed=4, EBML=6 |
| `SRSContainerType` | public enum | SRSBlock.cs | AVI, MKV, MP4, WMV, FLAC, MP3, Stream |
| `TrackInfo.SignatureSize` | public const int | TrackInfo.cs:12 | 256 |
| `MP4Atoms.ContainerAtoms` | static HashSet\<string\> | IContainerHandler.cs | moov, trak, mdia, minf, stbl, edts, udta |
| `SRSRebuilder.SearchBufferSize` | private const int | SRSRebuilder.cs:88 | 0x10000 |
| `SignatureScanner.DefaultBufferSize` | private const int | SignatureScanner.cs:8 | 64*1024 |
| `SRSFile.AsfDataObjectHeaderLength` | private const int | SRSFile.cs:758 | 26 |
| `WMVContainerRebuilder.DataObjectHeaderLength` | private const int | WMVRebuilder.cs:19 | 26 (dup of above — unify) |
| `SRSFile._guidSRSFile/Track/Padding` | private static readonly byte[] | SRSFile.cs:752-754 | "SRSF…"/"SRST…"/"PADDINGBYTESDATA" |
| `WMVContainerHandler._guidSRSFile/Track` | private static readonly | WMVHandler.cs:9-10 | (dup; missing Padding) |
| `WMVContainerRebuilder._guidSRSFile/Track/Padding` | private static readonly | WMVRebuilder.cs:13-15 | (dup, full set) |
| `MKVContainerHandler.MaxSignatureBlocks` | private const | Handler(mkv):14 | 40 |
| `MKVContainerRebuilder.ShouldIncludeBlock.MaxHeaderOverlap` | local const | Rebuilder(mkv):649 | 20 |
| `MP3TagReader.Id3v2HeaderSize` | private const int | MP3TagReader.cs:13 | 10 |
| `MP3TagReader.Id3v1TagSize` | private const int | MP3TagReader.cs:14 | 128 |
| `MP3TagReader.ApeTagHeaderSize` | private const int | MP3TagReader.cs:15 | 32 |
| `MP3TagReader.Lyrics3v2FooterSize` | private const int | MP3TagReader.cs:16 | 15 |
| `MP3TagReader.MaxLyrics3v1Size` | private const int | MP3TagReader.cs:17 | 5100 |
| `FlacMetadataReader.Id3v2HeaderSize` | private const int | FlacMetadataReader.cs:16 | 10 — **DUPLICATE of MP3TagReader; delete** |

**Duplicate private-const fields to DELETE and re-point at `EBMLIds`:**
`EBMLHeaderStripping.cs:11-15` (IdContentEncodings, IdContentEncoding, IdContentCompression,
IdContentCompAlgo, IdContentCompSettings) — verbatim copies of `EBMLIds` members. Highest-
priority, zero-ambiguity cleanup.

---

## 3. Recommended Constant Homes

Follow the Phase-1/2 per-format id/layout-class pattern. Proposed homes:

**Element-ID / atom / FourCC / GUID classes (Category 1):**
- `EBMLIds` (existing, EBMLWriter.cs) — extend with §1b NEW members, §1c display IDs, the
  ID byte-width bounds (`OneByteBound`/`TwoByteBound`/`ThreeByteBound`), and
  `ContentCompAlgoHeaderStripping`.
- `Mp4AtomTypes` (new; near existing `MP4Atoms`) — `Ftyp`, atom header sizes, sentinels,
  tkhd offsets/field size.
- `RiffFourCC` (new) — `Riff`, `ChunkHeaderSize`, `SizeOffset`.
- `StreamFourCC` (new) — `Strm`, `M2ts`.
- `AsfGuids` (new) — `HeaderObjectPrefix`, `DataObjectPrefix`, `ObjectHeaderSize`,
  `GuidSize`, `DataObjectFileIdSize`, `DataObjectHeaderLength(26)`.
- `AsfSrsGuids` (new) — the three 16-byte ASCII pseudo-GUIDs (consolidate 3-way dup).
- `FlacBlockType` (new enum) — Streaminfo…Picture; `MaxStandardType`.
- `FlacSrsBlockType` (new) — `Srsf`, `Srst`, `Fingerprint`.
- `FlacConstants` (new) — `Marker`, `MarkerSize`, `BlockHeaderSize`, `BlockSizeFieldWidth`,
  `LastBlockFlag`, `BlockTypeMask`, `MaxSrsBlockCount(3)`.
- `SrsFourCC` (new) — `SrsFile`, `SrsTrack`, `SrsPadding`, `Strm`.
- `Mp3Constants` (new) — `Id3v2Magic`, `Id3v1MagicSize`, `Lyrics3BeginMagicSize`,
  `SyncByte0`, `SyncMask1`; plus MP3TagReader-local sub-field/marker lengths, `SyncSafeByteMask`, `ApeV2Version`.

**Framing-layout classes (Category 2/3):**
- `SrsBlockLayout` (new) — `HeaderSize = 8` (Stream/MP3/RIFF-SRS tag+size).
- `MkvBlockLayout` (new) — `FixedHeaderOverhead = 3` (timecode 2 + flags 1).
- `SrstLayout` (new) — `TrackNumberWidthThreshold = 0x10000`, field widths.
- `EBMLVInt` (existing, EBMLLacing.cs) — extend with tier limits, `MaxByteWidth`,
  `Marker1..4`, `FiveByteMinWidth`, `FiveByteSizeMax`.

**Flags (Category 4):**
- `[Flags] SrstFlags` (new) — `None=0, BigFile=0x4, BigTrackNumber=0x8`.
- `[Flags] SrsfFlags` (new) — `None=0, SimpleBlockFix=0x1, AttachmentsRemoved=0x2`.
- `MkvBlockFlags` (new) — `LacingMask = 0x06`.

**Cross-format shared (Category 4/5):**
- `SrsConstants` (new) — `BigFileSizeThreshold = 0x80000000L` (replaces 6 copies).
- `EBMLLacing.XiphContinuation = 0xFF` (unify the `255`/`0xFF` split).

**Handler/Rebuilder-local privates (Category 3/5):**
- `MKVContainerHandler` — `SignatureAsciiWindowSize = 64`, `AsciiBoundary = 0x80`.
- `MKVContainerRebuilder` — `PreTrackSkipMargin = 4096`.
- `ISOMediaExtractor` — `VobTitlePrefixLength = 6`.
- `TrackInfo` — `CompressionAlgoUnknown = -1`.

---

## 4. Overloaded Literals (intent-risk sites — same value, ≠ meaning)

| ID | Value | Meaning A | Meaning B | Meaning C |
|---|---|---|---|---|
| O-8 | `8` | SRS block header (tag+LE size) — Stream/MP3/RIFF-SRS | MP4 normal atom header (BE size + type) | RIFF/AVI chunk header (FourCC + LE size); also STRM embedded-size sentinel |
| O-08bits | `8` | Max EBML VINT byte width (domain) — EBMLWriter:84/88, EBMLReader:23/29/66/71 | bits-per-byte in shift exprs `(w-1)*8`,`i*8` (OUT OF SCOPE) — EBMLWriter:90/91/94 | — |
| O-16 | `16` | ASF object GUID width / offset-to-size — WMVHandler:36,144 | ASF Data Object fileId width / offset-to-packets — WMVHandler:62 | ASF GUID length guard — SRSFile:848; magic-detect buffer cap (coincidental) — SRSFile:67 |
| O-06/03 | 2-bit lacing field | `flags & 0x06` → 0/2/4/6 (EBMLLaceType) — Handler(mkv):189,424; Rebuilder:809 | `(flags>>1)&0x03` → 0/1/2/3 — Rebuilder(mkv):296,777 | raw `==1`(Xiph)/`==3`(EBML) tests — Rebuilder:310. **Three coexisting encodings — top MKV risk.** |
| O-0x80 | `0x80` | 1-byte VINT marker OR-mask (encode) — EBMLWriter:62 | VINT length probe `0x80>>i` (decode) — EBMLLacing:268, EBMLReader:21,64 | FLAC last-block flag — FlacHandler:52,160 etc.; MKV ASCII boundary — Handler:555 |
| O-0xFF | `0xFF` | Xiph continuation sentinel (protocol) — EBMLLacing:98; Rebuilder(mkv):325 | data-bit strip mask `0xFF>>vintLen` — EBMLLacing:188 | byte fill/extract in VINT emit — EBMLWriter:87,94; MP3 sync byte0 — SRSFile:161 |
| O-256 | `256` | `TrackInfo.SignatureSize` (signature capture) | MKV lacing peek buffer cap (impl, coincidental) — Handler:199,433; Rebuilder:804 | — |
| O-3 | `3` | MKV ContentCompAlgo header-stripping — Handler:306 | FLAC SEEKTABLE block type — FlacMetadataReader:146 | FLAC `srsBlockCount <= 3` guard — FlacRebuilder:46; ID3v1 magic size — SRSFile:427; FLAC BE24 width |
| O-4 | `4` | FLAC marker size vs FLAC block-header size (two consts) | RIFF FourCC / MP4 type / tkhd field widths | RIFF size-field offset |
| O-9 | `9` | "LYRICS200" marker length — MP3TagReader:202,215 | "LYRICSEND" marker length — MP3TagReader:237-241 | — |
| O-6 | `6` | FLAC PICTURE block type — FlacMetadataReader:149 | FLAC non-standard boundary `> 6` — FlacHandler:68 | ISOMedia VobTitlePrefix length — ISOMediaExtractor:388 |
| O-10 | `10` | ID3v2 header size — MP3TagReader:13 | duplicate decl — FlacMetadataReader:16 | inline copies — SRSFile:349,497,499; SRSWriter:308,311 |
| O-1 | `1` | MP4 extended-size sentinel — MP4Handler:147 | Stream/MP3 hardcoded single-track number — StreamRebuilder:27, MP3Rebuilder:63; WMV virtual track num — WMVHandler:75 | — |
| O-64K | `65536`/`0x10000`/`64*1024` | SRSRebuilder search window (impl) | SignatureScanner I/O buffer (impl) | SRST track-num width threshold (**domain** — only this needs a domain const) |
| O-0x18538067 | `0x18538067` | Segment: set isSegmentLevel flag — Handler(mkv):164 | Segment: ReSample injection point — Handler(mkv):380 | (same ID, separable roles) |

---

## 5. Out of Scope (trivial / impl / non-domain)

- Trivial `0`/`1`/`2` — inits, index starts, loop counters, boolean returns, frame-count
  `data[0]+1`, `bytesConsumed=1` seed, `EBMLLaceType.None=0`, `i+1` returns.
- Buffer sizes: `80*1024` FileStream/copy buffers (all handlers/rebuilders); `32*1024*1024`
  ISOMediaExtractor VOB scanner; `256` MKV lacing peek cap (coincidental — see O-256);
  `16` magic-detect read clamp (SRSFile:67); `1048576` FileName element sanity guard
  (Rebuilder(mkv):411).
- Shift amounts where N is the only literal: `<<8`,`>>8`,`>>16`,`>>24`,`<<21`,`<<14`,`<<7`,
  `1L<<(7*vintLen-1)`, `(1UL<<(7*length))-1` unknown-size sentinel, `7` in `7*length`/
  `7*vintLen` VINT data-bit formula, BE24 encode/decode shifts.
- Percent/progress: `*100`, `100` ceiling, `-1` lastPercent sentinel, `0` default MatchOffset.
- Stream-read guards: `actualRead<=0`, `read==0`, `count<=0`, `Math.Max(1L,…)`,
  `Math.Min(…)` args, `track.DataLength>0`, `guid.Length>=4`.
- Field-pointer advances `+=2/+=4/+=8` in ParseFileDataPayload/ParseTrackDataPayload — pure
  type-width arithmetic.
- API-driven allocs: `stackalloc byte[4]`/`[8]` for BinaryPrimitives, Crc32 digest width.
- Arithmetic: `'0'` ASCII base, `d0*10+d1` two-digit decode, `f+1` display index.
- Display strings: "Finding tracks (EBML walk)", element-name labels feeding UI only
  (the IDs behind them ARE in scope — Category 1c).

---

## 6. Suggested Task Decomposition (advisory for spec author)

Grouped for independent testing; ordered roughly low-risk → high-risk.

- **T1 — EBML ID consolidation (pure dedup, zero behaviour change).** Delete the 5
  `EBMLHeaderStripping` private-const dups + re-point at `EBMLIds`; delete the 5+4 MKV
  Handler private fields (`_eBMLId*`, `_mKVSrsContainers`) + adopt `EBMLIds`; add §1b/§1c
  NEW members + `ContentCompAlgoHeaderStripping`. Golden-file MKV round-trip covers it.
- **T2 — EBML VINT / writer internals.** `EBMLVInt` tier limits, `MaxByteWidth`,
  `Marker1..4`, `FiveByte*`; `EBMLIds` ID byte-width bounds; `EBMLLacing.XiphContinuation`.
  Unit-test MakeEBMLUInt/MakeEBMLId/ReadUnsigned against known vectors.
- **T3 — MKV block-flags lacing normalisation (behaviour-sensitive).** Reconcile the
  `0x06` vs `>>1 & 0x03` vs raw `1/3` idioms onto `EBMLLaceType` + `MkvBlockFlags.LacingMask`;
  add `MkvBlockLayout.FixedHeaderOverhead`, `SignatureAsciiWindowSize`, `AsciiBoundary`,
  `PreTrackSkipMargin`, `CompressionAlgoUnknown`. Needs laced-MKV fixtures for each type.
- **T4 — SRS block framing + FourCCs + flags (cross-format core).** `SrsBlockLayout`,
  `SrsFourCC`, `[Flags] SrstFlags`/`SrsfFlags`, `SrstLayout.TrackNumberWidthThreshold`,
  shared `SrsConstants.BigFileSizeThreshold` (replaces 6 copies). Touches SRSFile +
  SRSPayloadSerializer + all handlers' bigFile predicate; SRS round-trip + payload unit tests.
- **T5 — MP4 atom layout.** `Mp4AtomTypes` (Ftyp, header sizes, sentinels, tkhd offsets/
  field size). MP4 create/rebuild round-trip.
- **T6 — RIFF/AVI + Stream framing.** `RiffFourCC`, `StreamFourCC`, adopt `SrsBlockLayout`.
  AVI + Stream round-trip.
- **T7 — ASF/WMV GUIDs + object framing.** `AsfGuids`, `AsfSrsGuids` (consolidate 3-way
  pseudo-GUID dup), unify the two `26` consts, `WmvVirtualTrackNumber`. WMV round-trip.
- **T8 — FLAC + MP3/ID3 tag constants.** `FlacBlockType`, `FlacSrsBlockType`,
  `FlacConstants`; delete FlacMetadataReader `Id3v2HeaderSize` dup; `Mp3Constants` +
  MP3TagReader sub-field/marker lengths + `SyncSafeByteMask` + `ApeV2Version`;
  `ISOMediaExtractor.VobTitlePrefixLength`. FLAC + MP3 tag-parse round-trips.

---
