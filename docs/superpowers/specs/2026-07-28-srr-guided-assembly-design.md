# SRR-Guided Volume Assembly — Design

Status: rev 3 — user-approved walkthrough 2026-07-28; codex rev-1 REVISE (7B/3A) and rev-2 REVISE (2B/3A) folded in
Rev 3 codex re-review pending.
Scope: ReScene.Lib (`ReScene/Core/`) + one wiring touch in `Manager`. No UI changes.

## Problem

RAR reconstruction verifies candidates by byte-comparing produced volumes against the
originals' CRCs. The produced volumes must therefore be byte-identical, and today the
pipeline achieves that by patching the produced headers **in place** (host OS byte,
attributes, DOS mtime, EXT_TIME remainder, header CRCs — `RARPatcher`; it can also
structurally rewrite LARGE fields within one file). What the patcher cannot do is
**globally re-split the packed stream across volumes** after a header-layout
divergence has shifted every volume's split point.

Proven failing case (`Golden.Age.Of.Racing-iTWINS`, logs `D:\Temp3\windows_log.txt` /
`linnux_logs.txt`):

- Original (WinRAR 3.40 on Windows, `-tsm4`): file header 49 bytes, flags `0x90C2`,
  EXT_TIME present (5 bytes: flags word + 3-byte remainder).
- Linux `rarlinux-3.4.1` output, same switches: header 44 bytes, flags `0x80C2`,
  **no EXT_TIME field**. Unix builds of RAR 3.x read whole-second `st_mtime` and omit
  the field entirely; no switch can force it.
- Hex comparison proves the **compressed data streams are byte-identical**, shifted by
  exactly the 5 missing header bytes. Volume split points shift accordingly (vol 1
  packs 14,999,916 data bytes instead of 14,999,911); all 89 Linux candidates hash
  `match: False` while Windows matches with `wrar340`.

## Solution

Stop patching the produced container. **Assemble** the output volumes instead:

1. Take every header byte verbatim from the SRR (an SRR stores the complete original
   RAR headers — marker, archive header, file headers incl. EXT_TIME, the comment
   service block incl. its data, end blocks, padding).
2. Take the packed data from the brute-forced rar output, treated as one logical
   per-file packed stream (its container is irrelevant).
3. Re-split that stream at the original headers' ADD_SIZE boundaries.

Byte-perfect output by construction; only the compressed stream must match, which is
precisely what the brute-force varies. Header patching is unnecessary on this path.

### Reuse

- `SRRReconstructor.ReconstructAsync` already walks an SRR and emits volumes (SRR
  bookkeeping blocks, embedded RAR header bytes verbatim, `RARPadding` emission,
  Zip-Slip guard, progress). Its data source is hardwired to raw release files —
  correct only for its sole caller, the store-method custom-packer path
  (`Manager` ~line 241).
- `RARStream(firstRARPath, packedFileName)` provides a seekable read stream over one
  archived file's packed bytes across the volumes of a RAR set (cross-volume
  `Read`/`Seek` verified suitable). **Caveat (codex B3): it snapshots the volume list
  at construction and never discovers volumes created later** — the flow below
  therefore constructs fresh instances at defined points, never reuses one across a
  producer state change.

## Architecture

### 1. Data-source seam + outcome type in `SRRReconstructor`

```
internal interface IPackedSource : IDisposable
{
    // Sequential per-file access, in SRR order; stream positioned at packed byte 0.
    Stream OpenPackedStream(string archivedFileName);
}
```

- `ReleaseFilePackedSource` — current behavior (release file from the input
  directory; store-method: packed == unpacked). Custom-packer path, unchanged.
- `ProducedVolumesPackedSource` — new; wraps `RARStream(producedFirstVolumePath,
  archivedFileName)` per archived file. Instances are cheap and **single-snapshot**:
  the Manager creates a fresh source (fresh `RARStream`s) for each assembly attempt.

Archived-name handling (codex B6): the reconstructor currently decodes header names
as ASCII + NUL-truncate, while `RARStream` matches against Unicode/OEM-decoded
`RARFileHeader.FileName`. The seam refactor decodes with `RARUtils.DecodeFileName`
(honoring the LHD_UNICODE flag and the LARGE-shifted name offset) so both sides of
the seam speak the same names. A Unicode archived-name test pins this.

Outcome type (codex B1): the `(bool, IReadOnlyList<string>)` tuple cannot express why
assembly did not produce a verified set. `ReconstructAsync` returns:

```
internal enum SRRReconstructionStatus
{
    Success,            // all requested volumes written (and verified, where CRCs exist)
    UnsupportedSrr,     // preflight declined: required payload not present in the SRR
    SourceExhausted,    // packed source ended before the last requested ADD_SIZE byte
    VerificationFailed, // volumes written but hash comparison failed
    Error               // I/O or parse failure
}
record SRRReconstructionResult(
    SRRReconstructionStatus Status,
    IReadOnlyList<string> WrittenPaths,   // ordered, exactly as emitted
    string? Diagnostic);
```

The custom-packer call site maps `Success` to its current `true` and everything else
to its current `false` + logged diagnostic — observable behavior preserved, honesty
gained.

### 2. Preflight (codex B1 — replaces the rev-1 "RR flag" guard, which was wrong)

`SRRBlockFlags.RecoveryBlocksRemoved` is set **unconditionally** by `SRRWriter`
(SRRWriter.cs:722) even when no recovery record ever existed — it is not evidence and
MUST NOT gate anything. A flag-only SRR with no actual RR content remains eligible.

The real hazard: the SRR stores payload only for the comment (CMT) service block;
`SRRWriter` strips the data of every other data-bearing embedded block (other service
blocks such as AV, and data-bearing old-style blocks — SRRWriter.cs:916/942), yet the
current reconstructor trusts each embedded block's declared ADD_SIZE and would consume
the following headers as payload (SRRReconstructor.cs:291/311) — a latent bug in the
custom-packer path too.

**Preflight rule:** before any output file is created, walk the selected set's
sections and decline (`UnsupportedSrr`) if any embedded block's payload is required
but not stored in the SRR:

- archive header with the Protected flag, old-style recovery block `0x78`, or an
  `RR`-subtype service block → decline (recovery data unreconstructible);
- any other data-bearing block whose payload the SRR format strips (non-CMT service
  blocks with ADD_SIZE > 0, data-bearing old-style blocks) → decline;
- CMT service blocks (payload stored) and all pure-header blocks → eligible.

The preflight is an **explicit reconstructor API** (e.g.
`SRRReconstructor.PreflightSet(srrPath, originalRARFileNames)`), and the Manager
invokes it **once per set, before entering the producer/candidate loop** (codex
rev-2 B1). On `UnsupportedSrr` the Manager selects legacy mode for the whole set
before launching any candidate — a decline can therefore never occur mid-candidate,
no candidate is ever cancelled because of one, and the first version/argument
combination is evaluated through the legacy path like every other. "Decline" is NOT
one of the running-producer lifecycle exits.

### 3. Set filtering (codex B5 — directory-qualified, not name-only)

Multi-set SRRs can contain `CD1/x.rar` and `CD2/x.rar`. Matching a section to the
requested set uses **separator-normalized, case-insensitive relative names** whenever
the provided `originalRARFileNames` are directory-qualified (mirroring
`Manager`'s existing qualified-key-then-basename lookup and
`RARVolumeIdentifier.GetArchiveSetKey` semantics). A bare-basename fallback is
accepted only when the basename is unique across the SRR. The multi-set test uses
identical basenames in different directories.

### 4. `Manager` flow

Engagement rule — the assembly path replaces patch+hash **iff**
`SRRFilePath` is non-empty AND `CustomPackerDetected == None` AND the per-set
preflight (run once, before the candidate loop) did not decline. SFV-only runs (no
SRR) keep the legacy patch+hash path untouched. On decline: one log line, and the
entire set runs the legacy path from candidate one — described honestly as "trying
legacy reconstruction" (for an actually-Protected original the legacy path will also
fail verification; it is a diagnostic courtesy, not an RR solution).

**CAV split (codex B2).** The two lifecycles differ fundamentally — non-CAV mode
kills rar as soon as volume 2 appears (Manager.cs:447/474/783), so "await completion"
does not exist there:

- **CompleteAllVolumes = true** (recreate-whole-release): at the existing
  vol-2-exists trigger, attempt the quick check — assemble original volume 1 only
  (fresh `ProducedVolumesPackedSource`), hash with `options.HashType`, compare
  against `options.Hashes` (same contract as today). Any failure while the producer
  is still running — short read, missing/incomplete header, sharing or parse error,
  `SourceExhausted` — is treated as an *incomplete snapshot*, not a mismatch: await
  producer completion, retry ONCE with an entirely fresh source (codex B3). Failure
  after that retry is a real no-match. On quick match: await completion, assemble
  the full set (fresh source again), then per-volume verification (next paragraph),
  then finalize. Post-retry outcome mapping (codex rev-2 A3): `SourceExhausted` or
  `VerificationFailed` after producer completion = a real no-match for this
  candidate; a persistent parse/I-O `Error` = a failed combination, surfacing
  through the Manager's existing error-row behavior — the two are not conflated.
- **CompleteAllVolumes = false** (fast version hunt): first-volume-only assembly
  outcome. Assemble original volume 1 from the produced volume-1 data available at
  the kill point; on success report the match exactly as the legacy first-volume
  path does. If the source runs short (the mirror-shift direction needs produced
  vol-2 bytes that were never written), the candidate is logged as *inconclusive —
  enable "Complete all volumes" to test this candidate* (once per set at INFO,
  per-candidate at DEBUG) and treated as no-match. This is an explicit, logged v1
  trade-off, not silent.

**Verification coverage parity (codex B2).** Per-volume CRC verification engages
exactly as today: only in CAV mode and only when `BuildExpectedInOrder` is non-empty
(an imported SRR need not embed an SFV). With no CRC map, the assembly path preserves
today's first-hash-only success semantics — no silent strengthening or weakening.

**Single hashing responsibility (codex A2).** The Manager owns all match hashing; the
reconstructor's internal per-volume verify is disabled on Manager calls (hashes
parameter empty ⇒ `VerifyAndReportVolumeAsync` no-ops, as it already does). Honest
cost statement (codex rev-2 A5): in CAV mode the quick gate costs ONE EXTRA copy +
hash of volume 1 (the full-set assembly then re-emits and re-verifies it) — accepted;
no reuse mechanism is specified. Beyond that, each assembled volume is copied once
and hashed once.

**Producer lifecycle hygiene (codex B3).** Every non-winning exit from a candidate —
quick mismatch, inconclusive, exception, cancellation — cancels AND observes
(awaits) the running rar process before cleanup or the next candidate; today's
generic exception path only disposes the CTS (Manager.cs:968/999). This is a bug-fix
requirement of the rewiring, tested explicitly. (Preflight declines are not in this
list — they happen before any producer exists.)

### 5. Finalization + retention (codex B4 — new finalizer, not `RenameMatchedOutput`)

`RenameMatchedOutput` rediscovers volumes from the rar-produced `rarFilePath` and
patches them — pointed at an assembly win it would finalize the *carrier* volumes.
The assembly path gets a dedicated finalizer:

- input: the reconstructor's ordered `WrittenPaths`, verbatim — no volume
  rediscovery, no patching;
- action: transactional move into `<workRoot>/output` (satisfying the app-side
  `VerifiedOutputRelocator` contract, which only accepts committed files there);
- naming policy (codex rev-2 B2 — `RenameToOriginalNames` is a live setting and
  "no settings change" means honoring it): when `RenameToOriginalNames` is true,
  the original volume names; when false, a collision-free generated convention
  `<candidate-slug>-assembled.<ext>` per volume — carrier filenames cannot be
  reused because success may retain the carriers (`DeleteRARFiles=false`). Tests
  cover both toggle values × both carrier-retention values;
- assembled artifacts live under a per-candidate work subdirectory
  (`assembled-<candidate-slug>/`) so they can never collide with rar's own outputs.

Retention matrix — BOTH artifact classes (assembled volumes AND rar-produced carrier
volumes) have defined dispositions for: quick mismatch, full-verification mismatch,
duplicate-hash candidate, exception, cancellation, and success. Mismatch/duplicate
honor the existing `DeleteRARFiles` / `DeleteDuplicateCRCFiles` flags for both
classes; success deletes the carrier volumes (they are not the reconstruction) unless
`DeleteRARFiles` is false, in which case they remain in the work area for debugging;
exception/cancellation leave both classes for diagnosis, as today.

### 6. What the assembly path makes irrelevant

`EnableHostOSPatching`, detected host OS / attributes / mtimes, and the EXT_TIME
remainder patcher are not consulted on the assembly path (headers are verbatim). They
remain fully functional for the legacy path. No settings change.

## Limitations (v1, recorded)

- Recovery-record-bearing originals (real evidence: Protected flag / `0x78` / `RR`
  service): assembly declines via preflight; legacy is tried and will honestly fail
  verification for such sets. RR regeneration is out of scope.
- Stripped data-bearing blocks other than CMT (e.g. AV): decline, same mechanism.
- Non-CAV mirror-shift candidates: inconclusive with explicit guidance (see CAV
  split) — not a wrong answer, a logged narrower one.
- RAR4 only, matching `SRRReconstructor`'s existing coverage.
- No wine bridging, no UI toggle.

## Testing

Test-infra work this feature REQUIRES (codex B7 — the rev-1 plan was not
implementable):

- `RAR4HeaderBuilder`: emit a real 5-byte EXT_TIME field (flags word + 3-byte
  remainder), not just the flags word — needed to build "original" fixtures.
- `SRRTestDataBuilder`: support embedded RAR header sections with caller-controlled
  flags (today it hardcodes section flags to 0), including the real-world
  `RecoveryBlocksRemoved`-always-set shape.
- `Manager` candidate-flow seam: an injected producer/attempt-runner abstraction (or
  extracted candidate-flow component) so integration tests can drop pre-built
  "produced" volumes without a real rar binary (`RARProcess` is internal sealed and
  directly constructed today — Manager.cs:746).

Lib tests (all synthetic, no WinRAR dependency):

1. EXTTIME divergence (the bug): original 3-volume set WITH EXT_TIME, SRR built from
   it, produced volumes carrying the identical packed stream re-split with
   5-byte-shorter headers → assembled output byte-identical (SHA-256 per volume).
2. Mirror shift (produced headers larger; read crosses produced-volume boundary).
3. Multi-file archive spanning a volume boundary (SplitBefore/SplitAfter through the
   seam).
4. Padding blocks interleaved.
5. Multi-set SRR with identical basenames in different directories (CD1/CD2):
   assembling set B touches no set-A volume.
6. Preflight eligibility: flag-only `RecoveryBlocksRemoved` (no actual RR) remains
   ELIGIBLE — the real-world default shape must assemble.
7. Preflight declines, each before any output file exists: Protected archive header;
   old-style `0x78`; `RR` service block; stripped data-bearing AV service block.
8. Incomplete-snapshot race (codex rev-2 A4 — the Manager's trigger waits for
   vol-2-or-completion, so "vol 2 missing while running" cannot be the fixture):
   volume 2 EXISTS at the trigger but its target header/data is incomplete until
   producer completion → first attempt fails as an incomplete snapshot, the single
   post-completion retry with a fresh source succeeds. A second case pins the
   post-retry mapping: still-short source after completion → no-match
   (`SourceExhausted`), persistent parse failure → error row (`Error`).
9. Producer lifecycle: every failure path (mismatch, inconclusive, exception,
   cancel) cancels and observes the fake producer; a preflight decline launches NO
   producer at all and the set runs legacy from candidate one.
10. CAV and non-CAV flows, including the non-CAV inconclusive mirror case.
11. No-CRC-map SRR: first-hash-only semantics preserved.
12. Unicode archived name through the seam (LHD_UNICODE + LARGE offset).
13. Finalization commits the ASSEMBLED paths (not carrier paths) into
    `<workRoot>/output`; naming policy both ways (`RenameToOriginalNames` true/
    false) x carrier retention both ways; retention matrix cases (mismatch,
    duplicate, exception, cancellation, success) for both artifact classes.
14. Custom-packer regression: existing direct-reconstruction behavior byte-identical
    through `ReleaseFilePackedSource`; plus the preflight now protecting it.

Legacy-path regression: entire existing lib + App.Core + Manager suites stay green.

Acceptance smoke (manual, user): `Golden.Age.Of.Racing-iTWINS` reconstructs on Linux
with the `rarlinux-3.4.x` pack (`G:\WinRAR\extracted\Linux`), and still reconstructs
on Windows (now via assembly, since that flow imports the SRR too).

## Non-goals

RR regeneration; RAR5 SRR support; any UI surface (the path engages automatically
and announces itself in the Phase 2 log); performance work beyond "streams, no
whole-set buffering" (each assembled volume is copied once and hashed once — see
single-hashing responsibility).
