# RAR environment hardening — design

## Problem

rar reads default switches from the user's environment: `~/.rarrc` (or `.rarrc` in `/etc`) via a
`switches=` line, the `RAR` environment variable, and on Windows `rar.ini` beside the executable.
Any switch injected there silently alters every reconstruction attempt, and the app's log shows
only the app's own argv — the injected switch is invisible.

Proven field case (2026-08-07, `Golden.Age.Of.Racing-iTWINS`): a Linux user's environment
supplied `-ds` (disable solid name-sort), so rar packed `itw-gaor.cue` before `itw-gaor.bin`.
In a solid archive that changes the entire compressed stream, so SRR-guided assembly — which
splices correct headers with the produced stream — deterministically built a wrong volume and
every combination reported "no match". The user's 14 produced volumes were reproduced
byte-identically (14/14 whole-volume CRCs) by adding `-ds` with cue-first input order to an
otherwise correct run; the distributed rar pack and the stock container image were both verified
clean, isolating the injection to the user's own environment.

## Fix 1: pass `-cfg-` on every reconstruction rar invocation

`-cfg-` ("Ignore configuration file and RAR environment variable") makes rar ignore `.rarrc`,
`rar.ini`, and the `RAR` variable. Availability was measured across the version packs: all 477
version directories carrying a `rar.txt` document it — including the oldest (WinRAR 2.03,
RAR 2.5b3) and newest (7.20 beta) on Windows, Linux, and macOS. It is therefore added
**unconditionally** (no version gate, unlike `-ma4`/`-vn`).

- Phase 2: `Manager.BuildFinalArguments` inserts `-cfg-` at position 0 for every combination.
  It thereby appears in the executed-arguments log line and the user-facing
  "Copy Full Command Line" text, matching the existing contract that auto-added switches
  (`-ma4`, `-vn`, `-z`) are visible and the copied command reproduces the app's invocation.
- Phase 1: `CommentPhaseBruteForcer` adds `-cfg-` to its own argument list the same way.

`rarfiles.lst` is deliberately NOT suppressed: it is a separate mechanism (file order list, not
switches), `-cfg-` does not affect it, and on Windows the default list beside the executable is
part of the conditions under which scene originals were created.

## Fix 2: pack-order diagnostic on assembly quick-gate mismatch

When the assembly quick gate reports a hash mismatch, the engine now compares the first archived
file's name in the assembled volume (which carries the SRR's original order) against the first
archived file's name in the produced first volume. If they differ, the produced archive packed
files in a different order than the release — the exact signature of the `-ds`-class failure —
and the engine logs one Warning per run:

> Produced archive packs files in a different order than the release ('X' before 'Y') — a rar
> default switch such as -ds from .rarrc or the RAR environment variable can cause this.

- Implemented via a small internal helper that reads the first file-header name from a RAR4 or
  RAR5 volume (same walk shape as `RARStream.ValidateFirstVolume`).
- Fires at most once per brute-force run (`_inconclusiveGuidanceLogged` pattern).
- Silent when either name cannot be read (corrupt/missing volume must not add noise) and when
  the names match (the mismatch then has another cause).
- Scoped to the assembly path; the legacy patching path is unchanged.

With Fix 1 in place the diagnostic should never fire for `.rarrc`/env injection; it remains
valuable for other order-divergence sources (e.g. a future rar version changing sort behavior)
and as a regression tripwire.

## Out of scope

Passing an explicit SRR-ordered file list plus a version-gated `-ds` (which would also cover
releases whose original order differs from rar's name sort — e.g. default-list
`*.txt`-before-`*.bin` orders, currently unreconstructable on list-less Linux) is a larger
design with its own risks and is deferred.

No UI changes; the new Warning flows through the existing run log (already announced surfaces).

## Testing

Through the existing `IRARProcessRunner` seam and synthetic-volume fixtures:
- Phase 2 executed arguments contain `-cfg-` first, for every version tier (2.x, 3.x, 5.5x, 7.x).
- Phase 1 arguments contain `-cfg-`.
- Diagnostic: fires exactly once per run on an order-divergent produced set; silent on matching
  order; silent on unreadable produced volume; absent from the legacy path.
