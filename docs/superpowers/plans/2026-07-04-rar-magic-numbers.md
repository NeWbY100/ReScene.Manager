# RAR Magic-Number Elimination (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the domain numeric literals in `ReScene.Lib/ReScene/RAR/*.cs` with named constants — a behaviour-preserving refactor (zero output-byte change), and the pilot that sets the recipe for the later namespaces.

**Architecture:** Adopt the enums that already exist (`RARFileFlags`, `RAR4BlockType`), consolidate duplicated constants (markers in `RARUtils`; header offsets in `RARPatcher`'s private consts / `RARHeaderReader` locals) into a single `Rar4HeaderLayout`, and name the genuinely-missing constants (RAR5 flags/vint/CompInfo, EXT_TIME + DOS-date bit-fields, host-OS table, sentinels). Every substitution is value-identical; the existing byte-exact test suites are the safety net.

**Tech Stack:** .NET (net8.0;net10.0 lib), xUnit. Spec: `docs/superpowers/specs/2026-07-04-rar-magic-numbers-design.md`.

## Global Constraints

- **Behaviour-preserving:** every named constant MUST equal the literal it replaces; no logic changes. Pick the name by INTENT at each site (see the overloaded-literal list below), not just by value.
- **Overloaded literals** (same value, different meaning — choose the right name per site):
  - `0x00E0` = `RARFileFlags.Directory` (all-bits-set test) vs `RARFlagMasks.DictionarySizeMask` (dict-size extraction).
  - `32` = `Rar4HeaderLayout.HighPackSizeOffset` (only when LARGE set) vs `Rar4HeaderLayout.FixedFieldsEnd` (name base offset otherwise). **`<< 32`** (the 64-bit hi/lo pack-size combine, e.g. `(long)highPack << 32` at `RARPatcher.cs:267,750`, readers `:452-453`) is a SHIFT, NOT an offset — leave it a literal, never `FixedFieldsEnd`.
  - `7` / `8` are overloaded: the RAR4/RAR5 **marker lengths** (`isRar5 ? 8 : 7 // skip marker` at `RARStream.cs:278,346`, `RARArchive.cs:407`, `RARPatcher.PatchStream:436`) → `RARUtils.Rar4Marker.Length` / `Rar5Marker.Length` (do these in **Task 5**, not Task 2); the RAR4 **base header size** (`7`) → `Rar4HeaderLayout.BaseHeaderSize` (Task 2). Same value, opposite meaning — pick by intent.
  - `RARFileFlags.DictSize64…DictSize4096` are multi-bit FIELD VALUES — NEVER call `HasFlag` on them; keep the dict-size bits as `flags & RARFlagMasks.DictionarySizeMask`. `HasFlag` is only for single-bit flags (Large/Unicode/ExtTime/LongBlock/Salt) and the `Directory` all-bits-set test.
- **Scope:** `ReScene.Lib/ReScene/RAR/*.cs` only. NOT `RAR/Decompression/**` (Phase 5) — including the `RARMethod` enum's file (`RARDecompressor.cs`), which we only *use*, never edit.
- **Build/test ONLY with `-p:BaseOutputPath=bin2/`** (a running app locks `bin/`). NEVER kill the app. After verifying, delete bin2: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- **No-warning build** (both TFMs): `dotnet build ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental` → **0 warnings / 0 errors** (AnalysisLevel=latest-All, EnforceCodeStyleInBuild).
- **Verification per task = existing suites stay green with NO NEW FAILURES.** Run `dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/`. (Baseline at branch start: 1195 pass — but gate on "no new failures", not the absolute count.) The byte-exact suites (`RARPatcherTests`, `RARHeaderReaderTests`, `RARDetailedParserTests`, `RARArchiveTests`, `RARStreamTests`, `RARFlagsTests`, `RAR5HeaderReaderTests`) are what prove no output byte changed.
- Commit trailer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Branch: `refactor/magic-numbers-rar` (already checked out, off `main`). Do NOT edit `RAR5HeaderReader.RAR5Marker`'s test expectations by deleting the symbol without migrating them (Task 5).
- The reviewer for each task verifies: diff is ONLY value-preserving substitutions + new/consolidated constant definitions + removal of the duplicated ones; each substitution is intent-correct; no behavioural change.

## File structure

- Create: `ReScene.Lib/ReScene/RAR/Rar4HeaderLayout.cs` (RAR4 header offsets/sizes + EXT_TIME + DOS-date constants).
- Extend: `ReScene.Lib/ReScene/RAR/RARFlags.cs` (add `RAR5ArchiveFlags`, `RarHostOs`).
- Modify (only where they carry RAR-domain literals): `RARHeaderReader.cs`, `RARDetailedHeader.cs`, `RARPatcher.cs`, `RARStream.cs`, `RARArchive.cs`, `RAR5HeaderReader.cs`, `RARUtils.cs`, `RARFileHeader.cs`.
- Remove/alias duplicates: `RARPatcher.Offset*` consts and `RARHeaderReader` offset locals → migrated into `Rar4HeaderLayout`; `RAR5HeaderReader.RAR5Marker` → alias to `RARUtils.Rar5Marker` OR removed with its tests migrated.
- Tests: add the EXT_TIME-display regression test (Task 1); adjust `RAR5HeaderReaderTests.cs:58,64` if the marker duplicate is removed (Task 5).

---

### Task 1: Pin the untested EXT_TIME display path

The verbose EXT_TIME *display* decode in `RARDetailedParser.ParseRAR4FileHeader` (`RARDetailedHeader.cs:895-970`) has no test, so a wrong constant there (renamed in Task 3) would go undetected. Add a regression test FIRST that pins its current rendered output.

**Files:**
- Test: `ReScene.Lib/ReScene.Tests/RARDetailedParserTests.cs`

**Interfaces:**
- Consumes: `RARDetailedParser.Parse(...)` (existing) and its `RARDetailedBlock`/`RARHeaderField` output.
- Produces: a regression fixture other tasks rely on staying green.

- [ ] **Step 1: Read the current EXT_TIME rendering to capture exact expected strings.** Read `RARDetailedHeader.cs:895-970` and any existing `RARDetailedParserTests` fixture that builds a RAR4 file header with EXT_TIME (`RARFileFlags.ExtTime` set). If a test builder (`RARTestDataBuilder`/`AddFileHeader...`) can produce a header with a known EXT_TIME mtime, use it; otherwise construct the header bytes inline the way `RARHeaderReaderTests`/`RARPatcherTests` do.

- [ ] **Step 2: Write the test asserting the rendered EXT_TIME field(s).** Assert the exact `RARHeaderField` name/value strings the parser currently produces for a header whose mtime carries a known sub-second remainder at a chosen precision (e.g. a 3-byte remainder). Example shape (adapt names to the real API):

```csharp
[Fact]
public void ParseRAR4_ExtTime_RendersModifiedTimeFieldExactly()
{
    byte[] header = /* a RAR4 file header with RARFileFlags.ExtTime and a known mtime remainder */;
    RARDetailedBlock block = RARDetailedParser.Parse(header).Single(b => b.BlockType == "File");

    // RAR4 EXT_TIME display fields are named "Extended Time Flags" / "Ext mtime DOS" / "Ext mtime
    // subsec" (RARDetailedHeader.cs:898,944,965) — NOT "Modification Time" (that's the RAR5 path).
    RARHeaderField subsec = block.Fields.Single(f => f.Name.Contains("Ext mtime subsec", StringComparison.Ordinal));
    Assert.Equal(/* the exact string the parser produces today */, subsec.Value);
}
```

- [ ] **Step 3: Run it — it must PASS (it pins existing behaviour).** `dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~RARDetailedParserTests"` → PASS. (If the assertion is hard to pin exactly, assert the stable sub-strings — the precision label and the sub-second digits — rather than the whole line; the point is that Task 3's renames don't change them.)

- [ ] **Step 4: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene.Tests/RARDetailedParserTests.cs
git commit -m "test(rar): pin EXT_TIME display rendering before magic-number refactor"
```

---

### Task 2: `Rar4HeaderLayout` — consolidate RAR4 header offsets/sizes

Create the single source of truth for RAR4 header field offsets, migrating `RARPatcher`'s private `Offset*` consts and `RARHeaderReader`'s offset locals into it, and adopting it at every offset site (intent-correct for the overloaded `32`).

**Files:**
- Create: `ReScene.Lib/ReScene/RAR/Rar4HeaderLayout.cs`
- Modify: `RARPatcher.cs` (remove `Offset*` consts `:222-231`, reference layout), `RARHeaderReader.cs` (remove `baseHeaderSize`/`addSizeField`/`serviceFieldsSize` locals `:417-419,737-738`, reference layout), `RARStream.cs`, `RARArchive.cs`, `RARDetailedHeader.cs`.
- Out of scope: `SRRWriter.cs` also walks RAR4 headers with the same offsets, but it lives in `SRR/` — leave it for Phase 2. Phase 1 touches only `RAR/`.

**Interfaces:**
- Produces: `internal static class Rar4HeaderLayout` with the members below (later tasks add EXT_TIME/DOS constants to it).

- [ ] **Step 1: Create `Rar4HeaderLayout.cs`.** Values copied verbatim from `RARPatcher.Offset*` and the `RARHeaderReader` locals:

```csharp
namespace ReScene.RAR;

/// <summary>
/// RAR 4.x block/header field layout — the single source of truth for the byte offsets and sizes
/// used when reading, walking, and patching RAR4 headers. Values mirror the on-disk format exactly.
/// </summary>
internal static class Rar4HeaderLayout
{
    // Base block header (all RAR4 blocks): CRC(2) TYPE(1) FLAGS(2) SIZE(2).
    public const int Crc = 0;
    public const int Type = 2;
    public const int Flags = 3;
    public const int HeaderSize = 5;
    public const int BaseHeaderSize = 7;      // CRC 2 + type 1 + flags 2 + size 2
    public const int AddSize = 7;             // ADD_SIZE field offset (file/service blocks)
    public const int AddSizeFieldLength = 4;

    // File-header fixed fields (after the base header).
    public const int HostOs = 15;
    public const int FileTime = 20;
    public const int NameSize = 26;
    public const int Attr = 28;

    // Offset 32 is the end of the fixed file-header fields. It is therefore BOTH the HIGH_PACK_SIZE
    // field offset (present only when RARFileFlags.Large is set) AND the NAME base offset (when Large
    // is clear). Use the name that matches the intent at each call site.
    public const int HighPackSizeOffset = 32;
    public const int FixedFieldsEnd = 32;
}
```

- [ ] **Step 2: Migrate `RARPatcher`.** Delete the `private const int Offset* = …;` block (`:222-231`). Replace each `OffsetXxx` use with `Rar4HeaderLayout.Xxx` — with the `32` split: `OffsetHighPackSize` uses that read/write HIGH_PACK_SIZE (`:266,746`) → `Rar4HeaderLayout.HighPackSizeOffset`; the name-base, copy/insert, and header-size guard uses of 32 (`nameOffset = 32 + (large?8:0)` at `:488,667`; `extTimeOffset = 32 + …` at `:348`; the `>= 32` guards at `:466,651,834`; the copy/insert at `:843-851,882-887`) → `Rar4HeaderLayout.FixedFieldsEnd`. Express the derived siblings as sums: `11`(`:601,829`)→`Rar4HeaderLayout.BaseHeaderSize + Rar4HeaderLayout.AddSizeFieldLength`; `36`(`:264,744`)→`Rar4HeaderLayout.FixedFieldsEnd + 4`; `40`(`:873`)→`Rar4HeaderLayout.FixedFieldsEnd + 8` (the trailing `4`/`8` are the HIGH_PACK_SIZE / HIGH_PACK_SIZE+HIGH_UNP_SIZE widths — leave inline, or add `HighPackSizeWidth = 4`/`HighSizeFieldsWidth = 8`; be consistent). Do NOT touch the `(long)highPack << 32` combines (`:267,750`) — that `32` is a shift.

- [ ] **Step 3: Migrate `RARHeaderReader`.** Replace the `baseHeaderSize`/`addSizeField`/`serviceFieldsSize` locals (`:417-419,737-738`) and the bare `7`/`4` header-arithmetic with `Rar4HeaderLayout.*`. `serviceFieldsSize = 21` stays a local (it is service-specific, not in the layout) unless it decomposes cleanly into layout members.

- [ ] **Step 4: Adopt in `RARStream`, `RARArchive`, `RARDetailedHeader`.** Replace bare RAR4 base-header/offset literals with `Rar4HeaderLayout.*`, intent-correct. CAUTION: the `7`/`8` in `RARStream.cs:278,346` and `RARArchive.cs:407` (`isRar5 ? 8 : 7 // skip marker`) are MARKER LENGTHS, not base-header size — leave them for **Task 5** (`Rar4Marker.Length`/`Rar5Marker.Length`); only rename a `7` here that is genuinely the base-header size.

- [ ] **Step 5: Build + full lib suite — must be green, no new failures.**

```
dotnet build ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental   # 0 warnings / 0 errors
dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/          # no new failures vs baseline
```

- [ ] **Step 6: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/RAR/Rar4HeaderLayout.cs ReScene.Lib/ReScene/RAR/RARPatcher.cs ReScene.Lib/ReScene/RAR/RARHeaderReader.cs ReScene.Lib/ReScene/RAR/RARStream.cs ReScene.Lib/ReScene/RAR/RARArchive.cs ReScene.Lib/ReScene/RAR/RARDetailedHeader.cs
git commit -m "refactor(rar): consolidate RAR4 header offsets into Rar4HeaderLayout"
```

---

### Task 3: EXT_TIME + DOS-date bit-field constants

Add the EXT_TIME and DOS date/time constants to `Rar4HeaderLayout` and adopt them, guarded by Task 1's test.

**Files:**
- Modify: `Rar4HeaderLayout.cs` (add the constants below), `RARHeaderReader.cs`, `RARDetailedHeader.cs`, `RARPatcher.cs`, `RARUtils.cs`.

**Interfaces:**
- Consumes: `Rar4HeaderLayout` (Task 2). Guarded by `RARDetailedParserTests` EXT_TIME test (Task 1) and the `RARPatcherTests`/`RARHeaderReaderTests` EXT_TIME coverage.

- [ ] **Step 1: Add the constants to `Rar4HeaderLayout`** (values verbatim from the current bit-math):

```csharp
    // EXT_TIME (RAR4 extended timestamps). Each of the 4 time fields (mtime/ctime/atime/arctime)
    // has a 4-bit rmode nibble; the low bits give sub-second precision.
    public const int ExtTimeFieldCount = 4;
    public const int ExtTimeNibbleBits = 4;
    public const int ExtTimePresentBit = 0x8;     // time field is present
    public const int ExtTimeRoundUpBit = 0x4;     // +1s rounding
    public const int ExtTimePrecisionMask = 0x3;  // number of extra 100ns remainder bytes (0-3)
    public const int ExtTimeNibbleMask = 0xF;     // one rmode nibble

    // rmode nibble packing inside the ext-time flags word: mtime>>12, ctime>>8, atime>>4, arctime>>0.
    public const int MtimeNibbleShift = 12;       // << 12 / >> 12
    public const int CtimeNibbleShift = 8;
    public const int AtimeNibbleShift = 4;
    public const int MtimeNibbleMask = 0x0FFF;    // clear the mtime nibble

    // DOS date/time packing (FTIME).
    public const int DosSecondMask = 0x1F;      // *2 seconds
    public const int DosSecondEvenMask = 0x3E;  // encode: keep even seconds before >> 1
    public const int DosMinuteMask = 0x3F;
    public const int DosMinuteShift = 5;
    public const int DosHourShift = 11;
    public const int DosDayMask = 0x1F;
    public const int DosMonthMask = 0x0F;
    public const int DosMonthShift = 5;
    public const int DosYearMask = 0x7F;        // 7-bit year (years since 1980)
    public const int DosYearShift = 9;
    public const int DosEpochYear = 1980;
    public const int DosMaxYear = 2107;         // encode clamp (1980 + 0x7F)
```

- [ ] **Step 2: Adopt EXT_TIME constants** in `RARHeaderReader.cs` (reader decode `:584-620,812-829`), `RARDetailedHeader.cs` (`:895-970` display), `RARPatcher.cs` (`:355-370` mtime nibble; the remainder encode already uses named `mtimeByteCount`). Replace `& 0x8`→`& ExtTimePresentBit`, `& 0x4`→`& ExtTimeRoundUpBit`, `& 0x3`→`& ExtTimePrecisionMask`, `& 0xF`→`& ExtTimeNibbleMask`, `(3 - i) * 4`→`(ExtTimeFieldCount - 1 - i) * ExtTimeNibbleBits`, and the reader's explicit per-field shifts `>> 12`/`>> 8`/`>> 4` (`:812,819,826`)→`>> MtimeNibbleShift`/`>> CtimeNibbleShift`/`>> AtimeNibbleShift`, `<< 12`→`<< MtimeNibbleShift`, `& 0x0FFF`→`& MtimeNibbleMask`.

- [ ] **Step 3: Adopt DOS-date constants** in `RARUtils.DosDateToDateTime` (`:91-101`, consolidate its existing `dosEpochYear` onto `Rar4HeaderLayout.DosEpochYear`; name the `0x7F` year mask, `0x1F`/`0x3F`/`0x0F` masks, and `9`/`5`/`11` shifts) and `RARPatcher.EncodeDosDate` (`:280-287`, which re-inlines `1980` → `DosEpochYear`, and uses `0x3E`+`>> 1` even-seconds → `DosSecondEvenMask`, `& 0x7F` year → `DosYearMask`, and the `2107` clamp → `DosMaxYear`).

- [ ] **Step 4: Build + full lib suite green (no new failures).** The EXT_TIME test (Task 1), `RARPatcherTests` §"File Modified Times", and `RARHeaderReaderTests` precision tests are the guards here — they MUST stay green.

- [ ] **Step 5: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/RAR/Rar4HeaderLayout.cs ReScene.Lib/ReScene/RAR/RARHeaderReader.cs ReScene.Lib/ReScene/RAR/RARDetailedHeader.cs ReScene.Lib/ReScene/RAR/RARPatcher.cs ReScene.Lib/ReScene/RAR/RARUtils.cs
git commit -m "refactor(rar): name EXT_TIME and DOS-date bit-field constants"
```

---

### Task 4: Adopt flag enums + block-type enum

Replace raw flag/block-type literals with the existing enums, minding the `HasFlag` hazard.

**Files:**
- Modify: `RARHeaderReader.cs`, `RARDetailedHeader.cs`, `RARPatcher.cs`, `RARStream.cs`, `RARArchive.cs`, `RARUtils.cs` (only flag/block-type literal sites). (NOT `RARFileHeader.cs` — its convenience properties already use `RARFileFlags.*`; nothing to change.)

**Interfaces:**
- Consumes: existing `RARFileFlags`, `RARArchiveFlags`, `RAREndArchiveFlags`, `RAR4BlockType`, `RARFlagMasks` (all in `RARFlags.cs`/`RARBlockType.cs`).

- [ ] **Step 1: Replace single-bit flag tests with `HasFlag`.** For each `(flags & 0x….) != 0` on a single-bit flag: `(flags & 0x0100) != 0`→`((RARFileFlags)flags).HasFlag(RARFileFlags.Large)` (cast only if `flags` isn't already `RARFileFlags`); likewise `0x8000`→`LongBlock`, `0x0200`→`Unicode`, `0x1000`→`ExtTime`, `0x0400`→`Salt`, etc. Directory: `(flags & 0x00E0) == 0x00E0`→`…HasFlag(RARFileFlags.Directory)`.

- [ ] **Step 2: DO NOT touch the dict-size extraction.** Leave `flags & RARFlagMasks.DictionarySizeMask` as-is (it is already named). NEVER convert `DictSize*` members to `HasFlag`. If you find a raw `flags & 0x00E0` used to *extract* dict-size bits, name it `RARFlagMasks.DictionarySizeMask` (not `RARFileFlags.Directory`).

- [ ] **Step 3: Replace block-type literals.** `type == 0x74`→`type == (byte)RAR4BlockType.FileHeader`; `0x73`→`ArchiveHeader`; `0x7A`→`Service`; `0x72`→`Marker`; end-block/etc. per `RAR4BlockType`. Archive/end flags → `RARArchiveFlags`/`RAREndArchiveFlags` where raw.

- [ ] **Step 4: Build + full lib suite green (no new failures).** `RARFlagsTests`, `RARDetailedParserTests`, `RARArchiveTests` guard this.

- [ ] **Step 5: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/RAR/
git commit -m "refactor(rar): adopt RARFileFlags/RAR4BlockType enums for raw flag/type literals"
```

---

### Task 5: Consolidate markers

Adopt the existing `RARUtils.Rar4Marker`/`Rar5Marker`; alias-or-migrate the `RAR5HeaderReader.RAR5Marker` duplicate; inline the marker bytes only where a prefix/`SequenceEqual` check preserves the exact scan.

**Files:**
- Modify: `RAR5HeaderReader.cs` (`:420` duplicate; `:463` consumer), `RARUtils.cs` (`FindRarMarkerOffset :335-364`), `RARDetailedHeader.cs` (`IsValidRAR4Signature :388-397`; `Rar4Signature :236`).
- Test: `RAR5HeaderReaderTests.cs:58,64` (if the duplicate is removed).

**Interfaces:**
- Consumes: `RARUtils.Rar4Marker`/`Rar5Marker` (`ReadOnlySpan<byte>` properties).

- [ ] **Step 1: Resolve the `RAR5HeaderReader.RAR5Marker` duplicate.** Simplest safe option — make it a thin alias so the public API and its two tests keep compiling:

```csharp
// was: public static readonly byte[] RAR5Marker = [...];
public static byte[] RAR5Marker => RARUtils.Rar5Marker.ToArray();
```

(Alternatively remove it and change `RAR5HeaderReaderTests.cs:58` to `Assert.Equal(8, RARUtils.Rar5Marker.Length)` and `:64` to `Assert.True(RARUtils.Rar5Marker.SequenceEqual(expected))`. Pick one; the alias is lower-churn.) Also repoint the production consumer at `RAR5HeaderReader.cs:463` (`marker[i] != RAR5Marker[i]` in an 8-iteration loop) to `RARUtils.Rar5Marker` so the alias isn't allocated per-index.

- [ ] **Step 2: `IsValidRAR4Signature` → reuse the existing alias.** `RARDetailedHeader.cs:236` already exposes `Rar4Signature => RARUtils.Rar4Marker`. Replace the inline byte comparison in `IsValidRAR4Signature` (`:388-397`) with a `SequenceEqual`/prefix check against `RARUtils.Rar4Marker`, ONLY if it preserves the exact match (same length, same bytes). If the method checks a partial prefix, keep the exact semantics.

- [ ] **Step 3: `FindRarMarkerOffset` — inline consts only where byte-exact-safe.** This is a prefix scan that branches on byte 6/7 for RAR4-vs-RAR5 with a tail special-case (`:335-364`). Replace the raw byte literals in the prefix/branch with references to `RARUtils.Rar4Marker`/`Rar5Marker` indices ONLY where it does not change which offsets match. If a clean substitution isn't possible without altering the scan, leave the scan bytes and just name the marker lengths (`Rar4Marker.Length`/`Rar5Marker.Length`) — do not rewrite the algorithm.

- [ ] **Step 4: Build + full lib suite green (no new failures).** `RAR5HeaderReaderTests` (marker length/bytes), the SFX `FindRarMarkerOffset_*`/`Parse_SfxArchive*` tests, and `RARDetailedParserTests` (signature) guard this. (The SFX tests need `TestData/best_little/best_little_sfxgui.exe`, which is present locally.)

- [ ] **Step 5: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/RAR/ ReScene.Lib/ReScene.Tests/RAR5HeaderReaderTests.cs
git commit -m "refactor(rar): consolidate RAR markers onto RARUtils; drop duplicate"
```

---

### Task 6: RAR5 bit-fields

Add a `RAR5ArchiveFlags` enum and name the RAR5 vint/CompInfo masks.

**Files:**
- Modify: `RARFlags.cs` (add `RAR5ArchiveFlags`), `RAR5HeaderReader.cs`, `RARDetailedHeader.cs` (CompInfo duplicate `:1276-1279`).

**Interfaces:**
- Produces: `RAR5ArchiveFlags` enum.

- [ ] **Step 1: Add `RAR5ArchiveFlags`** to `RARFlags.cs` (values from `RAR5ArchiveInfo`'s raw literals):

```csharp
/// <summary>RAR 5.0 main-archive header flags.</summary>
[Flags]
internal enum RAR5ArchiveFlags : ulong
{
    None = 0x0000,
    Volume = 0x0001,
    VolumeNumber = 0x0002,
    Solid = 0x0004,
    RecoveryRecord = 0x0008,
    Locked = 0x0010
}
```

(Match the underlying type to how `ArchiveFlags` is stored — it is a vint; use `ulong` or the existing width. Verify against `RAR5ArchiveInfo.ArchiveFlags`'s type.)

- [ ] **Step 2: Adopt it in `RAR5ArchiveInfo`** (`RAR5HeaderReader.cs:123-143`): `(ArchiveFlags & 0x0001) != 0`→`((RAR5ArchiveFlags)ArchiveFlags).HasFlag(RAR5ArchiveFlags.Volume)`, etc.

- [ ] **Step 3: Name the vint + CompInfo masks.** Add `private const` (or a small `Rar5Format` static class) for the vint decode (`0x7F` data mask, `0x80` continuation bit, `63` max shift; `:522-533`) and the CompInfo unpacking (`& 0x3F` version, `& 0x40` solid bit, `>>7 & 0x07` method, `>>10 & 0x0F` dict, `128 << power` base) at `:225-235,706-708`, and apply the same names to the display duplicate in `RARDetailedHeader.cs:1276-1279` (which also carries the `& 0x40` solid bit at `:1277`).

- [ ] **Step 4: Build + full lib suite green (no new failures).** `RAR5HeaderReaderTests` (which exercise the vint/CompInfo masks) guard this.

- [ ] **Step 5: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/RAR/
git commit -m "refactor(rar): add RAR5ArchiveFlags and name RAR5 vint/CompInfo masks"
```

---

### Task 7: Host-OS table, method base, and sentinels

Name the remaining domain literals.

**Files:**
- Modify: `RARFlags.cs` (add `RarHostOs`), `RARPatcher.cs` (`GetHostOSName :242-251`), `RARArchive.cs` (`:273` method base), `RARHeaderReader.cs` (`:446` method base), `RARDetailedHeader.cs` (`0xFFFFFFFF` sentinels `:755,840,862`), `RARUtils.cs` (`0xFFFF` CRC mask `:54`).

**Interfaces:**
- Produces: `RarHostOs` enum, an `AsciiDigitZero` const.

- [ ] **Step 1: Add `RarHostOs`** enum (values from `GetHostOSName`'s 0-5 table) and adopt it there.

```csharp
/// <summary>RAR host OS codes (HOST_OS field).</summary>
internal enum RarHostOs : byte
{
    MsDos = 0, Os2 = 1, Windows = 2, Unix = 3, MacOs = 4, BeOs = 5
}
```

- [ ] **Step 2: Name the method ASCII base.** Add an in-scope `internal const byte AsciiDigitZero = 0x30;` (e.g. in `Rar4HeaderLayout` or `RARUtils`) and use it at `RARArchive.cs:273` (`(RARMethod)(AsciiDigitZero + method)`) and `RARHeaderReader.cs:446`. (Do NOT edit the `RARMethod` enum's file — it's in Decompression/Phase 5.)

- [ ] **Step 3: Name the sentinels/masks.** `0xFFFFFFFF` custom-packer sentinel (`RARDetailedHeader.cs:755,840,862`) → an `internal const uint CustomPackerSentinel = 0xFFFFFFFF;` (place near its use). `0xFFFF` CRC low-16 mask (`RARUtils.cs:54`) → `const ushort HeaderCrcMask = 0xFFFF;`.

- [ ] **Step 4: Build + full lib suite green (no new failures).** `RARPatcherTests.GetHostOSName` (`:288-301`), `RARArchiveTests`, `RARDetailedParserTests` guard this.

- [ ] **Step 5: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/RAR/
git commit -m "refactor(rar): name host-OS table, method base, and format sentinels"
```

---

## Final verification (after all tasks)

- [ ] No-warning build (both TFMs): `dotnet build ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental` → 0/0.
- [ ] Full lib suite green, no new failures vs the branch-start baseline.
- [ ] App suite green (`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/`) — the app references the lib.
- [ ] `git grep` the specific replaced literals in `ReScene.Lib/ReScene/RAR/*.cs` returns only constant definitions (no stray domain magic numbers remain in the targeted categories).
- [ ] Delete bin2 dirs. The whole-branch reviewer confirms every commit is a pure value-preserving substitution.
