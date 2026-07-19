# Multi-Set SRR Creation (Spec 1: Video Releases) Implementation Plan

> **STATUS: SCAFFOLD — task list locked; per-task steps being written. Do not execute until this
> banner is removed and the plan is codex-reviewed.**

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create one SRR from a release folder covering every RAR set (dvd1/dvd2), samples, subs,
proofs and stored files, byte-comparable to pyReScene Auto golden fixtures.

**Architecture:** Lib gains a multi-input writer + name canonicalizer (format); App.Core gains
`ReleaseScanner` (ordered pyrescene decision-tree port, policy) and folder-mode `CreatorViewModel`
wiring (generation-guarded); Manager gains browse-folder chrome per the spec's a11y contract.

**Tech Stack:** .NET 10, Avalonia 11.3, CommunityToolkit.Mvvm, xUnit; local pyrescene checkout
(pinned commit) generates golden fixtures.

**Spec (normative):** `docs/superpowers/specs/2026-07-18-multiset-srr-creation-design.md` (rev 5,
codex-APPROVED) + `docs/superpowers/specs/pyrescene-rules-excerpt.txt` (rule source of truth).

## Global Constraints

- File-input behavior stays byte-identical; existing suites stay green (lib ~912+, App.Core 513+,
  Manager 15+); forced-rebuild gate 0 warnings / 0 errors (`-p:BaseOutputPath=bin2/`, delete after).
- Folder-input output byte-identical to pyrescene golden fixtures after app-name normalization.
- Every ported rule cites its excerpt lines in a comment; divergences carry `[DIVERGENCE]` tags
  copied from the spec.
- One top-level type per file (docs/coding-guidelines.md); scanner in App.Core, writer in Lib.
- Review regime: codex reviews this plan before execution and every task's diff during execution
  (alongside the standard task-reviewer gate).

## Task List (locked)

1. **Lib — SrrNameCanonicalizer** (`ReScene.Lib/ReScene/SRR/SrrNameCanonicalizer.cs` + tests):
   final-path (GetFinalPathNameByHandle-semantics) containment for root+sources, `/` separators,
   SFV-entry both-separator interpretation + escape rejection, collision policy, flat mode.
   Produces: `SrrNameCanonicalizer.Canonicalize(root, sourcePath) -> string logicalName` +
   `TryValidateLogicalName`.
2. **Lib — CreateFromInputsAsync** (`SRRWriter.cs` + tests): N≥0 inputs, per-input volume blocks
   in order, stored dedup, temp-in-destination-dir + atomic move, zero/zero rejection,
   non-first-RAR error, multi-chain SFV support; existing `CreateFromSFVAsync`/RAR path delegate.
3. **Lib — golden fixture harness** (`ReScene.Tests/TestData/multiset/generate-golden.py`,
   README with pinned pyrescene hash/command, committed fixtures; byte-equality test with
   app-name normalization via independent block splitter; ≤1 excluded SFV per tree).
4. **App.Core — traversal engine** (`Services/ReleaseTraversal.cs` + tests): deterministic
   ordinal os.walk emulation, category-pass ordering (nfo→m3u→proof→log→cue→srs→sfv).
5. **App.Core — ReleaseScanner rules 2a** (`Services/ReleaseScanner.cs` + `IReleaseScanner` +
   records + tests): ordered decision tree 1-7, rescue fallback, excluded-SFV destinations,
   dirfix skip, subpack main-SFV nested queue.
6. **App.Core — scanner 2b/2c/2e** (same files + tests): has_music in rescue only, both sample
   phases (`sample[:-4]` literal quirk), first-RAR from SFVs + gated loose-RAR divergence.
7. **App.Core — scanner 2d stored chain** (+ tests): nfo filtering (imdb/tvmaze/no.nfo),
   m3u/log/cue/pre-existing srs (+generated-SRS supersede rule), always_skip exact predicates,
   store_rls_root (>100000, similar-name incl. M3U, strip_zeros, fixed-resolution),
   filter_proof_rar_files, proof-SFV state machine.
8. **App.Core — service + VM folder mode** (`ISRRCreationService`/`SRRCreationService`,
   `CreatorViewModel` + tests): pass-through, directory detection, scan generation guard +
   IsScanning, collection population, auto-vs-user OutputPath tracking, status summary,
   music-only disable.
9. **App.Core — generated artifacts** (VM + tests): temp working dir per generation, relative-stem
   collision keying (full-ext on collision), SRS-failure txt, VOB-sample nested SRR, multi-SRR
   subtitle results, cancellation cleanup.
10. **Manager — UI both surfaces** (`CreatorView.axaml`, `CreateSRRWizardBody.axaml` + code-behind):
    Browse folder button + `OpenFolderAsync`, DetectedSets bounded ItemsControl, FieldStatusLine
    summary, §4a a11y contract (automation names, HelpText, tab order, focus return).
11. **E2E + final review**: bridge-driven two-disc folder scenario on both surfaces (typed path →
    detected sets → create → Inspector/Reconstructor verification), full gates, codex whole-branch
    review.

---

(Per-task steps with complete code follow; each task section replaces this line as it is written.)
