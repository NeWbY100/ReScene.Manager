# SRR Magic-Number Elimination (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the domain numeric literals in `ReScene.Lib/ReScene/SRR/*.cs` with named constants — a behaviour-preserving refactor (zero output-byte change), replaying the Phase-1 (RAR) recipe and reusing the `Rar4HeaderLayout` it produced.

**Architecture:** Adopt the SRR enums that already exist (`SRRBlockType`, `SRRHeaderFlags`, `SRRBlockFlags`); consolidate the framing consts / CRC sentinels / OSO sizes duplicated across `SRRVerifier`/`SrrBlockWriter`/`SRREditor` into a new `SrrBlockLayout`; and where `SRRWriter` parses embedded RAR4 headers, reuse the Phase-1 `Rar4HeaderLayout` (extending it with `Method=25`). Every substitution is value-identical; the existing suites are the safety net.

**Tech Stack:** .NET (net8.0;net10.0 lib), xUnit. Spec: `docs/superpowers/specs/2026-07-04-srr-magic-numbers-design.md`.

## Global Constraints

- **Behaviour-preserving:** every named constant MUST equal the literal it replaces; no logic changes. Name by INTENT at each site (see overloaded literals below), not just by value.
- **Overloaded literals** (same value, different meaning — choose the right name per site):
  - **`7` — FOUR meanings:** SRR block base-header size → `SrrBlockLayout.BaseHeaderSize`; RAR4 marker byte-length → `RARUtils.Rar4Marker.Length`; RAR4 block base-header size → `Rar4HeaderLayout.BaseHeaderSize`; RAR4 ADD_SIZE field OFFSET → `Rar4HeaderLayout.AddSize`. **SRR-framing `7`s (`SRRFile.cs:498/530`, `SRRWriter.cs:416/439/453`, `SRRFileParser.cs:125`) are Task 3's `SrrBlockLayout`; RAR4-embedded `7`s (`SRRWriter.cs:508/514/529/542/560`) are Task 4's RAR constants.** `SRRFile.cs:498/530` already imports `ReScene.RAR`, so a wrong `Rar4HeaderLayout.BaseHeaderSize` there would compile silently — it is SRR framing.
  - **`8`:** OSO file-size length / OSO hash length → `SrrBlockLayout.OsoFileSizeLength`/`OsoHashLength`; RAR5 marker length → `RARUtils.Rar5Marker.Length`.
  - **`32`:** `Rar4HeaderLayout.HighPackSizeOffset` (LARGE) vs `FixedFieldsEnd` (filename base) — as Phase 1.
  - **`26`:** `Rar4HeaderLayout.NameSize` (offset) vs a `< 26` guard = `Method + 1`.
- **Scope:** `ReScene.Lib/ReScene/SRR/*.cs` only, plus a small `Method = 25` addition to `ReScene/RAR/Rar4HeaderLayout.cs`. NOT Decompression; not other namespaces.
- **Build/test ONLY with `-p:BaseOutputPath=bin2/`** (a running app locks `bin/`). NEVER kill the app. After verifying, delete bin2: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- **No-warning build:** `dotnet build ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental` → **0 warnings / 0 errors**.
- **Verification per task = existing suites stay green, NO new failures:** `dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/`. Baseline at branch start: 1196 pass. The SRR suites (`SRRWriterTests`, `SRRFileTests`, `SRRVerifierTests`, `SRREditor` tests, `SRRFileParser` coverage) plus the RAR suites (which guard the `Rar4HeaderLayout.Method` addition) prove no output byte changed.
- **CRITICAL repo:** nested submodule `E:/Projects/ReScene.NET/ReScene.Lib` (path contains `ReScene.NET`). The decoy clone `E:/Projects/ReScene.Lib` must NEVER be touched. Commit on lib `main`.
- Commit trailer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. App branch: `refactor/magic-numbers-srr`; gitlink bumped at the end.

## File structure

- Create: `ReScene.Lib/ReScene/SRR/SrrBlockLayout.cs`.
- Extend: `ReScene/RAR/Rar4HeaderLayout.cs` (add `Method = 25`); `ReScene/SRR/SRRBlock.cs` (packer-sentinel consts).
- Modify: `SRRWriter.cs`, `SRRFileParser.cs`, `SRRFile.cs`, `SRRVerifier.cs`, `SrrBlockWriter.cs`, `SRREditor.cs`.
- Remove duplicates: the private framing/sentinel consts in `SRRVerifier`, `SrrBlockWriter`, `SRREditor` (migrated into `SrrBlockLayout`).
- Tests: add the OSO-write and CMT-write characterization tests (Task 1) before their constants are renamed.

---

### Task 1: Pin the two untested write paths

`WriteOSOHashBlock` (the `7+8+8+2` framing) and `IsRar4CmtServiceBlock` are exercised only indirectly / on the parse path; a wrong constant there would be undetected. Add characterization tests FIRST.

**Files:**
- Test: `ReScene.Lib/ReScene.Tests/SRRWriterTests.cs` (or `SRRFileTests.cs` — wherever the SRR create/round-trip fixtures live).

**Interfaces:**
- Produces: regression fixtures the later tasks rely on staying green.

- [ ] **Step 1: Read the OSO write path + a hashable fixture.** Read `SRRWriter.WriteOSOHashBlock` (~`SRRWriter.cs:445-460`) and how `CreateAsync` triggers it (the `ComputeOSOHashes`/OSO option). Find how existing tests build an SRR with a real RAR volume (e.g. `SRRWriterTests` create-path fixtures).

- [ ] **Step 2: Write an OSO-write characterization test.** Drive `CreateAsync(..., ComputeOSOHashes: true)` (match the real option name — the reviewer confirmed `SRRCreationOptions.ComputeOSOHashes` → `WriteOSOHashBlock`) over a fixture that produces at least one OSO block, then reload and assert the OSO block's exact fields (file size, 8-byte hash, name) — pinning the `7+8+8+2` framing. NOTE: OSO/ISDb hashing only emits a block when the archived content is large enough to hash — use an adequately-sized fixture (a too-small one yields no block and the `Assert.Single` will fail loudly, not vacuously pass). If driving OSO end-to-end is impractical, assert `WriteOSOHashBlock`'s bytes directly (may need `internal` visibility / `InternalsVisibleTo`). Example shape (adapt to the real API):

```csharp
[Fact]
public async Task CreateAsync_WithOsoHashes_EmitsOsoBlockWithExactFraming()
{
    // ... build an SRR with OSO hashing enabled over a real RAR fixture ...
    SRRFile srr = SRRFile.Load(outputPath);
    OSOHashBlock oso = Assert.Single(srr.OSOHashBlocks);
    Assert.Equal(/* known file size */, oso.FileSize);
    Assert.Equal(/* known 8-byte hash hex */, Convert.ToHexString(oso.Hash));
    Assert.Equal(/* known name */, oso.FileName);
}
```

- [ ] **Step 3: Write a CMT-write characterization test.** Build/parse an SRR whose RAR4 stream contains a CMT service block, and assert the create path's handling (whatever `IsRar4CmtServiceBlock` gates — e.g. that the comment is recorded / the warning path taken). Use an existing CMT fixture if `SRRFileTests` has one (parse-path CMT is tested via `RARHeaderReader`; reuse that RAR fixture on the create path).

- [ ] **Step 4: Run — both new tests PASS (they pin current behaviour).** `dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~SRRWriterTests|FullyQualifiedName~SRRFileTests"` → PASS.

- [ ] **Step 5: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene.Tests/
git commit -m "test(srr): pin OSO-write framing and CMT-write detection before magic-number refactor"
```

---

### Task 2: Extend `Rar4HeaderLayout` with `Method = 25`

Tiny cross-phase extension the RAR suites guard; do it standalone so Task 4 can consume it.

**Files:**
- Modify: `ReScene/RAR/Rar4HeaderLayout.cs`.

**Interfaces:**
- Produces: `Rar4HeaderLayout.Method` (=25).

- [ ] **Step 1: Add the constant.** In the file-header fixed-fields group (next to `HostOs=15, FileTime=20, NameSize=26, Attr=28`):

```csharp
    public const int UnpVer = 24;   // UNP_VER byte
    public const int Method = 25;   // METHOD byte (0x30=Store, 0x31-0x35=compressed)
```

(`Method=25` verified: base 7 + PACK 4 + UNP 4 + HOST_OS 1 + FILE_CRC 4 + FTIME 4 = 24 → UNP_VER@24, METHOD@25. `UnpVer` is optional but completes the run.)

- [ ] **Step 2: Build + full lib suite green (no new failures).** The RAR suites guard the layout.

```
dotnet build ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/
```

- [ ] **Step 3: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/RAR/Rar4HeaderLayout.cs
git commit -m "refactor(rar): add Rar4HeaderLayout.Method/UnpVer offsets (for SRR reuse)"
```

---

### Task 3: `SrrBlockLayout` — consolidate SRR framing, CRC sentinels, OSO sizes; adopt SRR enums

Create the SRR single source of truth, migrate the three files' private duplicates, adopt it plus the existing `SRRBlockType`/flag enums at the SRR-framing sites.

**Files:**
- Create: `ReScene/SRR/SrrBlockLayout.cs`
- Modify: `SRRVerifier.cs` (delete its private framing+sentinel consts, reference layout), `SrrBlockWriter.cs` + `SRREditor.cs` (delete their private framing consts, reference layout), `SRRWriter.cs`, `SRRFile.cs`, `SRRFileParser.cs`.

**Interfaces:**
- Consumes: existing `SRRBlockType`, `SRRHeaderFlags`, `SRRBlockFlags` (in `SRRBlock.cs`).
- Produces: `internal static class SrrBlockLayout`.

- [ ] **Step 1: Create `SrrBlockLayout.cs`.** Values verbatim from the existing `SRRVerifier` private consts:

```csharp
namespace ReScene.SRR;

/// <summary>
/// SRR block framing constants — the single source of truth for the byte sizes and CRC "sentinels"
/// shared by the SRR writer, editor, and verifier. Values mirror the on-disk SRR format exactly.
/// </summary>
internal static class SrrBlockLayout
{
    // Base SRR block header: CRC(2) + Type(1) + Flags(2) + Size(2).
    public const int BaseHeaderSize = 7;
    public const int AddSizeFieldLength = 4;     // ADD_SIZE / data-length field
    public const int NameLengthFieldLength = 2;  // inline name-length prefix (framing only)

    // Each SRR block's 2-byte CRC is a fixed sentinel, not a real CRC.
    public const ushort HeaderSentinel     = 0x6969;
    public const ushort StoredFileSentinel = 0x6A6A;
    public const ushort OSOSentinel        = 0x6B6B;
    public const ushort RARPaddingSentinel = 0x6C6C;
    public const ushort RARFileSentinel    = 0x7171;

    // OSO (OpenSubtitles) hash-block payload field sizes.
    public const int OsoFileSizeLength   = 8;    // ulong file size
    public const int OsoHashLength       = 8;    // 8-byte hash
    public const int OsoFixedPayloadSize = OsoFileSizeLength + OsoHashLength + NameLengthFieldLength; // 18
}
```

- [ ] **Step 2: Migrate the three consumers.** Delete the private `BaseHeaderSize`/`AddSizeFieldLength`/`NameLengthFieldLength` consts in `SRRVerifier` (`:10-16`, also its 5 sentinel consts), `SrrBlockWriter` (`:11-13`), `SRREditor` (`:11-13`); replace their uses with `SrrBlockLayout.*`. `SRRVerifier` now references `SrrBlockLayout.*Sentinel`.

- [ ] **Step 3: Adopt the SRR framing consts at the remaining raw sites.** `SRRWriter.cs:416` (`7`→`BaseHeaderSize`), `:439` (`7 + 2 + …`→`BaseHeaderSize + NameLengthFieldLength + …`), `:453` (`7 + 8 + 8 + 2`→`BaseHeaderSize + OsoFileSizeLength + OsoHashLength + NameLengthFieldLength`). `SRRFileParser.cs:125` (the `7 + 4 + 2` local → the three named consts). `SRRFile.cs:498` (`+ 7`) and `:530` (`< 7`) → `SrrBlockLayout.BaseHeaderSize`; `SRRFile.cs:522` (`+ 4`) → `SrrBlockLayout.AddSizeFieldLength`. OSO sizes: `SRRFileParser.cs:50` (`18`→`OsoFixedPayloadSize`), `:56` (`ReadBytes(8)`→`OsoHashLength`).

- [ ] **Step 4: Adopt CRC sentinels + block-type bytes + flag words at the writer/editor sites.**
  - Sentinels: `SRRWriter.cs:424` (`0x6969`→`SrrBlockLayout.HeaderSentinel`), `:441` (`0x7171`→`RARFileSentinel`), `:455` (`0x6B6B`→`OSOSentinel`); `SrrBlockWriter.cs:25` (`0x6A6A`→`StoredFileSentinel`); `SRREditor.cs:237` (`0x6A6A`→`StoredFileSentinel`).
  - Block-type bytes: `SRRWriter.cs:425` (`0x69`→`(byte)SRRBlockType.Header`), `:442` (`0x71`→`RARFile`), `:456` (`0x6B`→`OSOHash`); `SrrBlockWriter.cs:26` (`0x6A`→`StoredFile`); `SRRFileParser.cs:12` (the `is 0x69 or 0x6A or 0x6B or 0x6C or 0x71` guard → `(byte)SRRBlockType.*`), `:296` (`>= 0x69 and <= 0x71`→`(byte)SRRBlockType.Header`/`RARFile`).
  - Flag words: `SRRWriter.cs:414` (`0x0001`→`(ushort)SRRHeaderFlags.AppNamePresent`, else `0x0000`→`.None`), `:443`/`:457` (`0x0000`→`(ushort)SRRBlockFlags.None`); `SrrBlockWriter.cs:27` (`0x8000`→`(ushort)SRRBlockFlags.LongBlock`). (Confirm exact enum names in `SRRBlock.cs` first.)

- [ ] **Step 5: Build + full lib suite green (no new failures).** `SRRVerifierTests`, `SRRWriterTests`, `SRRFileTests`, `SRREditor` tests guard this.

- [ ] **Step 6: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRR/
git commit -m "refactor(srr): add SrrBlockLayout; adopt SRRBlockType/flags + consolidate sentinels"
```

---

### Task 4: Reuse `Rar4HeaderLayout` for SRRWriter's embedded RAR4 parsing (the four-way `7`)

`SRRWriter` (and a few `SRRFileParser` sites) read raw embedded RAR4 headers. Adopt the Phase-1 RAR constants — carefully, since `7`/`8`/`32`/`26` are overloaded.

**Files:**
- Modify: `SRRWriter.cs`, `SRRFileParser.cs`.

**Interfaces:**
- Consumes: `Rar4HeaderLayout.*` (incl. `Method` from Task 2), `RARUtils.Rar4Marker`/`Rar5Marker`, `RARFileFlags`.

- [ ] **Step 1: Marker lengths (RAR4-embedded, NOT SRR framing).** `SRRWriter.cs:508` (`7`, RAR4 marker check) and `:514` (`ReadBytes(7)`) → `RARUtils.Rar4Marker.Length`; `:694` (`8`, RAR5 marker) and `:700` (`ReadBytes(8)`) → `RARUtils.Rar5Marker.Length`; `SRRFileParser.cs:281` (`Seek(8,…)`) and `:282` (`+= 8`) → `RARUtils.Rar5Marker.Length`.

- [ ] **Step 2: RAR4 base-header guards + ADD_SIZE.** `SRRWriter.cs:529` and `:542` (`7`, base-header guards inside `ProcessRar4Volume`) → `Rar4HeaderLayout.BaseHeaderSize`; `:560` (`blockStart + 7`, ADD_SIZE field offset) → `Rar4HeaderLayout.AddSize`. (These are RAR4-embedded 7s — distinct from the SRR-framing 7s already done in Task 3.)

- [ ] **Step 3: HIGH_PACK_SIZE.** `SRRWriter.cs:574` (`headerSize >= 36`) → `Rar4HeaderLayout.HighPackSizeOffset + Rar4HeaderLayout.AddSizeFieldLength`; `:576` (`ToUInt32(headerBytes, 32)`) → `Rar4HeaderLayout.HighPackSizeOffset`.

- [ ] **Step 4: METHOD / NAME_SIZE / filename.** `SRRWriter.cs:649` (`headerBytes[25]`) → `headerBytes[Rar4HeaderLayout.Method]`; `:650` (`0x30`, and `SRRFileParser.cs:561/569/650`) → `Rar4HeaderLayout.AsciiDigitZero`; `:656`/`:675` (`ToUInt16(headerBytes, 26)`) → `Rar4HeaderLayout.NameSize`; `:657-658`/`:681` (filename base `32`) → `Rar4HeaderLayout.FixedFieldsEnd`; `:644` (`headerSize < 26` guard) → expressed via `Rar4HeaderLayout.Method` (need `≥ Method + 1` bytes; e.g. `< Rar4HeaderLayout.Method + 1`). CMT sub-type: `:670` (`headerSize < 35`) → `Rar4HeaderLayout.FixedFieldsEnd + CmtSubTypeLength` with a method-local `const int CmtSubTypeLength = 3`; `:676`/`:681` (`3`/`GetString(...,32,3)`) → `CmtSubTypeLength` / `FixedFieldsEnd`.

- [ ] **Step 5: Build + full lib suite green (no new failures).** The Task-1 CMT-write test, the compressed-file test (`SRRWriterTests`), and the OSO test guard this; the RAR suites guard the layout.

- [ ] **Step 6: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRR/
git commit -m "refactor(srr): reuse Rar4HeaderLayout for embedded RAR4 header parsing in SRRWriter"
```

---

### Task 5: Packer sentinels, RAR-version tag, and remaining SRR literals

**Files:**
- Modify: `SRRBlock.cs` (packer-sentinel consts), `SRRFileParser.cs`.

**Interfaces:**
- Produces: `PackerSentinelAllOnes`/`PackerSentinelMaxUint32` (next to `CustomPackerType`).

- [ ] **Step 1: Name the custom-packer sentinels.** `CustomPackerType` is a `public enum`, so the
  consts CANNOT live inside it. Put them in a host TYPE — either add them to `SrrBlockLayout` (created
  in Task 3, the SRR constants home) or a new `internal static class SrrPackerSentinels` in
  `SRRBlock.cs` next to `CustomPackerType`. Qualify the `SRRFileParser.cs` references to whichever host
  you choose.

```csharp
/// <summary>UNP_SIZE all-ones with LARGE flag (both 32-bit halves = 0xFFFFFFFF) — non-WinRAR packer.</summary>
internal const ulong PackerSentinelAllOnes = 0xFFFFFFFFFFFFFFFFUL;
/// <summary>UNP_SIZE 0xFFFFFFFF without LARGE flag — non-WinRAR packer.</summary>
internal const uint PackerSentinelMaxUint32 = 0xFFFFFFFFU;
```

Adopt at `SRRFileParser.cs:379` (`0xFFFFFFFFFFFFFFFF` → `PackerSentinelAllOnes`) and `:386` (`0xFFFFFFFF` → `PackerSentinelMaxUint32`). VERIFY the compared operand's type matches (`header.UnpackedSize` is `ulong`; the `uint` sentinel widens correctly) so the comparison result is unchanged. This branch is covered by `SRRFileTests`' `CustomPackerType` cases.

- [ ] **Step 2: Name the RAR-version tag.** Add `private const int RarVersion50 = 50;` to `SRRFileParser` and use it at `:285`, `:563`, `:571` (the three `50` / `RARVersion = 50` sites).

- [ ] **Step 3: Build + full lib suite green (no new failures).** `SRRFileTests` (custom-packer + RAR5 parse) guard this.

- [ ] **Step 4: Delete bin2, commit.**

```bash
find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null
git add ReScene.Lib/ReScene/SRR/
git commit -m "refactor(srr): name custom-packer sentinels and the RAR 5.0 version tag"
```

---

## Final verification (after all tasks)

- [ ] No-warning build (both TFMs): `dotnet build ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental` → 0/0.
- [ ] Full lib suite green, no new failures vs the branch-start baseline (1196 + Task-1 additions).
- [ ] App suite green (`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/`).
- [ ] `git grep` the replaced literals in `ReScene.Lib/ReScene/SRR/*.cs` returns only definitions (the deliberately-raw parser name-length `2`s per the spec's non-goals are expected).
- [ ] Delete bin2 dirs. Bump the app gitlink on `refactor/magic-numbers-srr`. Whole-branch reviewer confirms every commit is a pure value-preserving substitution and each overloaded `7`/`8`/`32`/`26` got the intent-correct name.
