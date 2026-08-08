# SRR-Ordered RAR Input Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconstruction feeds rar the SRR's original file order explicitly (with `-ds`), making
solid-set byte order machine-independent — immune to `/etc/rarfiles.lst` and any name-sort
divergence.

**Architecture:** Order captured at SRR parse time (new ordered list beside the existing
`HashSet`), threaded through `RAROptions` (lib) and `ArchiveSetPlanner` (app), consumed by the
Manager's assembly path which passes explicit `./`-prefixed inputs via a new optional
`inputPaths` on the process runner seam. `-ds` auto-added with the explicit tail. Copyable
command stays truthful via a new `InputFileArguments` on the progress event.

**Tech Stack:** .NET 10, xUnit, existing `IRARProcessRunner` seam + `SRRTestDataBuilder`.

**Spec:** `docs/superpowers/specs/2026-08-08-srr-ordered-input-design.md`. Measured facts it
relies on: `-ds` documented in all 477 pack `rar.txt` files (2.03–7.20, no version gate);
`-ds` + explicit order defeats Ubuntu's `/etc/rarfiles.lst` through `run-rar` (matrix F2).

## Global Constraints

- `ArchivedFilesInOrder` preserves FIRST-occurrence order (continuation headers repeat a file
  per volume — dedupe against the existing `HashSet` add result, never a separate lookup).
- Explicit tail ONLY when `_useAssembly` && `OrderedArchiveFiles.Count > 0`; every other run
  (legacy path, Phase 1, flat/no-SRR) keeps the `.{sep}*` mask — byte-identical behavior.
- `-ds` is added exactly when the explicit tail is used — never otherwise, no version gate.
- Tail entry form: `"." + Path.DirectorySeparatorChar + name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)`.
- Length guard: if `exePath + joined args + output + tail` exceeds 25,000 chars → write
  `<options.OutputDirectoryPath>\rar-file-order.lst` (ASCII, LF) and pass a single-element tail
  `@<that path>`; if any tail name contains a non-ASCII char in that fallback → keep the mask,
  log ONE Warning `File names exceed the command-line limit and are not ASCII — using rar's own
  ordering for this run` to `LogTarget.Phase2`, and do NOT add `-ds`.
- Public API additions only (`ArchivedFilesInOrder` ×2, `OrderedArchiveFiles`,
  `InputFileArguments`); update the PublicApi approved baseline in the same task that adds each.
- All existing tests stay green; no comment references the plan/process/reviewers.

---

### Task 1: capture the order at parse time (lib)

**Files:**
- Modify: `ReScene.Lib/ReScene/SRR/SRRFile.cs` (~line 124, beside `ArchivedFiles`)
- Modify: `ReScene.Lib/ReScene/SRR/SRRArchiveSet.cs` (~line 23)
- Modify: `ReScene.Lib/ReScene/SRR/SRRFileParser.cs` (~lines 727 and 759)
- Modify: the PublicApi approved baseline (test asserts it; update in this task)
- Test: the suite covering SRR parsing (find via `grep -rn "ArchivedFiles" ReScene.Lib/ReScene.Tests` — extend in its style)

**Interfaces:**
- Produces: `public IReadOnlyList<string> ArchivedFilesInOrder { get; }` on BOTH `SRRFile` and
  `SRRArchiveSet`, backed by a private `List<string>` with an `internal void
  RecordArchivedFileOrder(string)`-style add OR direct internal list access — match each
  class's existing member style. Task 2/3 consume `SRRArchiveSet.ArchivedFilesInOrder`.

- [ ] **Step 1: Failing tests.** Build a synthetic SRR whose embedded file headers appear in a
  deliberately NON-alphabetical order (e.g. `zzz.dat` before `aaa.dat`) with the second file's
  header repeated as a continuation (SplitBefore) in a later volume. Assert:
  `ArchivedFilesInOrder` equals `["zzz.dat","aaa.dat"]` on the flat `SRRFile` AND on the set;
  a two-set SRR keeps per-set lists independent; `ArchivedFiles` (set) unchanged.
- [ ] **Step 2: Run → FAIL (member missing).**
- [ ] **Step 3: Implement.** At `SRRFileParser.cs:727`: capture the existing add's result —
  `if (srr.ArchivedFiles.Add(normalized)) { <record into srr's order list> }` — and the same
  shape at `:759` for the set (the current code adds unconditionally; keep semantics: the
  HashSet add IS the dedupe). Add the members to both model classes; update the PublicApi
  baseline.
- [ ] **Step 4: Run new tests → PASS; full `ReScene.Tests` suite → green.**
- [ ] **Step 5: Commit** (lib): `feat(lib): preserve the SRR's original archived-file order`.

### Task 2: engine consumes the order (lib)

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/RAROptions.cs` (add `OrderedArchiveFiles`)
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (`BuildFinalArguments` for `-ds`; the two
  launch sites — `RARCompressDirectoryAsync` and the CompleteAllVolumes `_runner.RunAsync`
  call ~line 885 — get the tail; executed-arguments composition gains the tail string)
- Modify: `ReScene.Lib/ReScene/Core/Diagnostics/IRARProcessRunner.cs`,
  `RealRARProcessRunner.cs`, `RARProcess.cs` (optional `IReadOnlyList<string>? inputPaths`,
  default null → today's mask)
- Modify: `ReScene.Lib/ReScene/Core/BruteForceProgressEventArgs.cs` (add
  `InputFileArguments`, default `""`)
- Modify: PublicApi baseline (RAROptions + event args are public)
- Test: the fake-runner flow suites and `RARProcessArgumentTests` (extend in style)

**Interfaces:**
- Consumes: `RAROptions.OrderedArchiveFiles` (set by Task 3's planner; lib tests set it
  directly).
- Produces: `public IReadOnlyList<string> OrderedArchiveFiles { get; init; } = [];` on
  `RAROptions`; `public string InputFileArguments { get; init; } = "";` on
  `BruteForceProgressEventArgs`; runner/RARProcess `inputPaths` parameter (internal seam).

- [ ] **Step 1: Failing tests.** (a) `RARProcess` given `inputPaths` appends them verbatim
  after the output path and does NOT append the mask; given null appends the mask exactly as
  today. (b) Flow test through the fake runner: assembly-mode options with
  `OrderedArchiveFiles=["b.bin","a.cue"]` produce argv containing `-ds` and ending
  `./b.bin ./a.cue` (platform separators) after the output path — and the SAME options with an
  empty list produce the mask and NO `-ds`. (c) Length guard: a name list whose joined length
  exceeds the threshold yields a single `@`-prefixed tail entry pointing at
  `rar-file-order.lst` inside the output directory, and the file's content is the names in
  order; a non-ASCII name in that condition yields mask + no `-ds` + one Warning.
  (d) Progress events carry `InputFileArguments` matching the tail (and `""` for mask runs).
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.** Manager composes the tail once per candidate (pure function of
  options + work root; extract a small private helper returning
  `(IReadOnlyList<string>? tail, string display, bool useDs)` so `-ds`, the tail, the event
  string, and the fallback Warning stay coherent). `-ds` added in `BuildFinalArguments` via
  that decision (thread a bool; keep `-cfg-` exactly as-is).
- [ ] **Step 4: Run new tests → PASS; full suite → green.**
- [ ] **Step 5: Commit** (lib): `feat(lib): drive rar input order from the SRR (-ds + explicit
  file list)`.

### Task 3: planner + copyable command (app)

**Files:**
- Modify: `ReScene.App.Core/ViewModels/Reconstruction/ArchiveSetPlanner.cs`
  (`BuildOptionsForSet`: `OrderedArchiveFiles = [.. set.ArchivedFilesInOrder]`)
- Modify: `ReScene.App.Core/ViewModels/ReconstructorViewModel.cs` (the `FullCommandLine`
  composition and its `VersionEntry`-side mirror: use the event's `InputFileArguments` when
  non-empty, else the existing mask literal; the entry model gains the property plumbed from
  `BruteForceProgressEventArgs`)
- Test: `ReScene.App.Core.Tests` — planner test asserting the copy, and the FullCommandLine
  composition tests (extend the existing fixtures that pin the copied command shape)

**Interfaces:**
- Consumes: `SRRArchiveSet.ArchivedFilesInOrder` (Task 1), `InputFileArguments` (Task 2).

- [ ] **Step 1: Failing tests.** Planner: a set with `ArchivedFilesInOrder=["z.bin","a.cue"]`
  yields options with `OrderedArchiveFiles` equal and in order. FullCommandLine: an entry with
  `InputFileArguments="./z.bin ./a.cue"` composes `… <output> ./z.bin ./a.cue`; an entry with
  empty `InputFileArguments` composes the mask exactly as today.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run new tests → PASS; full `ReScene.App.Core.Tests` suite → green; app builds.**
- [ ] **Step 5: Commit** (app repo): `feat(app): thread SRR file order into reconstruction and
  the copyable command`.

### Task 4: diagnostic message names the real culprit (lib)

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (the pack-order Warning string)
- Test: `ReScene.Lib/ReScene.Tests/ManagerAssemblyFlowTests.cs` (the two pack-order tests'
  expected substring)

- [ ] **Step 1: Update the flow tests' expectation** to the new message:
  `Produced archive packs files in a different order than the release ('<produced>' before
  '<expected>') — an /etc/rarfiles.lst order list or a rar default switch such as -ds from
  .rarrc or the RAR environment variable can cause this.` Run → FAIL.
- [ ] **Step 2: Change the string. Run → PASS; full suite → green.**
- [ ] **Step 3: Commit** (lib): `fix(lib): pack-order warning names /etc/rarfiles.lst first`.
