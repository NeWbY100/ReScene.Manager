# SRR-Guided Volume Assembly — Design

Status: approved by user 2026-07-28 (verbal design walkthrough); codex review pending.
Scope: ReScene.Lib (`ReScene/Core/`) + one wiring touch in `Manager`. No UI changes.

## Problem

RAR reconstruction verifies candidates by byte-comparing produced volumes against the
originals' CRCs. The produced volumes must therefore be byte-identical, and today the
pipeline achieves that by patching the produced headers **in place** (host OS byte,
attributes, DOS mtime, EXT_TIME remainder, header CRCs — `RARPatcher`).

In-place patching cannot bridge **structural** header differences. Proven failing case
(`Golden.Age.Of.Racing-iTWINS`, logs `D:\Temp3\windows_log.txt` / `linnux_logs.txt`):

- Original (made by WinRAR 3.40 on Windows, `-tsm4`): file header 49 bytes, flags
  `0x90C2`, EXT_TIME present (5 bytes: flags + remainder).
- Linux `rarlinux-3.4.1` output, same switches: header 44 bytes, flags `0x80C2`,
  **no EXT_TIME field**. Unix builds of RAR 3.x read whole-second `st_mtime` and omit
  the field entirely; no switch can force it.
- Hex comparison proves the **compressed data streams are byte-identical**, shifted by
  exactly the 5 missing header bytes. Volume split points shift accordingly (vol 1
  packs 14,999,916 data bytes instead of 14,999,911). Every downstream byte cascades;
  all 89 Linux candidates hash `match: False` while Windows matches with `wrar340`.

Conclusion: when the local rar build's header *layout* differs from the original's,
patch-in-place can never match — on any OS, in either direction.

## Solution

Stop patching the produced container. **Assemble** the output volumes instead:

1. Take every header byte verbatim from the SRR (an SRR stores the complete original
   RAR headers — marker, archive header, file headers incl. EXT_TIME, service blocks
   incl. comment data, end blocks, padding).
2. Take the packed data from the brute-forced rar output, treated as one logical
   per-file packed stream (its container is irrelevant).
3. Re-split that stream at the original headers' ADD_SIZE boundaries.

Byte-perfect output by construction. Reconstruction becomes host-, filesystem- and
rar-build-agnostic: only the compressed stream must match, which is precisely what the
brute-force varies. Host-OS/attr/mtime patching is unnecessary on this path.

### Reuse (this is mostly plumbing, not new machinery)

- `SRRReconstructor.ReconstructAsync` (lib `Core/`) already walks an SRR and emits
  volumes: SRR bookkeeping blocks handled, embedded RAR header bytes written verbatim,
  `RARPadding` blocks emitted, Zip-Slip guard, per-volume verify + progress. Its data
  source is hardwired to raw release files (`FindSourceFile` + per-file `FileStream`) —
  correct only for store-method custom packers, which is its only caller today
  (`Manager` line ~241, gated on `CustomPackerDetected != None`).
- `RARStream(firstRARPath, packedFileName)` already provides a seekable read stream
  over one archived file's packed bytes **across volumes** of a RAR set. Opened on the
  produced set, it IS the produced-volumes data source.

## Architecture

### 1. Data-source seam in `SRRReconstructor`

Extract the packed-byte supply behind a small interface (one clear purpose: "give me
this archived file's packed stream"):

```
internal interface IPackedSource : IDisposable
{
    // Sequential per-file access; called once per archived file, in SRR order.
    // Returns a readable stream positioned at the file's packed byte 0.
    Stream OpenPackedStream(string archivedFileName);
}
```

- `ReleaseFilePackedSource` — current behavior (open the release file from the input
  directory; bytes are stored, packed == unpacked). Used by the custom-packer path,
  behavior unchanged.
- `ProducedVolumesPackedSource` — new. Wraps `RARStream(producedFirstVolumePath,
  archivedFileName)` per archived file. Directory entries never reach the source
  (existing guard). Reading past the currently-written produced volumes while rar is
  still completing them is a caller-level concern (see flow below) — the source itself
  simply reads; the caller ensures volume completion before full assembly.

`ReconstructAsync` keeps its signature plus an `IPackedSource` parameter (the existing
custom-packer call site passes `ReleaseFilePackedSource`). Its per-file open/copy/close
logic moves onto the seam; split-volume copy arithmetic (`packedSize` per piece,
SplitBefore/SplitAfter bookkeeping) is unchanged.

### 2. Set filtering (multi-set SRRs)

`ReconstructAsync` gains the rule: only RARFile sections whose volume name matches the
provided `originalRARFileNames` (OrdinalIgnoreCase, name-only comparison — consistent
with the pipeline's set keying) are emitted; other sets' sections are skipped without
opening output streams. The custom-packer path gets this fix for free.

### 3. `Manager` flow (the wiring change)

Engagement rule — the assembly path replaces patch+hash **iff**:

```
options.RAROptions.SRRFilePath is non-empty
&& CustomPackerDetected == None
&& the SRR carries no removed recovery records (see Limitations)
```

SFV-only runs (no SRR imported) keep the legacy patch+hash path untouched.

Per-candidate flow (replacing `PatchRARFilesHostOS` + first-volume hash):

1. rar finishes producing volume 1 (same trigger point as today).
2. Assemble original volume 1 only: SRR vol-1 section headers + the first
   `sum(vol1 ADD_SIZEs)` packed bytes via `ProducedVolumesPackedSource`.
3. Hash the assembled volume with `options.HashType`; compare against
   `options.Hashes` — the same quick-check contract as today.
4. Insufficient-data edge (produced headers larger than originals — the mirror
   direction, e.g. reconstructing a Unix-made original on Windows): assembled vol 1
   may need bytes from produced vol 2. If the packed stream runs dry and rar is still
   running, skip the quick check for this candidate, await volume completion, then
   assemble vol 1 and compare (log the reason at DEBUG).
5. On quick-check match: await completion of all produced volumes, assemble the full
   set, then run the existing `VolumeMatchEvaluator` per-volume CRC32 verification
   (CRCs from the SRR-embedded SFV, exactly as today) against the **assembled** files.
6. Finalization (move/rename into the user's output directory) consumes the assembled
   files — which already carry the original volume names — through the existing
   `MatchedRARWriter` finalize step. Assembled artifacts are written under a
   dedicated per-candidate subdirectory of the work area so they can never collide
   with rar's own `<slug>.rar` outputs, and non-matching candidates' assemblies are
   deleted under the same retention flags as rar outputs today
   (`DeleteRARFiles` / `DeleteDuplicateCRCFiles`).
7. Logging: candidate lines read `Assembled hash for <path>: <hash> (match: …)`;
   the match summary prints `SRR-guided assembly` instead of `(patched)`. The
   patch-describing log block is skipped on this path.

### 4. What the assembly path makes irrelevant

`EnableHostOSPatching`, detected host OS / attributes / mtimes, and the EXT_TIME
remainder patcher are not consulted on the assembly path (headers are verbatim). They
remain fully functional for the legacy SFV-only path. No settings change.

## Limitations (v1, recorded)

- **Recovery records.** SRRs strip RR data (`SRRBlockFlags.RecoveryBlocksRemoved`);
  those bytes cannot be assembled from the SRR, and produced-RAR RR bytes protect the
  wrong container. If the target set's embedded headers contain an RR/protect block
  (RAR4 old-style recovery `0x78`, or an `RR`-subtype service block) or a section
  flags `RecoveryBlocksRemoved`, assembly declines. Placement: the guard lives in the
  reconstructor as a typed decline result (it is the component that reads the SRR);
  the Manager translates a decline into one clear log line + legacy-patch fallback
  for that set. (Note: the current custom-packer direct path would silently mis-read
  such SRRs — the same reconstructor-level guard protects it too, surfacing as a
  reconstruction error there since it has no fallback.)
- **RAR4 only**, matching `SRRReconstructor`'s existing coverage. RAR5 reconstruction
  is out of scope exactly as it is today.
- No RR regeneration, no wine bridging, no UI toggle.

## Testing

Lib tests (`ReScene.Tests`), all synthetic, no WinRAR dependency:

1. **EXTTIME divergence (the bug):** build an "original" 3-volume set whose file
   headers carry EXT_TIME, derive the SRR (existing `SRRTestDataBuilder` machinery),
   then hand-build "produced" volumes containing the identical packed stream re-split
   with 5-bytes-shorter headers. Assemble → output byte-identical to originals
   (SHA-256 per volume).
2. **Mirror shift:** produced headers *larger* than originals (EXT_TIME present in
   produced, absent in originals) — exercises the read-across-volumes edge.
3. **Multi-file archive** spanning a volume boundary (SplitBefore/SplitAfter walk
   through the seam).
4. **Padding blocks** (`RARPadding`) interleaved — existing writer path still emits
   them in assembled output.
5. **Multi-set SRR:** two sets in one SRR; assembling set B touches no set-A volume
   and emits only set-B files.
6. **RR guard:** SRR section with `RecoveryBlocksRemoved` → assembly declines,
   diagnostic logged, legacy fallback signaled.
7. **Custom-packer regression:** existing direct-reconstruction tests green through
   the `ReleaseFilePackedSource` seam (byte-identical behavior).
8. **Manager integration:** quick-check + full-verify flow against a fake "rar
   producer" that drops pre-built produced volumes into the output directory
   (no real rar binary in tests).

Legacy-path regression: entire existing lib + App.Core + Manager suites stay green.

Acceptance smoke (manual, user): `Golden.Age.Of.Racing-iTWINS` reconstructs on Linux
with the `rarlinux-3.4.x` pack (`G:\WinRAR\extracted\Linux`), and still reconstructs
on Windows (now via assembly, since the SRR is imported in that flow too).

## Non-goals

RR regeneration; RAR5 SRR support; any UI surface (the path engages automatically and
announces itself in the Phase 2 log); performance work beyond "streams, no whole-set
buffering" (volumes are copied once; quick-check cost is one volume copy + hash, the
same order as today's patch+hash).
