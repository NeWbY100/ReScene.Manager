# SRR-ordered rar input — design

## Problem

The engine tells rar WHAT to pack (`./*`) but not in WHICH ORDER. For solid sets the order
shapes every compressed byte, and rar resolves it from the machine: its name sort, or any
`rarfiles.lst` it finds. The 2026-08 field incident proved the failure mode end to end: the
Ubuntu/Debian `rar` package installs a PATCHED `/etc/rarfiles.lst` whose order list puts `*.cue`
above `*.bin` (upstream's list has no `*.cue`), so a bin+cue release packs cue-first on any such
machine — unmatchable forever, while the archives stay valid. Measured on unix rar 3.41:
`-cfg-` (shipped 2026-08-07) suppresses `~/.rarrc`, the `RAR` env var, and `~/.rarfiles.lst`,
but NOT `/etc/rarfiles.lst`. Separately, releases whose original order differs from rar's
name-sort (possible whenever the original packer's `rarfiles.lst` ordering diverged from plain
alphabetical) have never been reconstructable on machines lacking that same list.

The SRR records the original order — the embedded RAR file headers appear in packing order —
but the parser collapses names into a `HashSet`, destroying it.

## Fix: feed rar the SRR's own order, explicitly, with sorting disabled

1. **Preserve order at parse time.** `SRRFile` and `SRRArchiveSet` gain
   `IReadOnlyList<string> ArchivedFilesInOrder` (List-backed), populated beside the existing
   `ArchivedFiles` adds with first-occurrence dedupe (continuation headers repeat a file per
   volume; the first occurrence is its true position). Additive public API.
2. **Thread it to the engine.** `RAROptions` gains `OrderedArchiveFiles`
   (`IReadOnlyList<string>`, default empty); `ArchiveSetPlanner.BuildOptionsForSet` copies
   `set.ArchivedFilesInOrder`. The legacy flat/no-SRR path leaves it empty.
3. **Pass explicit inputs.** In assembly mode, when `OrderedArchiveFiles` is non-empty, the
   Manager builds the input tail itself — each name converted to platform separators and
   prefixed `./` (matching the existing mask idiom; rar strips the prefix) — and passes it
   through a new optional `inputPaths` parameter on `IRARProcessRunner.RunAsync` /
   `RARProcess`; `null` keeps today's `.{sep}*` mask (legacy path, Phase 1, no-SRR runs are
   unchanged).
4. **Disable rar's reordering.** `BuildFinalArguments` adds `-ds` whenever the explicit tail
   will be used. `-ds` is documented in every rar version in the packs (477 `rar.txt` files,
   2.03–7.20, all platforms — same measurement method as `-cfg-`) — no version gate. Proven
   against the live failure: `-ds` + explicit order beats the Ubuntu `/etc/rarfiles.lst`
   through the pack's own `run-rar` (container matrix F2). On Windows, where the exe-dir
   `RarFiles.lst` used to normalize order by luck, the SRR order is the original order by
   construction — a strict improvement.
5. **Command-line length.** If the composed command line would exceed 25,000 characters, the
   Manager writes the file list to `<work root>\rar-file-order.lst` (ASCII, one name per line)
   and passes `@<path>` instead. If any name is non-ASCII in that fallback (old rar list-file
   encoding is unreliable for non-ASCII), it keeps the legacy mask and logs one Warning —
   scene names are ASCII in practice.
6. **Honest copyable command.** `BruteForceProgressEventArgs` gains `InputFileArguments`
   (space-joined, quoted like `ExecutedArguments`; empty = legacy mask). The app's
   "Copy Full Command Line" composition uses it when present so the pasted command reproduces
   the run byte-for-byte.
7. **Diagnostic message.** The pack-order warning now names the real-world culprit first:
   "… — an /etc/rarfiles.lst order list or a rar default switch such as -ds from .rarrc or the
   RAR environment variable can cause this."

`-cfg-` stays: it still closes config-injected switches the command line does not override
(`-mc`, `-rr`, `-ds` from `~/.rarrc` on top of ours is harmless but the others are not).

## Out of scope

Directory-entry ordering (dir entries carry no packed data; assembly splices them from the SRR
regardless), the legacy non-assembly path (whole-volume hashing has header-order dependencies
explicit file lists cannot honor; it keeps the mask), and Phase 1 (single comment file).

## Testing

- Parser: synthetic SRRs assert `ArchivedFilesInOrder` — non-alphabetical order preserved,
  continuation repeats deduped to first occurrence, per-set lists independent.
- Engine: seam-captured argv asserts tail order, `./` prefix, platform separators, `-ds`
  presence with explicit tail and absence without; `RARProcess` appends a given tail verbatim
  instead of the mask; length-threshold unit test for the `@list` fallback and the
  non-ASCII mask fallback.
- App: planner threads the order; `FullCommandLine` uses `InputFileArguments` when present.
- Acceptance (controller, real data): narrow brute-force of `Golden.Age.Of.Racing-iTWINS`
  on Windows with wrar341 must report MATCH via the assembly path with the explicit tail.
