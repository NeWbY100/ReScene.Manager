# RAR Environment Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make reconstruction immune to rar default-switch injection from the user's environment
(`.rarrc` / `rar.ini` / `RAR` env var) and diagnose pack-order divergence on assembly mismatch.

**Architecture:** Two independent, small changes in the lib (`ReScene.Lib`): (1) `-cfg-` added
unconditionally to both rar invocation sites; (2) a first-file-name probe + one-per-run Warning
in the assembly quick-gate mismatch path.

**Tech Stack:** .NET 10, xUnit, existing `IRARProcessRunner` test seam, `SRRTestDataBuilder`-style
synthetic fixtures.

**Spec:** `docs/superpowers/specs/2026-08-07-rar-env-hardening-design.md` (measured fact it relies
on: `-cfg-` is documented in every rar version in the packs, 2.03–7.20, all platforms —
unconditional add, NO version gate).

## Global Constraints

- `-cfg-` is added unconditionally (no version gate) — do not copy the `-ma4`/`-vn` version
  guards for it.
- `-cfg-` must appear in the executed/displayed argument list exactly like other auto-added
  switches (it is part of the reproducible command line).
- The diagnostic logs at most ONCE per brute-force run, Warning level, `LogTarget.Phase2`.
- The diagnostic must never throw: unreadable/absent volumes → silent skip.
- No public API changes; new helper is `internal`.
- All existing tests stay green (`dotnet test` for `ReScene.Tests`).

---

### Task 1: `-cfg-` on every reconstruction rar invocation

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (`BuildFinalArguments`, ~line 1446)
- Modify: `ReScene.Lib/ReScene/Core/CommentPhaseBruteForcer.cs` (argument construction feeding
  `new RARProcess(...)` at ~line 153)
- Test: extend the existing suites that assert built/executed arguments (locate via
  `grep -rn "\-ma4" ReScene.Lib/ReScene.Tests` — the fixtures that pin auto-added switches;
  Phase-1 args tests near `CommentPhaseBruteForcer` coverage)

**Interfaces:**
- Consumes: existing `BuildFinalArguments(List<string>, BruteForceOptions, int)` shape.
- Produces: executed argument lists whose FIRST element is `-cfg-` (Phase 2) and a Phase-1
  argument list containing `-cfg-`. Task 2 does not depend on this task.

- [ ] **Step 1: Write the failing tests.** In the suite that already pins auto-added switches,
  add cases asserting: (a) Phase-2 final arguments start with `-cfg-` for a 2.x-era version, a
  3.x version, a 5.5x version (where `-ma4` is ALSO added — assert relative order `-cfg-` first,
  `-ma4` second), and a 7.x version; (b) Phase-1's argument list contains `-cfg-`. Follow the
  existing fixtures' style for constructing options/versions; capture through the same seam they
  use (fake `IRARProcessRunner` / direct helper access).
- [ ] **Step 2: Run the new tests, verify they FAIL** (missing `-cfg-`).
- [ ] **Step 3: Implement.** In `BuildFinalArguments`, AFTER the `-ma4`/`-vn`/comment blocks
  (so it ends up at index 0 ahead of `-ma4`'s own `Insert(0, ...)`), add
  `finalArguments.Insert(0, "-cfg-");` with a comment stating why (environment default-switch
  immunity; available in every supported rar version — measured across the packs 2.03–7.20) and
  update the method's XML doc summary to mention `-cfg-`. In `CommentPhaseBruteForcer`, add
  `"-cfg-"` at the front of its constructed argument list with a one-line comment referencing
  the same rationale.
- [ ] **Step 4: Run the new tests → PASS; run the full `ReScene.Tests` suite → green.** Existing
  argument-shape fixtures that enumerate full argv (if any assert exact lists) must be updated
  to include `-cfg-` — that update is part of this task, not a regression.
- [ ] **Step 5: Commit** (lib repo): `fix(lib): pass -cfg- so user rar config cannot alter
  reconstruction`.

### Task 2: pack-order diagnostic on assembly quick-gate mismatch

**Files:**
- Create: `ReScene.Lib/ReScene/RAR/RARFirstEntryReader.cs`
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (quick-gate `!quickMatch` block, ~lines
  1033–1066; add a `_packOrderGuidanceLogged` field beside `_inconclusiveGuidanceLogged` and
  reset it wherever that flag is reset per-run)
- Test: `ReScene.Lib/ReScene.Tests/RARFirstEntryReaderTests.cs` (new) and the assembly-flow
  suite (`ManagerAssemblyFlowTests.cs`) for the end-to-end Warning behavior

**Interfaces:**
- Consumes: `quick.WrittenPaths[0]` (assembled volume path) and `actualRARFilePath` (produced
  first volume path), both already in scope at the mismatch site.
- Produces: `internal static class RARFirstEntryReader { internal static string?
  TryGetFirstFileName(string volumePath); }` — returns the archived name of the first file
  header in a RAR4 or RAR5 volume, or `null` on any parse/IO failure (never throws).

- [ ] **Step 1: Write failing helper tests** (`RARFirstEntryReaderTests`): RAR4 volume with a
  known first file → its name; RAR5 volume → its name; non-RAR bytes → `null`; missing path →
  `null`; empty file → `null`. Build volumes with the existing synthetic RAR fixture helpers
  used by `RARStreamTests`.
- [ ] **Step 2: Run → FAIL (type missing).**
- [ ] **Step 3: Implement `RARFirstEntryReader`.** Walk shape mirrors
  `RARStream.ValidateFirstVolume`: detect RAR5 via `RAR5HeaderReader.IsRAR5`, skip marker, read
  blocks; on the first file header return the (path-separator-normalized) name; wrap the whole
  body in `try/catch (Exception) { return null; }` — this is a diagnostic probe, and any parse
  wobble must degrade to silence, not abort a brute-force run.
- [ ] **Step 4: Run helper tests → PASS.**
- [ ] **Step 5: Write the failing flow test.** In the assembly-flow suite, arrange a produced
  set whose first volume's first file header name DIFFERS from the assembled/SRR first file
  (build the produced fixture with the two entries swapped), drive the quick gate to a mismatch,
  and assert: exactly ONE log entry containing `packs files in a different order` across two
  mismatching combinations (once-per-run), Warning level. Add the negative case: same-order
  mismatch produces NO such entry.
- [ ] **Step 6: Run → FAIL.**
- [ ] **Step 7: Implement the diagnostic.** In the `!quickMatch` block, before retention
  cleanup: when `!_packOrderGuidanceLogged` and `quick.WrittenPaths.Count >= 1`, read both first
  names via `RARFirstEntryReader.TryGetFirstFileName`; if both non-null and different
  (`OrdinalIgnoreCase`), set the flag and log Warning to `LogTarget.Phase2`:
  `Produced archive packs files in a different order than the release ('<produced>' before
  '<expected>') — a rar default switch such as -ds from .rarrc or the RAR environment variable
  can cause this.`
- [ ] **Step 8: Run the flow tests → PASS; full suite → green.**
- [ ] **Step 9: Commit** (lib repo): `feat(lib): diagnose produced pack-order divergence on
  assembly mismatch`.
