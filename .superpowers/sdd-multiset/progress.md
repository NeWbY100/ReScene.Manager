# Multi-set SRR creation — execution ledger
Plan: docs/superpowers/plans/2026-07-19-multiset-srr-creation.md (codex-approved r5+fix)
Regime: sequential subagent-driven; per task ONE consolidated task-reviewer + codex diff loop;
stub-first RED discipline; recorded RED/GREEN/full-suite evidence.
Branch: avalonia-feature. Plan approved at base: 6fb9a64a54c49f409c38c164d348d528cd47c7f2

## Tasks
(append: Task N: complete (commits <base7>..<head7>, reviews clean))

Task 1: implemented (lib 579020b, outer 4f84565); task-review Spec PASS / Quality Approved.
  FIX IN FLIGHT: CA1032/CA1816 gate violation (verified live) -> impl-task1.
  Deferred Minors for final review triage:
  - drive-root prefix-compare edge in CanonicalizeRelative/ResolveSfvEntry (root like C:\)
  - POSIX branch: OrdinalIgnoreCase on case-sensitive FS + IsPathRooted("C:\...") false on Linux
  - GetFinalPath \?\ strip ignores \?\UNC\ form (self-consistent but malformed absolute)
  Codex diff review: pending.
  Fix applied (lib 2eb04bf, outer dbdd33d): CA1032/CA1816 -- gate re-verified 0/0 by lead;
  fix re-review: Approved (no contract change, pure API addition, scope clean).
  Awaiting: codex diff verdict to close Task 1.

Task 1: codex diff review REVISE -- 7 findings (2 Critical containment escapes), all REAL and
  in the plan's verbatim code (plan-review approves structure, not deep code semantics; the peer
  task-review also missed the symlink escape). Fix dispatched to impl-task1: centralized final-
  path containment helper (symlink/junction escape #1/#2, root boundary math #3, long-path retry
  + error capture #4, \?\UNC\ #5, host-independent grammar #6, POSIX+long+UNC+link tests #7).
  Findings: .superpowers/sdd-multiset/task-1-codex-findings.txt. NOT closed until fix + both
  re-reviews clean.

Task 1 final re-review: peer reviewer Needs-more -- 1 Critical SFV latch escape
  (ResolveExistingPrefixThenAppend one-way stillExisting latch: x/../J/evil bypasses containment
  through an unresolved junction). CONFIRMED against code by lead. Fix re-dispatched to impl-task1
  (drop latch, per-component resolve via ApplyComponent; REQUIRED regression test
  ResolveSfvEntry_MissingPrefixThenLink_Throws; + 2 minors: split-both-separators, POSIX link test).
  Codex final re-review: still running (may add findings -> same fix round). Task 1 NOT closed.
  Latch fix landed (lib 9205a93, outer 9b8bcf2): latch removed, per-component re-resolve;
  new test fail-before/pass-after CONFIRMED; 31/31, 1331/1331, gate 0/0 -- all re-verified by lead.
  Closing: peer re-review of latch fix dispatched; codex final review STILL RUNNING on the
  pre-fix diff (base->314d392) -- when it returns, re-point at current base->9205a93 for the
  definitive codex verdict. Task 1 closes only on both clean.
  Codex final review (on stale base->314d392) REVISE: its 2 Criticals (latch #2, ResolveAncestor
  single-separator) BOTH already closed in 9205a93 (lead traced + verified: latch gone,
  ResolveAncestorChain splits both, SFV walker single-split ok because ResolveSfvEntry
  pre-normalizes). Sole residual: codex #7 test-gap -- no test exercises the forward-slash
  long-path case (code correct, coverage missing). Test-only fix dispatched. Then: definitive
  codex re-run on final diff + peer re-review confirm -> close.
  Peer reviewer FINAL: Approved (latch fix closes x/../J/evil, no new hole, walkers symmetric,
  separator scoping correct -- corroborates lead's trace). Task-reviewer half DONE.
  Remaining to close: forward-slash test lands -> definitive codex pass on final diff.

Codex DEFINITIVE (base->7c7af81) verdict on the truly-final diff: first pass found a NEW cross-volume
Critical (ApplyComponent ".." fallback used the originally-captured `root`, so a C:\...\J->D:\ junction
let "..".at-root snap back to C:\ -> false-accept escape). One-line fix dispatched + landed
(lib 7c7af81, outer b24b5a1): fallback now `?? Path.GetPathRoot(current)!`; unused `root` param removed
from signature + call site; ApplyComponent private->internal for direct unit test; new
ApplyComponent_ParentAtRoot_StaysOnCurrentPathsRoot (pure path arithmetic, no real D: needed),
fail-before/pass-after confirmed. Lead re-verified: 33/33 canonicalizer, PublicApi snapshot clean
(no internal leak), gate 0/0, 1333/1333 full suite. Codex FINAL2 pass: APPROVE (cross-volume Critical
RESOLVED; no new hole from param removal / internal visibility; new test genuinely exercises the branch).
Task 1: COMPLETE (commits 579020b..7c7af81 lib, review clean -- peer Approved + codex APPROVE;
4 containment Criticals found & fixed across rounds). Outer pointer at b24b5a1.
--- Task 2 dispatched: CreateFromInputsAsync multi-input SRR writer. BASE recorded = lib 7c7af81 / outer b24b5a1.

Task 2 (multi-input writer, lib 1975d8d / outer 1024c68) — DELIVERED, then DUAL-GATE REVIEW:
  Lead independently re-verified: gate 0/0, targeted 19/19, full lib 1352/1352, no blocking-async antipattern.
  Both implementer flags CONFIRMED SOUND by peer + codex + lead: (A) CreateFromSFVAsync stays on legacy
  CreateAsync (re-routing through the STRICT new overload would break the spec's byte-identity guarantee);
  (B) WriteVolumesAsync `private Task`/Task.CompletedTask (async would be CS1998; loop is wholly synchronous).
  Peer reviewer found 1 Important (lone .rNN accepted as first volume). Codex adversarial pass found
  1 Critical + 5 Important + 1 Minor — all verified by lead against code + spec §1a:
    C1(Crit) output self-collision misses discovered volumes/stored -> File.Move overwrites a source RAR.
    C2 flat volume(498)+auto-SFV stored(574) names bypass CanonicalizeLogicalName (POSIX \ -> Win traversal).
    C3 §1a collision/dedup is WRITER-WIDE (vol+stored); impl was stored-only + volumes never deduped.
    C4 lone .rNN accepted as first volume (== peer finding).
    C5 ReportProgress after File.Move can flip committed success / propagate post-commit cancel.
    C6 GetBaseName strips at FIRST .part -> merges two-part-movie chains (anchor to trailing .part\d+.rar).
    C7(min) tmp FileMode.Create not exclusive + ownership untracked. F2(min,comment) empty-dir-on-failure.
  Consolidated fix brief: .superpowers/sdd-multiset/task-2-findings.md. Fix round dispatched to impl-task2
  (has code context). Task 2 NOT closed — awaiting fix + re-verify + re-review (peer + codex on the fix diff).

Task 2 fix round (lib 1ef9926 / outer c9343f9): all 7 findings closed. Lead re-verified each fix in code
+ gate 0/0, multi-input 29/29, full lib 1364/1364. Fix-diff RE-REVIEW: peer APPROVE (7/7 closed, no new
functional defect, corroborated lead traces; C6 underlying bug worse than Important — real chain-merge +
unstable interleave for two-part releases) + codex APPROVE (7/7 RESOLVED with line-precise evidence + its
own structural PASS checks, no new Critical/Important). One non-blocking residual: C3's front-loaded
GetFinalPath made MissingVolume_FlatNaming_FailsMidWrite fail pre-tmp -> stale test name + C7 cleanup
true-branch uncovered.
Test-only closing round (lib 744e029 / outer 790c0d4): renamed the stale test to _CaughtPreTmp_; added
VolumeOpenShareViolation_FailsAfterTmpCreated (FileShare.None lock -> genuine post-tmp IOException) with
red-green proof on the tmpCreated cleanup branch. Lead verified: production BYTE-IDENTICAL to dual-approved
1ef9926 (test-only), gate 0/0, full lib 1365/1365. Self-verified (no extra dual-gate round for a test-only
diff on byte-identical production).
Task 2: COMPLETE (commits 1975d8d..744e029 lib; outer pointer 790c0d4; production of record = 1ef9926,
dual-approved). 
--- Task 3 next: App.Core ReleaseScanner. BASE to record before dispatch = lib 744e029 / outer 790c0d4.

Task 3 DISPATCHED to impl-task3 (golden-fixture oracle). Env verified by lead: pyrescene @ pin 04da213,
Python 3.14.0, imghdr shim to be vendored. Escalation rule set: a byte divergence = BLOCKED finding
(likely Task-2 writer bug), NOT a patch target. BASE = lib 744e029 / outer 790c0d4.

Task 3 BLOCKED — golden oracle surfaced a REAL long-standing writer divergence (lead root-caused + confirmed):
  our SRRWriter.WriteRARFileBlock writes 0x71-block flags=0x0000 (SRRBlockFlags.None, SRRWriter.cs:748);
  pyrescene ALWAYS writes 0x0001 (RECOVERY_BLOCKS_REMOVED) — rar.py:730-732 unconditional, comment "we always
  set this flag, even if there aren't RR" + "earlier beta versions of ReScene .NET did not". Confirmed:
  (a) semantically accurate for us (SRRs are header-only/recovery-stripped); (b) OUR reconstruction IGNORES
  this SRR-block flag (grep empty) so flipping it does NOT break our round-trip; (c) pyrescene main.py:1270
  reads it as rebuild_recovery permission, always-true for its own SRRs. NOT a Task-2 regression — shared with
  legacy CreateAsync, predates the feature; never caught because no prior test did exact byte-compare vs real
  pyrescene RAR blocks. golden-storageonly (no RAR blocks) PASSES clean. Only diverging byte: the 0x71 flag,
  repeated at all 4 volumes. impl-task3 correctly returned BLOCKED, refused to force-green or commit red.
  CONFLICT: WriteRARFileBlock is shared -> fixing it globally satisfies GC2 (pyrescene byte-identity) but
  breaks GC1 (file-input byte-identical to prior). Unanticipated by the plan. ESCALATED to user (3 options).
  impl-task3 work staged uncommitted (shim, build-tree.cs, generate-golden.py, trees+goldens, tests, csproj
  fix); gate 0/0; nothing committed pending decision.

USER DECISION on the RECOVERY_BLOCKS_REMOVED divergence: FIX GLOBALLY (set 0x0001 on ALL SRR RAR-file
blocks, both legacy + new paths). Rationale: standards-compliance/pyrescene parity for every SRR the app
writes; reconstruction unaffected (flag ignored on read); overrides GC1 file-input-byte-identical as a
correctness fix. Dispatched to impl-task3: prod one-liner + verified per-test expected-byte updates
(flag-only, no blanket/weakening) + commit staged Task 3 goldens. Commits A (fix) + B (fixtures) + outer bump.

Task 3 flag fix landed: lib fda929f (fix: RecoveryBlocksRemoved=0x0001 named constant + WriteRARFileBlock
writes it; ONLY test affected = PublicApi snapshot, confirming NO existing test ever pinned this byte) +
lib cba5e46 (golden harness) + outer 13e9eaa. Lead verified: Commit A touches ONLY SRRBlockFlags.cs +
SRRWriter.cs + PublicApi.approved.txt (no byte-content test altered); INDEPENDENTLY re-ran pyrescene on both
committed trees -> byte-identical to committed goldens (goldens AUTHENTIC, not hand-tuned); golden filter 6/6.
BUT gate NON-DETERMINISTIC: build-tree.cs (TestData/multiset/tools) is copied into bin/ by the None glob;
under -p:BaseOutputPath=bin2/ the SDK stops excluding stale bin/, so bin/.../build-tree.cs gets compiled ->
CS9298. Real defect (implementer's 0/0 relied on manual bin/obj clear). Fix dispatched to impl-task3
(csproj: Exclude TestData\**\*.cs from None copy + Compile Remove bin/obj \*.cs; determinism proof required
WITHOUT clearing bin/obj). Task 3 review (peer+codex) holds until the gate is deterministic on the full diff.

Task 3 review: peer review-task3 APPROVED both verdicts (re-derived goldens from raw bytes; 3 Minors).
Codex adversarial pass REVISE — 2 real Important integrity issues (lead-verified against code):
  I1 NormalizeAppName unconditionally rewrites header headerSize + validates nothing -> a headerSize
     divergence (27->7) is MASKED (raw differ, normalized identical). Trust anchor for ALL golden tests.
  I2 Compile Remove covers bin/obj but not bin2* (gate's own output root) -> stale bin2/build-tree.cs
     compiled by a later default build. (lead's earlier proof only exercised the bin/ vector.)
  + peer M1 (SRRBlockFlags PROD doc comment "no flags property" is false), M2 (Assert.Same->Equal), M3 (ordering comment).
  Consolidated fix dispatched to impl-task3 (.superpowers/sdd-multiset/task-3-fix-findings.md): harden
  normalizer w/ validation + inconsistent-headerSize vector (fail-before/pass-after); relocate build-tree.cs
  out of copy cone (or bin2* exclude) w/ 2-root determinism proof; doc/test wording. Task 3 NOT closed.

Task 3 fix re-review: peer review-task3 APPROVED (verified I1 via own Python port + traced all 4 vectors +
filesystem-confirmed relocation). Codex re-review: I1 RESOLVED (confirmed, no new masking path), but I2 only
PARTIALLY closed — lead-confirmed REAL: fix 7131dc6 relocated build-tree.cs BUT also dropped
Exclude="TestData\**\*.cs" from the None Include (line 26), re-broadening the copy glob (no exposure today —
no .cs under TestData — but a future TestData .cs would be copied to output; the Compile Remove backstop
enumerates roots, incomplete vs arbitrary BaseOutputPath). One-attribute fix dispatched: restore the Exclude
(source-level close: no .cs ever reaches any output root). Self-verify (build + no-.cs-in-output + 2-root
determinism) then CLOSE — NOT spinning a 4th codex round on a one-attribute csproj hygiene fix (infinite-
iteration guard). I1 codex-confirmed + I2 closed-at-source + peer 2x approved = Task 3 closes on verify.

Task 3: COMPLETE (commits 744e029..c3fcf67 lib; outer pointer 2a24daf). Final state lead-verified:
I1 codex-RESOLVED + peer-confirmed (normalizer hardened, 4 vectors, real goldens still accepted);
I2 closed AT SOURCE (Exclude="TestData\**\*.cs" restored -> fresh build copies NO .cs to output; build-tree.cs
relocated to tools/; Compile Remove bin/obj/bin2*/TestData/tools belt-and-suspenders); 2-root planted-copy
determinism 0/0; M1-3 done. Golden 10/10, full lib 1375/1skip, gate deterministic 0/0. Goldens independently
re-generated == committed (authentic). Writer flag logic (fda929f) untouched by all fix rounds.
Oracle NET RESULT: caught a real years-old writer bug (RECOVERY_BLOCKS_REMOVED) on first byte-compare +
a non-deterministic gate + a masking hole in its own normalizer — all fixed.
--- Task 4 next: App.Core deterministic release traversal. BASE = lib c3fcf67 / outer 2a24daf.

Task 4 DISPATCHED to impl-task4 (App.Core deterministic release traversal — FIRST App.Core task; OUTER repo
only, no submodule). Env verified: App.Core + App.Core.Tests exist, TempDirTestBase present, System.IO/Xunit
implicit usings present. BASE = outer 2a24daf. Full code in plan; required extra tests (ACL-deny, junction-
not-followed, pre-cancelled ct) flagged; ACL-deny must be non-flaky or guarded-with-report.

Task 4 review: peer review-task4 spec PASS + quality Approved w/ 1 Important (CONFIRMED by lead against code):
  ReleaseTraversal.cs:73 File.GetAttributes(sub) reparse check is OUTSIDE the try/catch (55-64) that wraps
  GetFiles/GetDirectories -> a TOCTOU-delete or ACL edge throw propagates unhandled out of EnumerateFiles,
  crashing the whole traversal, contradicting the class's own "traversal continues" doc. Inherited from the
  plan's Step 3 sample (plan code contradicts plan's own contract). + 2 Minors (double GetFiles(root) on happy
  path; junction-test wording — impl's fail-loud is correct precedent, no fix). Codex pass still running;
  will bundle peer Important + codex findings into one fix round.

Task 4 codex REVISE — 3 Important contract defects (all lead-verified; peer found #3 too):
  F1 full-path contract: EnumerateFiles passes root unchanged -> relative root yields relative/CWD-dependent
     results, violating "full paths" contract (Tasks 5-7 depend on it). Fix: Path.GetFullPath(root) at top.
  F2 root-failure misclassification: probe(31) + Walk re-enumerate root(38) -> root failure during Walk
     returns RootFailed=false. Fix: enumerate root once w/ RootFailed semantics (DRY). Subsumes peer double-GetFiles Minor.
  F3 File.GetAttributes(sub) line 73 OUTSIDE try/catch -> denied/disappearing child crashes whole traversal
     vs documented "traversal continues". Fix: guard -> Issue+skip.
  ALL THREE inherited from the plan's Step 3 sample code (plan contradicts its own contract). LESSON: keeping
  the full dual-gate on the "mechanical" task was right — the PLAN itself had the bugs. Fix dispatched to
  impl-task4 (.superpowers/sdd-multiset/task-4-fix-findings.md). Task 4 NOT closed.

Task 4: COMPLETE (commits 2784c67..c1737ba outer repo). 3 Important contract defects (F1 full-path, F2 root-
failure misclassification, F3 GetAttributes-crash — all inherited from the plan's sample) fixed via clean
restructure (TryReadDirectory + shared EmitFilesAndDescend). Lead-verified: F1/F2/F3 present + correct,
ordering preserved (DRY helper -> divergence structurally impossible), targeted 9/9, full App.Core 522/522,
gate 0/0. Fix-diff RE-REVIEW: peer APPROVE (ordering identical, absoluteness transitive, no RootFailed=false
path, new-defect sweep clean) + codex APPROVE. LESSON REINFORCED: full dual-gate on the "mechanical" task
caught 3 real bugs baked into the PLAN itself.
--- Task 5 next: App.Core ReleaseScanner records + main-set decision tree (spec §2a) — first heavy
classification task, correctness-critical (pyrescene parity). BASE = outer c1737ba.

Task 5 DISPATCHED to impl-task5 (ReleaseScanner main-set decision tree, spec §2a — the heart of folder scanning;
pyrescene remove_unwanted_sfvs line-for-line port). Spans BOTH lib (ProofRarFacts + RarProofInspector over
internal RARHeaderReader + ReScene.Tests fixture test) AND App.Core (4 types + scanner). 3 commits. 20 test
rows. Injectable seam (fact-literal in App.Core.Tests, real RAR in lib test). Correctness-critical -> full
dual-gate + full excerpt/spec reading required. BASE = outer c1737ba / lib c3fcf67.

Task 5 rule-4 ambiguity (impl surfaced, well-researched): what to store for the 3 rule-4 FAILURE sub-cases.
Lead read excerpt L294-436 + confirmed impl's proposal CORRECT: SFV->StoredFiles for all 4 proof outcomes;
RAR->StoredFiles ONLY readable+image (parity: pyrescene rule 4 stores nothing — only decides wanted_sfvs;
real RAR storage is Task 7 filter_proof_rar_files which also skips unreadable, so not storing unreadable RAR
= pyrescene net); warnings ONLY unreadable/ValueError (L376) + missing (L384), NOT bad-naming (L362-363 bare
continue) or success. Last-block-wins (L366-373); image set (".jpg","jpeg",".png",".bmp",".gif") on name[-4:].
Forward note logged: Task 7 filter_proof_rar_files must dedup the success-case RAR vs rule-4's StoredFiles.

Task 5 review: peer review-task5 spec ❌ (1 CRITICAL) + quality ✅. Lead-CONFIRMED against code:
  CRITICAL (ReleaseScanner.cs:129): post-rescue exclusion filter checks only `main`, not `musicSfvs` -> a
    rescue-promoted MUSIC sfv double-lists in BOTH MusicSfvs AND SubtitleSfvs. Parity: pyrescene rescue appends
    music+multi-entry to the SAME wanted_sfvs, so get_unwanted_sfvs excludes both. Fix: also check musicSfvs
    (HashSet main+music). NO music-rescue test existed (why it slipped). Silent golden-breaker.
  IMPORTANT-1 (RarProofInspector.cs:49-51): on ReadBlock null mid-stream (corrupt/truncated header, RARHeaderReader
    returns null not throw) it `break`s -> Readable:true partial, but the DOC promises Readable:false. Corrupt
    proof RAR wrongly falls through to rules 5-7 vs warn+excluded (spec hardening). Fix: null-mid-stream -> Readable:false.
  IMPORTANT-2 (warnings ordering, spec L202): DEFERRED — warnings are NOT written to the SRR, so order can't affect
    Task 9 goldens; UI concern -> Task 8. peer's golden worry misplaced. Track, not this round.
  MINORS: no test for rule 6b (subfix) / zero-after-rescue warning — fold in (cheap coverage).
  Peer confirmed rules 1-7, pass/elif fall-through, proof state machine, destination policy, inspector image set
  all faithful. Awaiting codex; will bundle Critical + Important-1 + minor tests + codex into one fix round.

Task 5 codex REVISE — 6 Important (2 overlap peer). All lead-verified vs code+excerpt:
  #1=peer I-1 (corrupt RAR -> Readable:true). #3=peer CRITICAL (music double-list). NEW:
  #2 malformed 64-bit packed size (ulong->long wrap) -> inspector loop no forward-progress guard -> hang or
     uncaught throw on hostile input.
  #4 _sfvEntryReader (:100 rescue, :242 proof) unguarded -> one bad SFV crashes whole Scan (spec §2 error contract).
  #5 no final ct check before return + inspector has no ct -> cancellation during final item ignored.
  #6 subpack/subfix subs = [excluded]+AddRange(main) non-canonical order (affects SubtitleSfvs nested-SRR byte order).
  Consolidated fix (1 Critical + 5 Important + 2 minor test gaps) dispatched to impl-task5
  (.superpowers/sdd-multiset/task-5-fix-findings.md). DEFERRED: warnings ordering (L202) -> Task 8 (not in SRR).
  Highest-findings task yet — dual-gate earning its cost on the correctness-critical scanner core. Task 5 NOT closed.

Task 5: COMPLETE (lib c3fcf67..1dbf8bf; outer c1737ba..c48b265). Fix round (1 Crit + 5 Imp) all closed w/
isolated fail-before/pass-after. Fix RE-REVIEW: peer BOTH ✅ (hand-traced C1/I5 through merged files; confirmed
I3 catch covers SFVFile's real InvalidDataException throw surface; rules 1-7/proof/destination byte-for-byte
unchanged; closed 2 minor test gaps as bonus) + codex APPROVE (all 6 resolved, valid-input parity unchanged,
readable/image happy path unchanged). Lead-verified: scanner 28/28, inspector 9/9, full App.Core 550/550, full
lib 1384/1skip, gate 0/0. DEFERRED to Task 8: warnings-ordering (spec L202, not in SRR) + a cosmetic rare
double "Unreadable SFV" warning (proof-dir-only-SFV double-failure; no output impact).
Task 5 was the highest-yield: dual-gate caught 1 silent Critical + 5 real robustness/contract gaps in scanner core.
--- Task 6 next: App.Core scanner music/samples/first-RAR (spec §2b/§2c/§2e). BASE = outer c48b265 / lib 1dbf8bf.

Task 6 DISPATCHED to impl-task6 (App.Core scanner samples §2c + gated loose-RAR §2e + music-rescue §2b coverage;
OUTER repo only, extends Task 5 scanner). 14 test rows incl. the sample[:-4] double-dot quirk + case-sensitive
phase-2. BASE = outer c48b265.

Task 6 review: peer review-task6 BOTH ✅ APPROVED, no Critical/Important, 4 Minors (hand-traced double-dot
quirk arithmetic + phase-2 Ordinal case-sensitivity + loose-RAR gating non-vacuous). Substantive Minor #2
(lead agrees, real spec deviation): IsLooseRarDirExcluded mirrors rules 3/5/6 but OMITS rule 4 (Proof/Proofs)
-> a first-vol RAR in a Proof/ dir in a zero-SFV release wrongly becomes a MainSet; spec §2e L186-188 literally
says "rules 3-6". One-line fix (add proof/proofs to loose-RAR dir exclusion). Cosmetic minors: missing
[DIVERGENCE] tag on file.Length>4 guard; test-helper DRY dup vs Task 5; eager siblingSfv string. Awaiting codex.

Task 6 codex REVISE — 5 Important, but lead verified vs excerpt get_sample_files L42-68: only 3 REAL, 2 FALSE POSITIVES.
  FALSE POSITIVE #1 (phase-2 "basename vs raw entry"): code stores RAW entries + compares candidate BASENAME =
    EXACTLY pyrescene (L62-65: sfv_stored_files holds raw entry.file_name; `basename(nsample) in sfv_stored_files`).
    Codex flagged vs my OWN imprecise review-prompt wording ("entry basenames") — MY error. Code is correct; storing
    basenames would DIVERGE from golden. Only issue = misleading var name sfvEntryBasenames (-> rename sfvStoredFiles).
  FALSE POSITIVE #2 (sample order "should interleave"): code [phase-1]+[phase-2] = pyrescene (L52 then L66). Codex's
    example output IS what pyrescene produces; full interleave would diverge from golden. Code correct.
  REAL: #3 loose-RAR omits Proof/Proofs dir (spec §2e rules 3-6) -> Proof/p.rar wrongly a MainSet. #4 chain grouping
    case-SENSITIVE (writer uses OrdinalIgnoreCase) -> case-differing volumes split -> non-first part02 emitted.
    #5 loose-RAR sets ordered by first-ENCOUNTERED not first-volume (divergence canonical-order).
  Fix (F1/F2/F3 + var rename) dispatched to impl-task6; explicitly told NOT to change #1/#2 (parity).
  LESSON: verify EVERY finding against source even from a trusted reviewer — codex 2/5 false here, 1 induced by my
  own prompt wording. Blind trust would have broken pyrescene parity + the golden. Task 6 NOT closed.

Task 6: COMPLETE (outer c48b265..3fdf466). Fix round: F1 loose-RAR excludes proof/proofs dirs; F2 chain grouping
OrdinalIgnoreCase (matches writer); F3 sets ordered by first-volume traversal index; R1 var rename. codex #1/#2
FALSE POSITIVES (sample basename-vs-raw + phase1-then-phase2 = pyrescene-faithful) correctly LEFT UNTOUCHED —
independently corroborated by impl AND peer confirm. Peer confirm: clean (hand-traced F1/F2/F3 vs lib sources).
Lead-verified: targeted 17/17, full App.Core 567/567, gate 0/0. Peer-confirm-only close (no codex re-review —
divergent loose-RAR feature, no golden parity stakes, parity code untouched, all verified).
--- Task 7 next: App.Core scanner stored-file chain §2d (nfo/m3u/log/cue/srs filtering, always_skip/store_rls_root
images, filter_proof_rar_files dedup vs Task 5 rule-4, proof-SFV state machine). BASE = outer 3fdf466.
Last heavy scanner task. Correctness-critical.

Task 7 DISPATCHED to impl-task7 (stored-file chain §2d — LAST heavy scanner task; OUTER only, extends scanner,
consumes PUBLIC RarProofInspector). Passes 1-5 + pre-existing srs + fix RAR + input-SFV append (Task 9 does 6-9
+ full pass-10). Verbatim transcription of always_skip/store_rls_root/similar_to_good_name(10-char slice)/
fixed_resolution_cover/is_storable_fix + filter_proof_rar_files DEDUP vs Task 5 rule-4. 22+1 test rows.
BASE = outer 3fdf466. Correctness-critical -> full dual-gate.

Task 7 review: peer review-task7 spec ✅ (1 Important) + quality ✅. Verified every helper line-by-line vs excerpt
(incl. the asymmetric case bug in similar_to_good_name preserved verbatim; 10-char slice; JPEG SOFn walk;
dedup call-ordering; fix-RAR full gate = generate_srr L784-798 not over-built; 6 Task-5 test updates legitimate).
  IMPORTANT (lead-CONFIRMED, ReleaseScanner.cs:1040-1046): TryGetFixRar first-volume check is extension-only —
  Path.GetExtension("x.part02.rar")==".rar" + IsRARVolume passes, so a NON-FIRST partN slips through; the comment
  falsely claims continuations can't. Old-style (.r00) correctly guarded (diff ext). Narrow (single-entry SFV
  listing non-first partN) but real gap in exact-port + lying comment. Fix: verify true first volume (partN lowest;
  reuse writer/RARVolumeIdentifier first-volume logic). Same theme as Task2 C4 / Task6 loose-RAR.
  MINORS: JPEG [DIVERGENCE] peer-confirmed acceptable (no fix); store_rls_root warning thousands-sep cosmetic
  (not in SRR, skip); nfo/log/cue/srs within-category traversal-order test not pinned (low, optional). Awaiting codex.

Task 7 codex REVISE — 5 Important; lead verified vs excerpt: 4 real fixes + 1 Task-9-scope. (peer's Important = codex #2.)
  F1 no.nfo: excerpt L611 `in ("no.nfo")` = parenthesized STRING (no comma) -> SUBSTRING membership, code uses
     equality -> 8-byte .nfo/o.nfo stored where pyrescene skips. Fix: "no.nfo".Contains(basename lower). (peer missed this.)
  F2 TryGetFixRar ext-only first-volume -> x.part02.rar slips through (comment lies). Fix: verify true first volume.
  F3 JPEG FF-D8=JPEG broader than imghdr -> marker-first 630x1200 JPEG skipped where pyrescene stores. Keep
     simplification (don't reproduce imghdr), strengthen [DIVERGENCE] + regression test; verify goldens at Task 9.
  F4 proof-RAR dedup lexical Path.GetFullPath -> use SrrNameCanonicalizer.GetFinalPath (design §1a). Writer final-path
     dedup is SRR backstop, so scanner-list correctness only.
  #5 NOT Task 7: proof [sfv,rar] front-position + RAR-before-SFV reorder = Task 9 pass-10 clear-and-rebuild scope.
  ** CARRY TO TASK 9 BRIEF: pass-10 must (a) clear stored files + rebuild in category order (excerpt L601-603),
     (b) move each proof/nested .srr AND proof .rar whose stem matches an SFV to immediately BEFORE that SFV
     (excerpt tail L1243, ext check ('.srr','.rar')); update ReleaseScannerStoredTests:390 order then. **
  Fix dispatched to impl-task7. Task 7 NOT closed. LESSON AGAIN: verified each finding vs source (codex #1 real
  subtlety peer missed; #5 correctly deferred not blindly "fixed").

Task 7 fix landed (outer 723b010): F1 substring no.nfo; F2 name-based IsTrueFirstVolume (plain .rar OR partNN N==1);
F3 JPEG divergence comment+regression test; F4 GetProofRars dedup via ResolveDedupKey(GetFinalPath). Lead-verified:
targeted 30/30, full App.Core 597/597, gate 0/0. Impl flags (both sound, low-priority, CARRY TO TASK 9):
(a) TryGetFixRar's own dedup + pass-10 SFV-append dedup still lexical Path.GetFullPath (same class as F4) — uniform
final-path treatment in Task 9; (b) LATENT Task-2 writer bug: SRRWriter.ResolveVolumesAsync also accepts a lone
.part02.rar (same class as C4 but new-style) — narrow (scanner never feeds it; direct-API-only), note for final review.
Peer re-review dispatched (no codex re-round — localized parity closures, verified).

Task 7: COMPLETE (outer 3fdf466..723b010). Fix round F1-F4 closed; codex #5 deferred to Task 9. Fix RE-REVIEW:
peer APPROVE (hand-traced all 4: re-derived L611 substring; IsTrueFirstVolume both branches; JPEG SOF byte-by-byte;
F4 reuses SRRWriter.cs:613 GetFinalPath precedent; confirmed ALL parity helpers zero-lines-touched; #5 no reorder).
Peer-confirm-only close (localized parity closures, verified). Minor FYI (not fixed): IsRARVolume redundant after
ext check — dead conjunct, zero effect.
*** ENTIRE SCANNER (§2a-e) COMPLETE. 7/11 tasks done. ***
--- Task 8 next: App.Core service pass-through + CreatorViewModel folder mode (spec §3). VM/service integration
(NOT parsing/parity; unit-testable VM logic, not .axaml markup — that's Task 10). BASE = outer 723b010.
CARRY TO TASK 9: (1) pass-10 clear+rebuild + proof-RAR-before-SFV reorder (excerpt L601-603, L1243 ext ('.srr','.rar'));
(2) uniform final-path dedup (TryGetFixRar own dedup + pass-10 SFV-append still lexical); (3) verify goldens use
standard JFIF/Exif JPEGs (F3 divergence). Also latent: SRRWriter.ResolveVolumesAsync accepts lone .part02.rar.

Task 8 DISPATCHED to impl-task8 (service pass-through + CreatorViewModel folder mode §3 — VM/service integration,
CONCURRENCY-critical generation-guarded scan; NOT parity, NOT markup). Atomic DI update (4 new CreatorViewModel(
sites incl Manager.Tests) + StubReleaseScanner/GatedReleaseScanner fakes. 18 test rows. Generation guard must
discard stale scans by generation-check not cancellation-alone. BASE = outer 723b010.

Task 8 review: peer review-task8 spec ✅ + quality ✅ (hand-traced generation guard/IsScanning/CanCreate/OutputPath
all CORRECT), 2 Important (lead-confirmed):
  F1 (concurrency) _scanCts Cancel()/Dispose() RACE: RunFolderScanAsync finally cts.Dispose() (line 999) runs on a
     BACKGROUND thread (ConfigureAwait(false) after Task.Run + fire-and-forget Post), racing _scanCts?.Cancel() on
     UI thread (OnInputPathChanged:266) -> CTS Cancel/Dispose concurrent = documented-unsafe -> uncaught
     ObjectDisposedException -> crash in narrow window (fast scan finally overlaps 2nd input change). No test hits it.
     Fix: dispose replaced cts on UI thread (Cancel then Dispose back-to-back in OnInputPathChanged), remove bg-thread
     dispose (handle terminal cts). + regression test (fast non-gated scanner + double input switch).
  F2 (UX) stale InputStatus after filesystem-root branch (clears collections+OutputStatus but not InputStatus) ->
     prior successful scan's "N RAR sets" summary persists while collections wiped. Fix: clear/reset InputStatus (or
     move rejection msg to InputStatus). + test (prior scan -> drive root -> InputStatus not stale).
  Minors: OutputStatus-vs-InputStatus field choice (combine w/ F2); empty-folder Create allowed (header-only, likely
     fine). Awaiting codex.

Task 8 codex REVISE — 3 Important (all lead-verified): #1 = peer F1 CTS race (+ TOCTOU null-out of newer CTS).
#2 NEW cross-mode output-path: file-mode AutoSetOutputPath (1311) doesn't set _outputPathAutoGenerated -> file->folder
keeps stale movie.srr (misclassified user-edited), folder->file retains folder auto. (peer traced folder-only, missed it.)
#3 NEW fail-open: filesystem-root branch stays folder mode + doesn't reset _isMusicOnlyFolder/gate -> drive-root empty
Create; RootError (ACL-denied folder) shown FieldStatus.Ok (1048-1062, no RootFailed check) -> empty Create. (overlaps
peer F2 stale InputStatus.) Consolidated fix (F1 serialize CTS UI-thread + race test; F2 file-mode provenance + 4 mode-
switch tests; F3 explicit invalid-scan state gating filesystem-root + RootError + 3 tests) dispatched to impl-task8.
Both reviewers confirmed generation guard/IsScanning/CanCreate/folder-Create-args/DI/file-byte-identity CORRECT.
Dual-gate on VM: peer concurrency-trace + codex cross-mode/fail-open = neither alone found all. Task 8 NOT closed.

Task 8: COMPLETE (outer 723b010..3c350d2). Fix F1 (CTS lifecycle serialized on UI thread + token-capture-once — the
race test caught a SECOND race in the first-pass fix) / F2 (cross-mode output-path provenance shared helper) / F3
(_folderScanInvalid gates CanCreate; filesystem-root + RootError). Fix RE-REVIEW: peer SHIP IT (F1 provably race-free
in PRODUCTION — real Avalonia dispatcher single-threaded; matches house pattern) + codex APPROVE. Lead-verified:
targeted 28/28, full App.Core 625/625, Manager 181/184 (same 3 pre-existing), gate 0/0.
NON-BLOCKING NOTES CARRIED: (a) F1 test-harness-only TOCTOU (TestUiDispatcher sync-on-caller-thread; production safe;
watch if the 200-iter test flakes in CI); (b) IsRootError string-prefix coupling -> add `bool RootFailed` to
ReleaseScanResult (Task 5 type) = clean fix; fold into Task 9.
--- Task 9 next: FULL-PIPELINE GOLDEN (enable Task 3's FullPipelineGoldenTests placeholder; splice passes 6-9 +
full pass-10 reorder; regen golden WITH samples/subs via pyrescene WITHOUT --no-srs). THE byte-identity verdict.
BASE = outer 3c350d2 / lib 1dbf8bf.
CARRY-FORWARDS INTO TASK 9: (1) pass-10 clear+rebuild + proof-RAR-before-SFV reorder (excerpt L601-603, L1243);
(2) uniform final-path dedup (TryGetFixRar own + pass-10 SFV-append still lexical); (3) verify goldens standard
JFIF/Exif JPEGs (Task7 F3); (4) add RootFailed flag to ReleaseScanResult (Task8 note); update ReleaseScannerStoredTests:390 order.

Task 9 DISPATCHED to impl-task9 (generated-artifact staging + FULL-PIPELINE GOLDEN — THE culmination/byte-identity
verdict). Touches App.Core + lib (FullPipelineGoldenTests). Escalation rule set: golden divergence = BLOCKED finding
(pyrescene arbiter, like Task 3), NOT force-green. KNOWN RISK flagged: SRS appName may diverge (different format than
the .srr NormalizeAppName targets). Absorbs carry-forwards: pass-10 reorder, uniform final-path dedup, optional
RootFailed flag, standard-JPEG check, update ReleaseScannerStoredTests:390 order. BASE = outer 3c350d2 / lib 1dbf8bf.

Task 9 PARTIAL: delivered (outer 00b4023) = artifact staging (SRS/.txt/VOB nested-SRR, collision by full relative stem,
same-stem full-ext), subtitle nested-SRR ordered-list, supersede pre-existing srs, cancellation cleanup (no OCE swallow),
FULL pass-10 proof-before-sfv reorder (shared generic ApplyProofBeforeSfvReorder<T>, scanner+VM), dedup carry-forward to
GetFinalPath, updated 2 scanner tests to final [rar,sfv]. Lead-verified: targeted 11/11, App.Core 636/636, gate 0/0. NO lib change.
BLOCKED (golden only, ENVIRONMENT not code): this sandbox has NO unrar/WinRAR -> pyrescene --vobsub-srr silently self-disables
(stores subs PLAIN, no nested SRR) while our C# always generates the nested SRR -> a golden here = environment artifact, not
a real comparison. impl correctly did NOT fake the golden or install unrar (authorization). KNOWN-RISK (SRS appName divergence)
UNRESOLVED (needs the golden). => USER DECISION: install unrar here / run golden on user's WinRAR machine / defer FullPipeline
golden to follow-up. Delivered-portion dual-gate dispatched; Task 10 (UI, independent) can proceed meanwhile.

GOLDEN UNBLOCK (user pointed to g:\winrar): UnRAR 7.01 stable at G:\winrarxtracted\winrar-x32-701\UnRAR.exe.
pyrescene locate_windows() fallback chain ends at locate_in_path()->which("unrar") (unrar.py:97), so prepending that
dir to PATH lets pyrescene find UnRAR.exe -> --vobsub-srr stops self-disabling. Re-dispatching golden step to impl-task9
(baked into generate-golden.py + pin the unrar version 7.01 in README alongside pyrescene commit pin). Escalation rule
STILL in force: a REAL golden divergence (SRS appName, ordering, nested content) = BLOCKED finding, not force-green.

GOLDEN UNBLOCK (user pointed to g:/winrar): UnRAR 7.01 stable at G:/winrar/extracted/winrar-x32-701/UnRAR.exe.
pyrescene locate_windows() fallback ends at locate_in_path -> which("unrar") (unrar.py:97), so prepending that dir to
PATH lets pyrescene find UnRAR.exe -> --vobsub-srr stops self-disabling. Re-dispatched golden step to impl-task9 (bake
into generate-golden.py + pin unrar 7.01 in README alongside pyrescene commit pin). Escalation rule STILL in force:
a REAL golden divergence (SRS appName / ordering / nested content) = BLOCKED finding, not force-green.

Task 9 delivered-portion review: peer review-task9 spec ✅ + quality ✅, 2 Important + 2 Minor (lead-confirmed):
  F1 (Important) redundant subtitle-SFV re-add (CreatorViewModel:1511) — scanner pass-10 (ReleaseScanner:260,
     foreach sfv in sfvs) ALREADY stores every sfv incl. excluded; the 11 artifact tests use stubs that hand-omit it
     so they never test the real-scanner contract (false assurance). Currently safe by emergent splice/reorder/writer-
     dedup interaction but fragile+untested. Fix: drop the re-add + rely on pass-10 SFV + reorder; ADD a real-scanner
     end-to-end test. (golden re-run must confirm byte-identity preserved.)
  F2 (Important) SRS-failure .txt gated on error-TEXT length (CreatorViewModel:1404) but excerpt L716/721 gates on
     SAMPLE FILE SIZE (getsize(sample)>0). Undisclosed divergence (0-byte sample: pyrescene suppresses, we store).
     Fix: gate on FileInfo(sample).Length>0 + fix Test 5 (uses 1-byte either way, doesn't distinguish).
  F3 (Minor) unported skip-subtitle-SFV-if-same-stem-RAR-stored (excerpt L805-811). Narrow; port (cheap).
  F4 (Minor) same_srs_name global-groupby vs pyrescene adjacent-only — intentional normalization (our ordinal order
     makes same-stem adjacent anyway), no fix, noted.
  Verified correct: ext-swap/collision, supersede, VOB nested-SRR, pass-10 reorder algorithm (hand-traced vs L1240-51),
  dedup carry-forward, service REUSE. Awaiting codex delivered-review + impl-task9 golden result; bundle after.

Task 9 delivered-portion codex REVISE — 5 Important (all lead-verified; codex found MORE than peer, incl. contradicting peer on OCE):
  #1 pass-10 MISSING clear-and-rebuild -> rule-4 proof entries pre-seeded at FRONT (ReleaseScanner:435 before 241) ->
     wrong category order [rar,sfv,nfo] vs required [nfo,rar,sfv]. THE carry-forward, only partially done (reorder not rebuild).
  #2 splice anchors use `!name.Contains("proof")` SUBSTRING (CreatorViewModel:1331,1352) -> proofread.sfv / movie.proof.fix.rar
     misclassified as proof -> wrong splice index. (CONFIRMED lines.)
  #3 plural subtitle gen TEST-ONLY (production single CreateFromSFVAsync); N>1 via test override w/ wrong naming subs.N.srr
     vs excerpt chain-basename (eng.srr/jpn.srr).
  #4 outer CreateSRR catch (898) SWALLOWS OCE -> cancel = "Error." not clean cancel. (CONFIRMED; peer MISSED this — peer only
     checked the staging finally, codex caught the outer catch.)
  #5 subtitle path RootRelativeName (1263) keeps ../ for outside-root -> writer rejects; sample path already uses
     GeneratedStoredName Subs/<name> fallback (1815). Inconsistent.
  + peer F1 (redundant subtitle re-add, stub-only test) = D4, F2 (SRS-txt sample-size gate) = D3, F3 (skip-same-stem-RAR) = D8.
  Consolidated fix brief (D1-D8) staged at task-9-delivered-fix-findings.md. Awaiting impl-task9 GOLDEN result; dispatch the
  delivered-portion fix after (golden must re-pass; D1/D4 ordering-sensitive). Delivered portion genuinely under-baked on
  category-ordering + edge cases — dual-gate caught it (golden tree alone wouldn't exercise most). Task 9 NOT closed.

Task 9 GOLDEN RESULT — the moment of truth delivered: golden found a REAL divergence (byte 607), precisely isolated by
impl (built 2 variants: without embedded SFV = byte-identical 0-diff; with = 40-byte delta). Root cause: nested subtitle
SRR embeds the SFV+nfos (BuildNestedSubtitleStoredFiles) but pyrescene's is RAR-blocks-only. Shipped (shared w/ wizard).
Env: unrar 7.01 unblocked; impl fixed 2 more env gaps (useRealCrc opt-in keeping committed trees UNCHANGED; nntplib shim)
+ resolved SRS-appName ("pyReSample 0.7" vs ours) via NormalizeSrsfAppName + deep normalization. Real FullPipelineGoldenTests
written, left RED+uncommitted per escalation rule.
USER DECISION (parallels RECOVERY_BLOCKS_REMOVED): FIX TO BYTE-IDENTITY — nested SRR = RAR-blocks-only.
Consolidated fix (D0 golden + D1-D8 delivered review) dispatched to impl-task9. Golden must PASS after. Task 9 NOT closed.

Task 9 codex FIX-RE-REVIEW REVISE — 6 Important the peer APPROVED past (lead-verified all real vs source):
  ** LEAD CORRECTION: I over-reported "full pipeline byte-identical / culmination verified." The golden is a LIB test
     that can't reference App.Core, so BuildStoredListForFullPipeline (FullPipelineGoldenTests:199) HARDCODES the stored
     order -> it validates the WRITER given a correct order, NOT the scanner/VM ordering. Writer byte-identity = real+proven;
     FULL-pipeline (scanner ordering) = NOT yet proven. Corrected to user. **
  E1 pass-10 stores sfvs traversal-order but excerpt L1195-1204 DEFERS main sfvs to bottom (non-main first, main last) ->
     [main.sfv,p.rar,p.sfv] vs ref [p.rar,p.sfv,main.sfv]. Golden can't catch (manual list). Fix + App.Core ordering tests.
  E2 IsUnderProofDirectory (1371) any-ancestor vs scanner rules 3/4 immediate-parent (L342) + splice/reorder interaction.
  E3 D4 REGRESSION: manually-added subtitles (AddSubtitleAsync) stored NOWHERE (pass-10 only stores scanned). Restore w/ dedup.
  E4 multi-chain subs SPEC-REQUIRED (§3 L233) — my D5 removal was WRONG. Support one-SRR-per-chain (create_srr_for_subs L1283/1477).
  E5 D6 catch (898) sets "Cancelled." no rethrow -> completes normally; make cancellation observable per contract.
  E6 nested SRR forwards outer options (1596); L1489 hardcodes oso_hash=False -> pass ComputeOSOHashes=false.
  Second fix round (E1-E6) dispatched to impl-task9. LESSON: peer approved (thorough but case-specific hand-trace); codex
  caught 6 (main-sfv deferral, manual-subtitle drop, spec-required multi-chain, oso edge). Verify findings vs source even
  when a thorough peer approves — and don't overstate what a golden validates. Task 9 NOT closed.

Task 9 2nd-fix re-review: peer review-task9b APPROVED (folder-mode deliverable) — re-traced ALL D0-D8 + the 6 E-fixes,
no regression; E1 byte-exact port of L1194-1206; ruleFourProofRars removal relies on LastPackedIsImage=>AnyImage (true);
E5 convention verified vs Reconstructor:1613/Compare:657. NEW Important-non-blocking SCOPE GAP: E4 (multi-chain) + E6
(oso-off) applied ONLY to folder-mode nested-SRR path, NOT the pre-existing wizard/Advanced-tab GenerateNestedSRRFileAsync
(:1870) — it still merges multi-chain into one SRR + forwards outer OSO options (same bug E4/E6 fix; NOT a regression).
Same class as D0 (shared nested-SRR shipped behavior). DECISION PENDING (fix wizard path too for consistency vs document
scope boundary vs follow-up). Awaiting codex 2nd-fix verdict before deciding.

Task 9 2nd-fix codex re-review: VERDICT REVISE — 4 Important. Lead-verified vs source+excerpt:
  G1 (proof RAR outside proof dir dropped): FALSE POSITIVE — excerpt L357-385 rule 4 never stores the RAR (only excludes
     the SFV); storage is solely filter_proof_rar_files L204-211 (needs "proof" in path); pyReScene stores it nowhere either.
     codex measured vs design-doc §2a L143 loose wording, not the arbiter. NO code change.
  G2 (subtitle SFV emitted before its nested SRRs): REAL — CreatorVM:1606 SFV before :1636 SRRs; pyReScene pass-9 (L803+)
     SRRs before pass-10 (L1189-1205) SFVs; same-stem reorder can't fix differently-named. Manual/multi-chain subtitle only.
  G3 (SFVFile.ReadFile rejects spaced RAR names): REAL — ReadFile strict-throws (SFVFile:52,79); writer ParseSfvEntryNames
     is space-tolerant (SRRWriter:695-708). One spaced entry drops ALL chains for the SFV.
  G4 (lexical Path.Combine splits .\-prefixed chains): REAL — VM:1667 raw Path.Combine vs writer ResolveSfvEntry (SRRWriter:521)
     → dup eng.srr collision. G3+G4 root cause: E4 reimplemented the writer's SFV→chains grouping divergently despite claiming
     to "mirror exactly". Fix: shared writer-identical grouping helper.
  → 3rd fix round dispatched (folder-mode ONLY): .superpowers/sdd-multiset/task-9-fix3-findings.md. Wizard/Advanced E4/E6 gap
    (peer + folded here) = SEPARATE pending USER decision.

USER DECISION (wizard/Advanced-tab scope): "Fix both E4 + E6 there too" — extend multi-chain split + oso-off to the
pre-existing GenerateNestedSRRFileAsync (CreatorViewModel.cs:1870), reusing fix3's shared chain-grouping helper. Full parity
(matches prior fix-globally/byte-identity choices). SEQUENCING: fix3 (folder-mode G2/G3/G4 + shared helper) lands & dual-gates
FIRST, then the Advanced-tab E4/E6 fix (its own gate) reuses the helper — no parallel implementers. THEN close Task 9.

Task 9 fix3 (folder-mode G2/G3/G4 + shared SfvVolumeResolver, outer 72f8eff/lib 481b81d): peer review-task9-fix3 APPROVED,
0 findings. Independently confirmed the writer-refactor byte-identity (pre-refactor branch already did ResolveSfvEntry→
IsRARVolume→AddVolumeToChain; ResolveOrderedChains identical resolved multiset + first-seen order; the only tie-break
divergence, case-only filename dups, is killed upstream by GetFinalPath dedup / writer OrdinalIgnoreCase throw). G1 non-issue
vs arbiter; G2 dedup/D8/splice intact; File.Exists-drop accepted (more faithful). Nit: writer path sorts each chain twice
(micro-cost, no correctness). Lead independently re-ran golden+resolver+multiinput = 38/38. Awaiting codex fix3 verdict (dual gate).

Task 9 fix3 codex re-review: VERDICT REVISE (split — peer APPROVED). 3 Important, lead-verified (codex used in-memory .NET probes):
  F1 (double-sort byte-divergence): CONFIRMED REAL, must-fix. Resolver sorts each chain (SfvVolumeResolver:82) then writer
     re-sorts (SRRWriter:568); base sorted once. Unstable List.Sort + comparator ties (.r04 vs .004 equal rank) → different
     bytes. Peer's dedup argument missed this tie source (distinct names, not case-only dups). FIX: resolver returns first-seen/
     listing order (no sort); writer single-sort stays (byte-identical to base); VM re-adds its own sort for naming. Golden/fix3
     tests unaffected (single-chain, no ties).
  F2 (proof SFV → external ../RAR still diverges: pyReScene nest-SRRs it, we skip): REAL but PATHOLOGICAL (proof SFV listing a
     RAR outside its dir — not a real artifact) + §2a already documents proof SFVs excluded from subtitle processing. Safe as-is.
  F3 (mixed scanner+manual subtitle SFV ordering: manual rides result block, lands before scanner SFVs): REAL but manual
     AddSubtitleAsync has NO pyReScene parity target (pyReScene only scans dirs); design already tags excluded-SFV order
     [DIVERGENCE: determinism]. ExtraSubtitleSfvFiles populated by BOTH scanner(:1182) + manual(:537), confirmed reachable.
  PLAN: F1 = fix (regardless). F2/F3 = batched USER decision (document [DIVERGENCE] vs fix for strict parity). Bundle F1 +
  F2/F3-resolution + the already-approved Advanced-tab E4/E6 into ONE implementer (overlapping subtitle code), then dual-gate + close Task 9.

USER DECISION (F2/F3): "Document both as [DIVERGENCE]". Bundled FINAL Task-9 round (task-9-fix4-findings.md): A=F1 double-sort
fix (resolver no-sort + VM re-sort + writer single-sort = byte-identical to base), B=F2 [DIVERGENCE: scope] doc, C=F3
[DIVERGENCE: determinism] doc + store-exactly-once invariant test, D=Advanced-tab E4+E6 (rewire CreateVobsubSRRsAsync to reuse
GenerateNestedSubtitleSrrsAsync = per-chain + oso-off; wizard-placeholder GenerateNestedSRRFileAsync gets E6-only, E4 deferred
to Task 10). impl dispatched (impl-task9-fix4). This round CLOSES Task 9 folder-mode + Advanced-tab subtitle parity.

Task 9 fix4 DELIVERED (lib 9388c8e / outer a50b740): App.Core 653 (4 new), lib 1398 (golden byte-identity preserved, PublicApi
unchanged), gate 0/0. Lead-verified: Part A resolver sort removed + VM re-sort restored + writer single-sort (diff matches brief);
independent re-run golden+resolver+multiinput = 40/40. Impl refined codex F1: .NET List.Sort stable insertion-sort n<=16, so the
double-sort only diverges at 17+ tied volumes/chain (fixture uses 17, genuine RED->GREEN). Parts B/C doc-only [DIVERGENCE]; Part D
Advanced-tab rewired to reuse GenerateNestedSubtitleSrrsAsync (per-chain + oso-off), wizard-placeholder E6 applied + E4 deferred
to Task 10. Dual gate dispatched (peer review-task9-fix4 + codex bsp7697bu).

Task 9 fix4: peer review-task9-fix4 APPROVED-WITH-NITS (Task 9 can close). Confirmed Part A byte-identical to base (all inputs,
incl 17+ ties; only 2 self-sorting callers), B/C [DIVERGENCE] comments factually accurate + correct excerpt cites, Part D no
OSO-leak into any of 3 subtitle nested-SRR paths + correct per-chain naming. Two NON-BLOCKING nits:
  NIT1 (FYI, DIFFERENT path, TASK-11 FOLLOW-UP): .vob SAMPLE nested SRR (CreatorViewModel:1509, create_srr_single_volume /
    excerpt L766-767 — NOT a subtitle SRR) still forwards outer options → ComputeOSOHashes could pass through. Outside Part D
    scope. Whether pyReScene's single-volume path forces oso-off is NOT in the current excerpt → check during Task 11/whole-branch.
  NIT2 (trivial/cosmetic): test name ResolveOrderedChains_SpacedRarName_OneChain_BothVolumesSorted now stale (resolver no longer
    sorts). Fold into a later cleanup.
Awaiting codex fix4 verdict (bsp7697bu) before closing Task 9 (dual gate).

Task 9 fix4 codex re-review: REVISE — ONE Important, DOC-ACCURACY only ("no behavior change required"); codex CONFIRMED Parts
A/C/D resolved. Finding: Part B [DIVERGENCE] comment wrong about D8 — D8 (excerpt L809) keys on basename(esfv)[:-3]+"rar" = the
SFV's OWN STEM, not the listed RAR, so the divergence also fires for a same-dir proof SFV whose stem != its listed RAR basename
(Proof/meta.sfv listing proofpack.rar), not only the external ../RAR case. Peer missed this. VERIFIED vs excerpt. Lead fixed the
comment directly (doc-only, codex-dictated) → commit 8a8e4a1; App.Core build 0/0. Focused codex re-confirm launched (fix4b,
bnrxm5zbi) as the final Task-9 gate. Task 10 brief read (UI both surfaces + §4a a11y contract; a11y verified via plan contract +
Task-11 ava-desktop bridge, NOT the web accessibility-lead — Avalonia is desktop). CLOSE Task 9 on codex APPROVE.

Task 9 fix4b codex re-confirm: APPROVE. ===> TASK 9 COMPLETE (dual-gate: peer APPROVED-WITH-NITS + codex APPROVE). <===
  Delivered: folder-mode generated-artifact staging (SRS/nested-SRR/stored-list assembly + pass-10 reorder + multi-input writer),
  full-pipeline golden BYTE-IDENTITY vs pyReScene --vobsub-srr (PASSING), multi-chain subtitle SFVs, writer-identical SFV chain
  grouping (shared SfvVolumeResolver), single-sort byte-identity, Advanced-tab subtitle parity (per-chain + oso-off), F2/F3
  documented [DIVERGENCE]s. Commits: outer 00b4023..8a8e4a1, lib 09fe780..9388c8e. Suites: App.Core 653, lib 1398 (golden 5/5), gate 0/0.
  CARRY-FORWARD to Task 11/whole-branch: nit1 = .vob SAMPLE nested SRR (CreatorViewModel:1509, create_srr_single_volume) forwards
  outer options → possible OSO pass-through; whether pyReScene forces oso-off there is NOT in the excerpt — investigate. nit2 =
  stale test name ...BothVolumesSorted (cosmetic).
  NEXT: Task 10 (Manager UI both surfaces + §4a a11y contract), then Task 11 (E2E via ava-desktop bridge + whole-branch review).

=== TASK 10 STARTED (Manager UI both surfaces + §4a a11y contract) ===
Verified VM API matches plan: BrowseInputFolderCommand (BrowseInputFolderAsync :130, OpenFolderAsync), DetectedSets
(ObservableCollection<ReleaseSetInput> :118), ReleaseSetInput = record(SfvOrRarPath, RelativeName) — {Binding RelativeName} valid,
IsScanning (:122). Both surfaces (CreatorView.axaml input row :25-41; CreateSRRWizardBody.axaml step-0 :11-25) currently have ZERO
AutomationProperties → this feature = app's FIRST a11y annotations. Task 10 = pure markup (both surfaces) + RED-first headless
binding tests. Accessibility: Avalonia is DESKTOP (AutomationProperties/UI Automation, NOT web/ARIA) → dispatched
accessibility-lead (a11y-task10) review-FIRST per hook, Avalonia-briefed; runtime a11y verified via ava-desktop bridge in Task 11
(web accessibility-lead is not the authoritative Avalonia checker). Awaiting a11y requirements → then dispatch impl.

Task 10 a11y review (a11y-task10, Avalonia-briefed) — EXCELLENT, found real gaps beyond the plan §4a + a FUNCTIONAL BUG:
  P1#1 input TextBox needs accessible NAME (HelpText is secondary only) — LabeledBy/Name. P1#2 no non-visual busy/results signal
  (ProgressBar not announced) — live region. P2#3 ItemsControl not keyboard-reachable (informational OK + live-region summary).
  P2#4 folder button Name violates Label-in-Name (WCAG 2.5.3) — must contain "Browse folder". P2#5 FieldStatusLine silent — add
  LiveSetting=Polite (benefits all 5 forms). P2#6 FUNCTIONAL BUG: IsVisible="{Binding DetectedSets.Count}" int→bool won't bind →
  add HasDetectedSets bool. P3#7 count grammar, P3#8 tab order/TabIndex. Runtime items (LiveRegionChanged fires? focus return?
  wizard-step focus?) → Task 11 bridge.
  Reconciliation (task-10-brief.md): keep plan's ItemsControl + live-region summary; SINGLE live region via FieldStatusLine +
  InputStatus "Scanning…"→result transition (no double-announce); HasDetectedSets bool; accessible names (Name/LabeledBy);
  folder button Name="Browse folder for release input"; ProgressBar Name; explicit TabIndex; never-disable TextBox. Small VM adds
  (HasDetectedSets, DetectedSetsSummary, scan-start InputStatus). RED-first binding tests both surfaces + VM tests.
  → impl-task10 dispatched (opus). Task 10 slightly larger than plan's minimal markup because mandated a11y review found the
  baseline insufficient + a real binding bug.

Task 10 DELIVERED (outer 84fc47a, UI-only, no lib): App.Core 657 (+4 VM), Manager 189/192 (3 pre-existing FileCompare/Inspector
render fails, verified unrelated), gate 0/0. Lead-verified: both surfaces mirror the a11y contract (folder btn Label-in-Name Name,
file btn Name, TextBox Name[CreatorView]/LabeledBy[wizard]+HelpText, ProgressBar Name, DetectedSets ScrollViewer+ItemsControl
IsVisible=HasDetectedSets [int→bool bug FIXED], Name=DetectedSetsSummary, TabIndex 0/1/2); FieldStatusLine msg LiveSetting=Polite
(shared, additive); VM HasDetectedSets+DetectedSetsSummary via CollectionChanged hook (cleaner than per-site), scan-start
InputStatus=Info("Scanning…")→result. Independent re-run: 8 binding + 6 VM tests GREEN. Dual+ gate dispatched: codex (b3j5ja0q9,
focus=binding paths + VM logic + stuck-"Scanning…" on cancel/supersede), peer review-task10 (spec+quality+stuck-status trace),
a11y-task10 final confirm (findings addressed). Runtime a11y (LiveRegionChanged, focus) → Task 11 bridge.

Task 10 review: codex APPROVE (binding paths + VM logic + stuck-status all clear). a11y-task10 final confirm: all 8 findings
addressed, NO blockers. 2 soft notes both resolve to no-action: (1) completion announcement conveys the count — VERIFIED the
scan summary (CreatorViewModel:1234) leads "{N} RAR set(s) · …" so InputStatus.Ok(summary) via the live region is meaningful;
(2) MaxHeight=96 ScrollViewer not keyboard-scrollable — a11y-lead deems acceptable for read-only informational content. Awaiting
peer review-task10 (last verdict) to close Task 10.

Task 10 review: peer review-task10 APPROVED-WITH-NITS (no Critical/Important). Full contract confirmed on BOTH surfaces;
stuck-status trace CLEAN on all 3 named paths (Info set only in StartFolderScan; every supersede/cancel/reset overwrites it
synchronously; stale continuations generation-gated); tests real; CollectionChanged self-cycle no leak.
===> TASK 10 COMPLETE (3-gate: codex APPROVE + a11y-lead all-addressed + peer APPROVED-WITH-NITS). Commit 84fc47a. <===
CARRY-FORWARD to Task 11 / whole-branch triage:
  M1 (MINOR, pre-existing, Task 10 EXTENDS to InputStatus): RunFolderScanAsync catches only OCE; an unexpected non-OCE from
     ReleaseScanner.Scan would strand IsScanning + the announced InputStatus on "Scanning…" (a11y: stuck live-region). Low-
     likelihood (defensive scanner: FS→warnings/RootError, cancel→OCE). Optional catch-all: clear IsScanning + InputStatus=Error.
  N1 (nit): announced summary "{n} RAR set(s)" (CreatorViewModel:1234) vs grammatical DetectedSetsSummary "{n} RAR sets" — align.
  N2 (nit): DetectedSets binding test asserts IsVisible transitively, not a direct Assert.True. Optional.
  (Also still open: fix4 nit1 = .vob SAMPLE nested SRR create_srr_single_volume oso-forward; fix4 nit2 = stale test name.)

=== TASK 11 STARTED (E2E via ava-desktop bridge + full gates + whole-branch review) ===
Steps: (1) bridge E2E Advanced surface 2-disc folder → DetectedSets CD1/a.sfv+CD2/b.sfv, automation names (ava_props), create;
(2) Inspector+Reconstructor show both discs; (3) wizard surface pre-populated; (4) full lib+App.Core+Manager + gate 0/0;
(5) whole-branch review (BASE=merge-base main HEAD) codex+peer → ONE fix subagent for ALL findings incl carry-forwards
(M1 stuck-status, N1/N2, fix4 nit1 .vob oso, fix4 nit2 test name) → re-verify → finishing-a-development-branch.

Task 11 progress:
  Full gates: lib 1398, App.Core 657, Manager 189/192 (3 pre-existing FileCompare/Inspector render), gate 0/0.
  E2E (ava-desktop bridge, wizard surface, live 2-disc folder C:\...\e2e-2disc\tree-2disc):
    ✓ automation names surface correctly: folder btn Name="Browse folder for release input", file btn="Browse input file"
    ✓ folder input triggered a LIVE scan → DetectedSets populated CD1/a.sfv + CD2/b.sfv (RelativeName display)
    ✓ DetectedSets container automation Name="2 RAR sets" (grammatical DetectedSetsSummary)
    ✓ FieldStatusLine status="2 RAR set(s) · 0 sample(s) · 4 stored file(s)" (live-region conveys count; stored list = nfo+2sfv+subs.sfv)
    (N1 visible: status "set(s)" vs list Name "2 RAR sets"). Create path itself golden-byte-identity + App.Core VM proven.
  Whole-branch review: codex APPROVE — 0 Critical/Important cross-task findings; full pipeline coherent (6 result collections,
    ordering composition, dedup, gating, both UI surfaces). Triage: M1=post-merge hardening OK, N1=cosmetic, fix4-nit1=NOT
    establishable as a violation from the excerpt (create_srr_single_volume oso default not in excerpt) → leave. Awaiting peer whole-branch.

Whole-branch review: peer review-wholebranch APPROVED-WITH-NITS (merge-ready). All 5 integration focus areas PASS (result
produced/consumed incl. IsRootError sentinel safety; whole-pipeline ordering composition; dedup incl. subtitle single-store;
CTS lifecycle F1 holds; both UI surfaces + shared stateless scanner safe). Peer RECOMMENDS fix4-nit1 before merge — it checked
pyReScene SOURCE (main.py:588 create_srr_single_volume). LEAD-VERIFIED directly: create_srr_single_volume (main.py:588-640)
writes ONLY header+SrrRarFileBlock+raw RAR blocks, NO oso logic → the .vob-sample nested SRR forwarding outer options
(CreatorViewModel:1547) IS a real byte divergence (oso-on). Peer caught what codex couldn't (excerpt lacked the fn body).
NEW peer Minor (DEFER→follow-up): Advanced-tab Options (AutoCreateSRS/CreateVobsubSRR/StoreFixRAR) dead in folder mode (visible
but no effect; design-by-omission, spec §3). Final cleanup round dispatched (impl-task11-cleanup): C1=fix4-nit1 vob oso-off
(parity), C2=M1 scan non-OCE hardening, C3=N1 summary grammar, C4=stale test renames. fix4-nit1 = the last parity item.

Cleanup round: impl flagged C4's stale test name is INSIDE the lib submodule (SfvVolumeResolverTests.cs:50) — contradicts the
outer-only scope I set. DECISION: defer C4 → trivial lib-side follow-up (rename ResolveOrderedChains_SpacedRarName_OneChain_
BothVolumesSorted, drop "BothVolumesSorted"). Other *Sorted* hits confirmed non-stale. Cleanup round = C1(parity)/C2(hardening)/
C3(grammar), outer-only. FOLLOW-UPS after merge: [lib] C4 test rename; [app] Advanced-tab options dead in folder mode (hide/
disable or honor); [open] fix4-nit1-adjacent none. fix4-nit1 itself is being FIXED (C1).

Cleanup delivered (outer 33a5f32; lib unchanged 9388c8e): C1 vob-oso-off (parity, nestedOptions cites main.py:588-640),
C2 non-OCE scan hardening (generation/_scanCts-gated Post, fails closed like IsRootError, F1 invariants preserved),
C3 summary reuses DetectedSetsSummary. App.Core 659, lib 1398 (golden 5/5), Manager 189/192, gate 0/0. Lead-verified:
golden 1/1 byte-identity + Vob/FolderMode 36/36 GREEN (independent), diff correct. Review gate: codex (bvfh01yww) + peer
review-cleanup (C2 CTS-lifecycle trace). On clean → close feature → finishing-a-development-branch (present merge/PR/keep/discard to USER).

Cleanup review: codex APPROVE + peer review-cleanup APPROVED (exhaustive C2 CTS-lifecycle trace: F1 holds, same-gate, mutual
exclusion of success/OCE/non-OCE, cts disposed once across all interleavings, fails closed like IsRootError/F3; C1 byte-identical
to E6 siblings; C3 populated on only reaching path).
=========================================================================================================================
===> FEATURE COMPLETE: multi-set SRR creation (Tasks 1-11). All tasks dual-gate approved; both whole-branch reviews APPROVED;
     final cleanup dual-gate approved; E2E runtime-verified (ava-desktop bridge, live 2-disc scan); golden BYTE-IDENTITY vs
     real pyReScene --vobsub-srr preserved throughout. HEAD outer 33a5f32 / lib 9388c8e. Suites: lib 1398 (golden 5/5),
     App.Core 659, Manager 189/192 (3 PRE-EXISTING FileCompare/Inspector render fails, unrelated), gate 0/0.
     POST-MERGE FOLLOW-UPS: [lib] C4 test rename (SfvVolumeResolverTests:50 drop "BothVolumesSorted"); [app] Advanced-tab
     options (AutoCreateSRS/CreateVobsubSRR/StoreFixRAR) inert in folder mode (hide/disable or honor). Both non-blocking.
     NEXT: finishing-a-development-branch → present merge/PR/keep/discard to USER.
=========================================================================================================================

USER: "Do the follow-ups first" → asked my recommendation for #2. FOLLOW-UP #1 DONE: lib aa0fdd9 (test rename
BothVolumesSorted→BothVolumesGrouped) + outer gitlink bump 2a7f6e5. FOLLOW-UP #2: RECOMMENDED + proceeding with option B
(honor AutoCreateSRS↔--no-srs + CreateVobsubSRR↔--vobsub-srr in folder mode; StoreFixRAR stays auto=pyReScene, checkbox
disabled in folder mode). Rationale: parity (maps to pyReScene flags) + consistency (file mode already honors them, :826/832/838)
+ defaults all true so no default-behavior change + golden (lib test) unaffected. Brief: followup-folder-options-brief.md.
impl-followup-opts dispatched (opus): B1 gate sample-SRS on AutoCreateSRS, B2 gate nested-sub pass-9 on CreateVobsubSRR (keep
pass-10 SFV storage), B3 public IsFolderMode + disable StoreFixRAR checkbox in folder mode (a11y HelpText reason). Then review gate → finish.

USER: "if codex agrees with your recommendation, go with that." codex assessment: Recommendation: B (AGREES, substantive) —
reasoning matches: AutoCreateSRS↔--no-srs + CreateVobsubSRR↔--vobsub-srr honored for parity+file-mode consistency; StoreFixRAR
no pyReScene flag → stays auto + DISABLED (not hidden); defaults true → no default-behavior change; codex adds "narrowly gate
only the two staging ops, leave fix-RAR untouched" = exactly the B1/B2/B3 brief. → PROCEEDING with B (impl-followup-opts running).
Then dual-gate review → finishing-a-development-branch.

Follow-up #2 (option B) DELIVERED (outer 0d4d7fa; lib unchanged aa0fdd9): App.Core 665 (+6 B tests), lib 1398 (golden 5/5),
Manager 189/192, gate 0/0. Lead-verified: diff matches codex-agreed design (B1 AutoCreateSRS gate on GenerateSampleArtifactsAsync;
B2 generateNestedSrrs threaded, gates ONLY pass-9 loop, pass-10/E3/D8 intact; B3 public IsFolderMode notified at all 3 sites +
CreatorView disables only StoreFixRAR checkbox w/ HelpText). Independent re-run: golden 1/1 byte-identity + FolderMode/Artifact
63/63 GREEN. Dual gate: codex (bukhvegql) + peer review-followup-opts (B2 staging-integrity trace + B3 a11y). On clean →
finishing-a-development-branch (present merge/PR/keep/discard to USER).

Follow-up #2 (option B) review: codex APPROVE + peer review-followup-opts APPROVED (exhaustive: B2 pass-9 short-circuits clean,
pass-10/E3/D8 intact, CreateVobsubSRR=true byte-identical to before; B1 no orphaned media; B3 IsFolderMode notified at all 3
sites, fix-RAR auto, only StoreFixRAR checkbox disabled, InverseBoolConverter resolves, a11y correct; both-off edge safe; no
stale sigs). Defaults true → default behavior + golden unchanged.
===> BOTH FOLLOW-UPS COMPLETE: #1 lib rename (aa0fdd9/gitlink 2a7f6e5), #2 option B folder-mode options (0d4d7fa, codex-agreed
     + dual-gate approved). HEAD outer 0d4d7fa. Suites: lib 1398 (golden 5/5), App.Core 665, Manager 189/192 (3 pre-existing),
     gate 0/0. NEXT: finishing-a-development-branch (re-present merge/PR/keep to USER).

===== RELEASE PREP (user: merge+release the whole port; codex review at each step) =====
Merge is a clean FAST-FORWARD (main at branch-point 5648cec, 0 commits since; 176 commits to land = WHOLE port). avalonia-feature
NOT pushed to origin. 3 WORKSTREAMS:
  WS1 (fix 3 red tests — release blocker): ROOT-CAUSED = stale tests. Views use fixed-choice ComboBox (SelectedItem→HexBytesPerLine,
     InspectorView:377 / FileCompareView:238/380) for Bytes/Row; the 3 tests still assert the REMOVED NumericUpDown → 0 found.
     Fix = test-only (NumericUpDown→ComboBox; repurpose obsolete ClearingBytesPerRowSelector_ToNull). Brief release-ws1-testfix-brief.md.
     impl-ws1-testfix dispatched. VIEWS ARE CORRECT — no .axaml change.
  WS2 (release mechanics): USER wants the lib-version==tag condition REMOVED (app-only releases shouldn't force a pointless lib
     release). + push, version bump, tag. Release process is tag-driven ([[release-process]]).
  WS3 (port release-readiness review): review the 176-commit port beyond the multi-set feature. Will run as a multi-agent Workflow
     (ultracode on) fanning out over subsystems. codex review per step.

WS2 DONE (config): removed lib-release coupling from .github/workflows/release.yml (libcheck + Tag/Release ReScene.Lib steps).
Rationale = USER intent ("releasing a lib with no changes because the app is released is pointless") over the literal "remove
the condition" (which would've made it release unconditionally = worse). App tags now release ONLY the app; lib released on its
own. Commit e314c54. The actual release (version + tag + push) is the LAST step, after WS1(tests green)+WS3(readiness)+merge.
Codex review of WS2 will bundle with WS1's (both release-prep steps). WS3 port-readiness Workflow running (wleo5yox7). WS1 impl running.

WS1 DONE (9852c54): 3 stale view tests updated NumericUpDown→ComboBox; Manager 192/192, gate 0/0. Impl caught my brief error:
Avalonia IsVisible=false does NOT remove from the VISUAL tree (GetVisualDescendants) — Inspector has 2 ComboBoxes (bytes/row +
Encoding) so it filters `.Single(cb => cb.SelectedItem is int)`; FileCompare has only 2 bytes/row (no Encoding). Obsolete null-clear
test REPURPOSED → BytesPerRowComboBox_ExposesFixedPresets_AndSelectionRoundTripsToVm. (Visual tree ≠ automation tree — Task-10
a11y reasoning unaffected.) Lead-verified: 3 target tests green independently.
Combined codex review WS1+WS2 launched (bc9tihs17). WS3 Workflow (wleo5yox7) still auditing. Chain: a13d0d6→9852c54(WS1)→e314c54(WS2).

WS1+WS2 codex review: APPROVE (no Critical/Important). 3 non-blocking nits: (1) FileCompare test assumes all ComboBoxes are
bytes/row (fragile if Encoding selector added), (2) Inspector test keys on SelectedItem-is-int not item-type/name, (3) release.yml
release-job checkout/version-step orphaned. Did nit3-partial: removed dead release-job version-extract step (commit 773a5af);
left the checkout (harmless). Nits 1+2 left (tests correct+pass; future-robustness only). ===> WS1 + WS2 COMPLETE + codex-approved. <===
Awaiting WS3 port-readiness Workflow (wleo5yox7) → triage blockers → fixes → THEN release (ff-merge + version + tag + push; version confirmed with user first).

WS3 port-readiness Workflow (wleo5yox7, 9 agents/1.18M tok/16.6min) DONE — verdict READY-AFTER-FIXES. 6/8 areas release-ready
(shell/Home, SRR editor, RAR reconstructor, sample restorer, services-infra all clean incl live bridge smoke-tests). Found 2
BLOCKERS + 4 IMPORTANT. Lead-VERIFIED both blockers + important 3/6 against code. USER: "fix ALL 6".
  B1 SRSCreatorViewModel:40/165 — DefaultOutputDirectory seeds OutputPath=bare dir; auto-suggest only fills when blank → CreateSRS
     passes dir as output file → silent no-op. Fix: OnInputPathChanged use FieldGuidance.SuggestSaveFileName (like BrowseOutputAsync).
  B2 FileCompareView.axaml.cs:169 — Copy menu resolves grid via ContextMenu.PlacementTarget (WPF-ism); Avalonia may not set it →
     dead. Fix: resolve via sender/VM SelectedProperty; red-green VERIFY.
  I3 InspectorViewModel:705 — Add/Remove stored-file: ReleaseFileHandles then LoadFile only on success (no finally) → edit error
     blanks Hex/Text till reopen. Fix: finally-reload (mirror Rename/Move).
  I4 InspectorViewModel:424 — Export CanExecute off-by-one + stale after close. Fix: NotifyCanExecuteChangedFor + CloseFile renotify.
  I5 FileCompareViewModel:825 — FindMatchingNodeRecursive fallback (Root && FileName==null) collapses RAR placeholder nodes onto root.
  I6 SRSReconstructorViewModel:331 — cancel returns Success=false → red "Failed" banner. Fix: detect cancel → neutral state (like SRSCreator).
  → impl-ws3-fixes dispatched (opus, TDD, report false-positives). Brief: release-ws3-fixes-brief.md. Then codex+peer review → release.

WS3 fixes DELIVERED (30f5152): all 6 reproduced RED-first (no false positives). Impl decompiled Avalonia 11.3.18 to PROVE
Blocker 2 (auto-open sets the popup's PlacementTarget, not ContextMenu.PlacementTarget → old resolver read null). App.Core 675,
Manager 194 (+2 copy-menu tests), golden byte-identity intact, gate 0/0. Lead-verified: blocker diffs clean (B1 SuggestSaveFileName
+no-clobber guard; B2 grid-by-ContextMenu-identity + VM SelectedLeft/RightProperty), independent suites 675/194/golden-1 GREEN.
Dual gate: codex (bgnoerc1r) + peer review-ws3-fixes (esp. B2 left-vs-right pane trace). On clean → whole feature+port RELEASE-READY
→ present release plan (ff-merge main + version + v-tag + push) to USER, confirm version, execute.

WS3-fixes codex review: REVISE — 1 Important (Fix 5 regression), other 5 CONFIRMED correct. LEAD-VERIFIED: FindMatchingNodeRecursive's
has-Data disambiguation (FileCompareViewModel:874-884) treats BuildDetailed's root as a PLACEHOLDER because that root alone has
Data==null (FileCompareTreeBuilder:29) — unlike every other root (BuildSRR:59/SRS:190/MKV:249/fallback-RAR:310 all set Data). So
two detailed-RAR roots with DIFFERENT block counts (labels embed the count) no longer sync (root↔root regressed). The guard test's
root used Data=new object() (non-null, test:50) → masked it. Fallback placeholders (Signature/CMT/End Archive :319/356/363) are the
real NodeType.Root+Data==null nodes. FIX options: give BuildDetailed root a non-null identity (CAVEAT: check root property-display
doesn't break) OR structurally detect the true root in the matcher; + a BuildDetailed unequal-block-count regression test.
Waiting for peer review-ws3-fixes to bundle any more findings, then ONE fix round.

Fix-5 SAFE FIX designed: give BuildDetailed root Data=blocks (FileCompareTreeBuilder:29), matching all other roots → matcher's
sourceHasData&&nodeHasData treats it as root↔root (any label/count); placeholders (Data==null) still label-match (Item 5 preserved).
SAFE for property display: CompareNodePropertyBuilder switch (:46) has NO Root case → Data on a Root node is matcher-identity only,
never displayed. + add BuildDetailed unequal-block-count root-sync test (codex's ask); fix guard test's synthetic-Data root.
Holding for peer review-ws3-fixes to bundle.

WS3-fixes peer review-ws3-fixes: NEEDS-FIXES — items 1,2,3,4,6 APPROVED (explicit B2 left-vs-right trace confirmed), item 5 SAME
regression codex found (INDEPENDENT convergence): BuildDetailed root null-Data → matcher treats as placeholder → detailed-RAR
(PRIMARY path, VM:753) root sync breaks when block counts differ. Both recommend Data=blocks on FileCompareTreeBuilder:29.
LEAD APPLIED the fix (Data=blocks) + added real-BuildDetailed unequal-count regression test (SelectingDetailedRarRoot). RED-GREEN
PROVEN: with fix 4/4 green; reverted → new test RED. Safe (property builder has no Root case → Data on root is matcher-only).
Full-suite verify running (bh6265u2u). Then commit → codex re-review → release.

Fix-5 correction committed 4566b3c: FileCompareTreeBuilder root Data=blocks + real-BuildDetailed regression test. Full verify GREEN:
App.Core 676, Manager 194, golden 1 (byte-identity), gate 0/0. codex Fix-5 re-review launched (bo9x3nw3s).
RELEASE PRECONDITIONS gathered:
  - App: last tag v1.9.0; version is TAG-injected (no csproj <Version>). main ff-able (181 ahead, 0 behind).
  - LIB submodule: HEAD aa0fdd9 on its own avalonia-feature branch; lib origin/main=5a892d2 (prepare v1.9.0). Lib avalonia-feature
    shows NO upstream → lib commits likely NOT PUSHED to ReScene.Lib remote. RELEASE NEEDS the gitlink's lib commit on the lib
    remote (CI checkout --recursive) → must reconcile+push lib before app release. (= memory's "reconcile lib at merge".)
  RELEASE SEQUENCE (present to USER, get version + authorize pushes — all OUTWARD-FACING):
    1. Lib: reconcile lib avalonia-feature → lib main (or push the branch), push to ReScene.Lib remote so gitlink resolves.
    2. App: ff-merge avalonia-feature → main.
    3. App: version (v1.9.0 → v2.0.0? major port — USER decides) + tag + push main+tag → triggers release.yml (app-only release now).

codex Fix-5 re-review: APPROVE. ===> ALL WS3 FIXES COMPLETE + dual-gate approved (5 approved + Fix-5 corrected). <===
====> WHOLE PORT RELEASE-READY: WS1 (tests, codex✓) + WS2 (release mechanics, codex✓) + WS3 (audit + 6 fixes, dual-gate✓). <====
Suites: App.Core 676, Manager 194, lib 1398 (golden byte-identity), gate 0/0. HEAD outer 4566b3c / lib aa0fdd9.
NEXT (all OUTWARD-FACING, needs USER version + push authorization): lib reconcile+push → app ff-merge main → version+tag+push.

LINUX SMOKE TEST (pre-release, user-requested): published linux-x64 self-contained single-file (Release, -p:Version=2.0.0),
ran on WSL Ubuntu 26.04 via WSLg. RESULT: SUCCESS — Beginner hub renders fully (Fluent dark, Inter fonts, all cards+menu),
status bar shows "ReScene Manager v2.0.0 (4566b3c…)", process stable + no startup errors, all native libs (libX11/ICE/SM/
fontconfig/icu) already present on Ubuntu (no apt needed). Validates the linux-x64 release artifact. App left running (PID 618).
RELEASE STILL PAUSED at safe local checkpoint (lib pushed origin/main=aa0fdd9; app main ff-merged locally to 4566b3c; NOT pushed).

INPUT-DESCRIPTION UX FIX (user-reported: wizard had Browse + Browse folder buttons with no text saying
which to use when). Added a multi-Run caption + per-button HelpText on BOTH surfaces (wizard
CreateSRRWizardBody + Advanced CreatorView). DUAL-GATE: a11y-lead APPROVE (mechanism: Label-in-Name,
field-vs-action HelpText split, contrast 6.2:1). codex FIRST pass REVISE — folder copy overpromised
("every subfolder"/"all its RAR sets"); scanner only discovers loose-RAR sets when zero SFVs exist
(ReleaseScanner:330) + skips unreadable/reparse-point subfolders (ReleaseTraversal:57). REVISED copy to
"search a release folder and its subfolders for RAR sets" (no exhaustive guarantee). codex RE-REVIEW
APPROVE (copy accuracy verified vs scanner, U+2026 parity, unchanged Names, valid XAML) + a11y-lead
re-confirm APPROVE. Manager binding tests 16/16; CreatorView caption runtime-verified via ava bridge.

SAVE-LOG BUTTON (user-reported: wizard step 5 / Create step had no way to save the creation output "in
case of problems"). Added a "Save log..." button to CreateSRRWizardBody step 4's log header, bound to the
existing CreatorViewModel.SaveLogCommand (no VM change) — a faithful clone of the sibling operation views'
Save-log affordance (WCAG 3.2.4). DUAL-GATE: a11y-lead APPROVE/SHIP (Content-as-name, always-enabled,
natural tab order, ASCII "Save log..."); codex APPROVE (no findings — wiring, step-4 placement, pattern
fidelity, non-vacuous test). Added headless binding guard (button present + routes to SaveLogCommand →
SaveFileCalls==1). Manager 195, App.Core 676, gate 0/0.

STALE INPUT-LABEL FIX (user-reported: wizard step 0 field label "Release .sfv or first .rar" didn't mention
the now-accepted folder input). Updated WizInputHeader → "Release .sfv, first .rar, or folder" (that header
is also the field's a11y name via LabeledBy, so visible label + accessible name update in lockstep).
HelpText kept as-is. DUAL-GATE: a11y-lead APPROVE/SHIP (clean LabeledBy update, "or folder", keep HelpText);
codex APPROVE (no findings). Added non-brittle guard (header mentions folder + .rar). Manager 196, gate 0/0.

LINUX/macOS WINRAR-VERSION PARSER FIX (user-reported on Ubuntu: RAR Reconstructor said "No WinRAR versions
found" when pointed at extracted Linux WinRAR folders). Root cause: RARVersionSelector regex only grokked
Windows naming (winrar-560); Linux/macOS tarball folders (rarlinux-x64-5.5.0, rarosx-3.1.0, rarlinux-x64-611,
betas) have a platform token + dotted versions → all rejected. FIX (lib): regex gains (linux|osx|macos|bsd)?
+ -arm + dotted-version capture, dots stripped so 5.5.0→550/3.9.3→393; Windows names byte-identical (verified
empirically + existing tests). App: OS-aware "no versions" message (RarExecutable.FileName, not hardcoded
rar.exe) + portable scanner test + *nix-naming scanner regression test. DUAL-GATE: a11y-lead APPROVE/SHIP
(message = net a11y win); codex APPROVE (no findings, 13-case regex verified). Lib 1410, App.Core 677, gate 0/0.
Deferred a11y follow-ups (a11y-lead, app-wide, NOT blockers): MessageDialog body not auto-announced on open;
severity glyph (info/warn/error) not marked AutomationProperties.AccessibilityView=Raw. Heads-up to user: rar
binaries need chmod +x on Linux (Windows copy loses exec bit).

RAR LAUNCH-FAILURE = "ERROR" ROWS (user-reported on Linux: un-chmod+x'd rar binaries showed every combo as
"Complete/No Match" instead of an error). FIX (lib): BruteForceProgressEventArgs.CombinationFailed flag; the
brute-force catch fires it; the CompleteAllVolumes path (beginner default) now observes the faulted process
task (Task.WhenAny didn't surface it); double-count guard for late verify/rename exceptions; OperationProgress-
EventArgs CLAMPS out-of-range progressed instead of throwing (denominator is a Phase-1-scaled approximation).
App: tracker.ErrorActiveVersion marks the row Status="Error"/Result="Run failed" + releases pointer; VM completion
heading gains "(N could not run)" aggregate; heading is a Polite live region. TRIPLE-ROUND dual-gate: a11y-lead
SHIP (adjudicated codex's 4.1.3 per-cell finding -> text-only grid cells UPHELD + Polite aggregate completion
heading is the correct locus); codex APPROVE round 3 (r1: double-count + 4.1.3; r2: CompleteAllVolumes fault +
progress overshoot throw; all fixed). Lib 1414, App.Core 679, Manager 197, gate 0/0.

WIZARD LOG VISIBILITY + SAVE LOG (user-reported: launch-failure errors invisible in the Reconstruct wizard's
Details; no way to save the log). App-only: (A) completion appends aggregate System-log line when combos
errored ("N combination(s) could not run - use Save log... to see each failure (Phase 2 section)."); (B)
"Save log..." button on the wizard Details header -> existing SaveLogCommand (writes System+P1+P2 sections);
(C) LabeledBy names the log TextBox "Details" (4.1.2). TWO-ROUND dual-gate: a11y-lead APPROVE (wording/
pattern/LabeledBy) + TWO adjudications on record: (1) wizard-local live completion line WITHDRAWN - the
BruteForce modal cannot be dismissed mid-run (OnClosing -> Stop), so its Polite heading is the guaranteed
4.1.3 carrier in wizard runs; a background live region = inert/double-speak. (2) codex's VALID finding
(Save-log outcome unannounced; silent-failure harm) ruled PRE-EXISTING+UNIFORM across ALL Save-log surfaces
-> committed dedicated app-wide fix (dedicated Polite save-status via FieldStatusLine on ALL SEVEN surfaces
- codex round-2 corrected the count from six), TOP of tracked sweep, sequenced immediately after the log
merge. codex round-1 REVISE (that finding + user's own untracked FolderProfile.pubxml misread as scope);
round-2 APPROVE with both adjudications. pubxml = user's local VS linux-x64 publish profile, left alone,
gitignore offer pending with user. App.Core 680, Manager 198, gate 0/0.

TRACKED A11Y SWEEP (priority order): 1. Save-log outcome Polite announcement (SEVEN surfaces, after log
merge, TOP). 2. logList context-menu Copy (app-wide; precedent: BruteForce grid Copy menu). 3. MessageDialog
body auto-announce + severity glyph AccessibilityView=Raw. 4. Sibling log-pane LabeledBy names.

NEXT (user-approved): Reconstructor LOG MERGE - one chronological LogEntries (ObservableCollection) +
logList ListBox on BOTH surfaces; [P1]/[P2] prefixes + run-start legend line; Advanced TabControl -> one
pane, AutoScrollToEnd bound to AutoScrollLog checkbox (caret trick deleted); SaveLogAsync drops section
stitching; aggregate line reworded "...each failure is logged above."; LabeledBy both panes ("Log"/
"Details"). a11y-lead design-APPROVED (Q1-Q4 answered; logList navigable+conditional-scroll verified).

LOG MERGE (user-approved): Reconstructor's three logs (System/Phase1/Phase2, WPF-era) merged into ONE
chronological LogEntries (ObservableCollection) — the split had hidden Phase-2 launch failures from wizard
users. [P1]/[P2] tags stamped at enqueue + run-start legend line; #20 queue/flush/generation guard intact;
Advanced 3-tab TabControl -> one virtualized logList ListBox (AutoScrollToEnd bound to the Auto-scroll
checkbox; caret-trick code-behind deleted); wizard Details -> same ListBox; SaveLogAsync writes the log
verbatim; aggregate reworded "...each failure is logged above."; LabeledBy names both panes (Log/Details).
Net -60 lines, 12 files, app-only. TWO-ROUND dual-gate: a11y-lead design-APPROVE (Q1-Q4) + fix-APPROVE;
codex r1 REVISE (REAL: live collection passed to async export -> "Collection was modified" partial-file
risk, FIXED with UI-thread snapshot; long [P2] lines clipped in both ListBoxes, FIXED with
HorizontalScrollBarVisibility=Auto on both + headless guards — a11y-lead approved h-scroll over wrap:
SR-neutral, consistency-positive, wrap worse for magnifier users; Save log = guaranteed full-text path for
keyboard users, ToolTip rejected); codex r2 APPROVE (0 crit/0 major/0 minor, all concurrency/order/labeling
guarantees reconfirmed). Suites: App.Core 681, Manager 198, gate 0/0; bridge smoke: merged Advanced pane
renders (single Log header row, tabs gone). SWEEP NOTE: sibling OperationViewModelBase.SaveLogToFileAsync
has the same latent live-collection export pattern — snapshot hardening folds into the committed app-wide
Save-log-outcome change (it touches that exact method).

LINUX RAR INPUT-MASK FIX (user-reported: post-chmod, rar executes but "arguments might be wrong" — the
diagnostic log was rar's HELP screen from a switches-only paste; all switches valid). REAL root cause:
RARProcess hardcoded input mask ".\*" — on Linux backslash is a plain filename char, rar matched ZERO
files, created no archive, every combination read as clean "No Match". FIX (lib): mask composed with
Path.DirectorySeparatorChar (".\*" Windows byte-identical / "./*" Unix); NormalizeOutputFileName already
stripped both prefixes. + RARProcessArgumentTests (arg order + platform mask). codex APPROVE (also
confirmed a11y hook inapplicable — lib-only). Lib 1415, gate 0/0.

COMPLETED-RUN FAILURE CLASSIFICATION (user-reported: rarlinux-3.1.0 loader failure "error while loading
shared libraries ... libstdc++-libc6.2-2.so.3" showed as Complete/No Match). Root cause: loader failures
START as a process (no exception) -> exit 127, stderr only, no archive -> the not-created branch treated it
as an ordinary no-match; the earlier CombinationFailed path only fired from the catch. FIX (lib): keep the
completed process exit code (standard: RARCompressDirectoryAsync return, previously DISCARDED; CAV: task
Result when IsCompletedSuccessfully); not-created + known non-zero exit -> WARNING w/ exit code + Combination-
Failed event (Error row + aggregate). GATE SUBSTITUTION: codex QUOTA-EXHAUSTED until 2026-07-29 07:05 ->
peer adversarial reviewer (fresh Claude agent, same brief) substituted, TWO ROUNDS: r1 REVISE with a REAL
regression — RARProcess.RunAsync SWALLOWS the cancellation OCE and returns exit 1, so user Stop before first
file would fire a spurious "Run failed" (both modes; my brief's premise 1c/1d was wrong) + 2 inaccurate
comments; FIXED by folding a cancellationRequested arm into the classifier (IsCompletedRunFailure(exit,
cancel)) + comment corrections + 4-row cancellation Theory. r2 APPROVE (proved laundered-1 unreachable with
flag false: the value only arises from _cts-linked cancel, monotonic; conservative suppression on the
cancel-races-genuine-failure edge endorsed as the right tradeoff). Lib 1426, gate 0/0.
CODEX CATCH-UP QUEUE (required before release push unless user waives): this exitcode change (lib c7e9fb3..HEAD).

RUNNABLE COPY FULL COMMAND LINE (user-requested: "It should probably copy the whole command, as-is run" -
the switches-only copy printed rar's usage screen and twice suggested broken args). Events carry
InputDirectoryPath/OutputFilePath/ExecutedArguments (BuildFinalArguments HOISTED - pure - so all FIVE
fire sites incl. the row-creating one carry them); tracker createRow widened; VersionEntry.FullCommandLine
composes cd/pushd "<inputDir>" && "<rar>" <EXECUTED args> "<out>" mask (Windows=cmd dialect by documented
choice); ExeAndArguments keeps the terse display form for the "Testing" log lines (a11y-lead's
dual-consumer catch - log stays byte-identical). GATES: a11y-copycmd APPROVE (payload-only; label kept;
3 pre-existing minors -> sweep: VersionGrid unnamed, silent no-op copy w/o selection, right-click-selection
check). peer-copycmd-review THREE ROUNDS: r1 MAJOR - copied line used DISPLAY args, omitting engine-added
-ma4/-z/-vn -> could silently build RAR5 where the run forced RAR4 (the exact silent-divergence the feature
exists to kill); FIXED executed-args plumb (r1 also verified 5-site coverage + honest note: plumb test is
hand-built events, fire-site population rests on review). r2 MODERATE - -z<commentfile> joined unquoted ->
splits under output path with spaces; FIXED JoinExecutedArguments whole-token quoting (quoting never leaks
into the real invocation). r3 APPROVE (re-verified from scratch). Lib 1428, App.Core 686, Manager 198, gate
0/0 (one CA1307 fixed). CODEX CATCH-UP QUEUE now: exitcode change + this change.

KEEP WORK FILES SETTING (user-specified semantics: ON=clear on finish, OFF=keep, OFF BY DEFAULT — flips
the app default to keeping scratch for diagnostics after the user lost per-attempt rar logs to cleanup).
AppSettings.CleanupReconstructionWorkFiles (default false) + SettingsViewModel round-trip + SettingsWindow
"Work files" block (header+CheckBox+caption per the Mode-row anatomy; a11y REVISE condition adopted:
AutomationProperties.HelpText on the CheckBox); ReconstructorViewModel: run-scoped capture at
RunArchiveSetsAsync start, setting-gated CleanupWorkRoot (ALL deletion funnels through it — peer verified
no bypass incl. lib sites; InputDirectoryPreparer's delete is a pre-clean), "Work files kept: <path>"
per kept set EXISTENCE-GATED (peer r1 F1: a set failing pre-scratch — BuildOptionsForSet throw — logged a
never-created path; PROVEN by probe, fixed + regression test the peer RED-VERIFIED by temporarily
reverting the fix, md5-checked restore). Also: 3 stale comments fixed (F2); pre-run confirm text now says
"— including any kept work files —" (F3b); RollBack message names the preserved workRoot (F4).
RECORDED CONSEQUENCES (F3a): repeat runs into the same output folder now ALWAYS confirm-prompt, and
confirming wipes the previous run's kept files (pre-run ClearReservedSubtrees unchanged, confirm-gated);
kept scratch in abandoned output folders has no GC. GATES: a11y-scratch APPROVE (verified in markup);
peer-scratch-review 2 rounds APPROVE (also verified: fail-safe direction — corrupt settings -> keep;
composition root passes real settings service; TempDirTestBase reclaims CI scratch). HONEST GAPS (peer):
SettingsViewModel.Save untested app-wide (pre-existing); no real-run disk-retention test; multi-set
kept-line cardinality untested. App.Core 689, Manager 199, gate 0/0. SWEEP ADDITION: SettingsWindow
TextBox/NumericUpDown fields lack programmatic labels (a11y-scratch, pre-existing, own follow-up).

CAV PROCESS-LOG FIX (user: "I don't see any logs being created" — kept work roots had input/+output/ but
never logs/). Root cause: OpenLog called ONLY from RARCompressDirectoryAsync (standard path); the
CompleteAllVolumes branch (the WIZARD DEFAULT — BeginnerWizardFactory hard-sets CAV=true) never registered
its process; WriteOutput = silent TryGetValue no-op → NO CAV run ever produced a per-process log. FIX
(lib, 2 lines): OpenLog(options.OutputDirectoryPath, rarFilePath) in the CAV branch before subscribe/run.
PEER APPROVE (warm peer-scratch-review): close symmetry proven on all exit paths (success/swallowed-cancel/
error-rethrow + early-kill/user-Stop reduce to cancel; the close mechanism was already debugged in anger —
RARProcess.cs:228 comment); ctor-before-OpenLog ordering load-bearing (missing binary can't strand a
writer); recorded follow-ups: (a) same-stem same-second log collision truncates under append:false
(pre-existing, both paths; fix = attempt counter or append:true), (b) log path ~14 chars longer than
archive path near MAX_PATH, (c) AutoFlush per-line I/O now also paid by CAV runs. OBSERVED-CORRECT: ran a
throwaway probe with REAL Rar.exe (wrar602) exercising the exact OpenLog→stream→CloseLog sequence — logs/
created, non-empty log, exit 0 — then deleted the probe (peer's reasoned-vs-observed recommendation).
CODEX CATCH-UP QUEUE: now five peer-gated changes.

## 2026-07-26: run-rar wrapper resolution + platform-labeled pack links
- Lib f9d0101: RarExecutable.ResolveIn prefers a version dir's run-rar launcher on Unix (internal seam preferUnixWrapper; Windows never shadowed; public API unchanged, baseline untouched). +2 seam tests (lib 1430).
- App bae7611: ReconstructorView header now three labeled links - Windows extracted pack, NEW Linux wrapper pack (Drive 1TcpA7RXoTUEr3pHZ8-4YTcQFRGZYP7v_), RAR FTP originals "(Windows)" (USER corrected my wrong "(all platforms)" claim: archive is Windows-only).
- Dual gate: a11y-rarlinks APPROVE (corrected label confirmed, "(all platforms)" withdrawn as premised on wrong fact); peer-scratch-review APPROVE (codex quota-exhausted substitute) - re-ran all suites itself (lib 1430 / App.Core 689 / Manager 199) + forced rebuild 0W/0E on real tree; verified diff blobs match tree.
- Peer non-blocking notes carried: (a) resolution-failure messages (RARVersionSelector:170, ReconstructorViewModel:1434) name only {FileName}, never run-rar - copy nit, follow-up; (b) production delegation boolean at RarExecutable.cs:19 is the one uncovered line (seam tests drive the internal arm); (c) macOS stray-wrapper asymmetry accepted (hypothetical, loud on failure).
- Codex catch-up queue now SIX: exit-code classification, runnable Copy Full Command Line, quoting doc clause, keep-work-files setting, CAV process-log fix, run-rar wrapper + links. Quota returns 2026-07-29 ~07:05.

## 2026-07-26: wizard download links + step-1 field names (app-only 3af0b48)
- ReconstructWizardBody step 1: same three pack links as Advanced header under the WinRAR-folder caption; lead-in "Download WinRAR versions from:" (a11y-wizlinks finding 1 - explicit antecedent). Fold-in (its advisory 3): LabeledBy on the four path TextBoxes + distinct Browse-button names (2.4.6/2.5.3). Handler mirrored in code-behind (sixth mirrored member; extraction, if ever, should take all six - peer).
- Drift protection (advisory 2): shared ResourceLinkExpectations pins (label,url) pairs; twin tests both surfaces (wizard Step1_ShowsWinRarPackDownloadLinks / Advanced Header_ShowsWinRarPackDownloadLinks). Both-must-move-together semantics peer-verified non-vacuous (hidden step realizes ZERO descendants - probe-proven; CurrentStepIndex=1 is load-bearing in both new tests).
- Dual gate: a11y-wizlinks APPROVE (pre-impl, all advisories adopted); peer-scratch-review APPROVE (probe-based verification of realization/order/LabeledBy timing; suites re-run by peer: Manager 202, App.Core 689, lib 1430, rebuild 0W/0E). Cosmetic step1-scoping nit adopted post-verdict, suite re-run green 202.
- NEW SWEEP ITEM 7 (peer finding): Advanced ReconstructorView has the SAME field-labeling gap (4 Browse buttons unnamed at 120/135/150/165, 4 TextBoxes no LabeledBy at 124/139/154/169) but combined Run-based header+caption TextBlocks make LabeledBy announce the whole sentence - needs its own small design (named TextBlock or AutomationProperties.Name), NOT a copy-paste of the wizard fix.
- Peer residual noted: nothing pins handler BEHAVIOR across the two mirrored code-behinds (only labels/URLs pinned) - accepted for 14 lines of glue.
- Codex catch-up queue now SEVEN (adds wizard-links). Quota returns 2026-07-29 ~07:05.

## 2026-07-26: tabbed Settings window + third link surface (app-only edcf140)
- SettingsWindow rewritten: 700x520 (Min 560x360, fixed height - no resize on tab switch), Grid *,Auto with TabControl BEFORE footer (fixes pre-existing 2.4.3: Cancel was the first Tab stop). Four plain-string tabs verbatim from the old section names; per-tab ScrollViewer Padding=4; content moved VERBATIM (peer mechanically diffed: all 12 bindings identical incl TwoWay, HelpText byte-identical, every margin accounted for).
- Links: RAR Reconstruction tab under the WinRAR picker = THIRD surface; ResourceLinkExpectations doc names all three; new RarTab links test pins the triplet + order.
- NEW ReScene.Manager/Views/ResourceLink.cs: single OpenFromTag behavior; all three code-behinds delegate (closes peer's wizard-review residual: handler behavior now single-sourced; surfaces can only drift in markup, which the shared list pins).
- Tests: SelectTab helper (unselected tabs materialize nothing); tab walks in Renders test (peer probe: leaving a tab detaches deterministically, counts 0/1/1/1, never accumulates); Tabs_FourSectionHeaders pin + SelectedIndex 0; MinWidth 560; TemplatedParent-null filter on the app-name First() (peer nit: NumericUpDown template contains its own TextBox).
- Dual gate: a11y-settings APPROVE twice (plan with 7 advisories all adopted; post-impl pass verified links char-for-char, verbatim move, handler consolidation behavior-identical). peer-scratch-review APPROVE.
- Residuals recorded: (1) RAR tab fits 408/416 at default height - ~8px slack; anything added there tips it into (graceful Auto) scrolling; (2) SettingsViewModel.Save STILL has zero tests (pre-existing) - never-visited-tab round-trip rests on reasoning (ctor loads all fields, Save reads VM not view) until Save coverage lands; (3) manual smoke queue: first Tab stop = tab strip, min-size + 150% scaling, Ctrl+Tab check, optional NVDA pass.
- Codex catch-up queue now EIGHT (adds settings-tabs). Quota returns 2026-07-29 ~07:05.

## 2026-07-26: scrollbar overlay fix (app-only a7b677b, user-reported w/ Ubuntu screenshot)
- Defect: Fluent overlay scrollbar (AllowAutoHide=true default; persistently expanded on Linux) drew ON TOP of right-edge controls - reported on the Reconstruct wizard step-1 Browse buttons ("Browse" clipped). Framing per a11y: 2.4.11-adjacent visual occlusion, NOT an AA failure (partial occlusion passes 2.4.11; keyboard/names never harmed).
- Fix: ScrollViewer.AllowAutoHide=False (probe: reserves exactly 16px, ONLY while the bar shows) on: wizard step-1 form, Settings x4 tabs, Reconstructor x6 tabs, Home recent-files, detected-sets mini-lists x2, CreateSRR sample/subtitle ListBoxes, and the shared logList STYLE setter `(ScrollViewer.AllowAutoHide)` covering all 12 log panes (Linux horizontal bar sat on the newest log line). Precedent: BruteForceProgressWindow version grid.
- Excluded (recorded for audit reproducibility): BeginnerShellView:33 (centered MaxWidth 760; sub-776px caveat = bar over card padding only), HexView:18, InspectorView:175/358, File/ImagePreview panes.
- Tests: pin the PAIR Auto+AllowAutoHide=false per surface (a future flip to Visible would reserve permanently); TemplatedParent-null filter for declared scrollers. Manager 206 (204+2), rebuild 0W/0E.
- Dual gate: a11y-scrollbars APPROVE (8 advisories; mini-lists+logLists included at its call; WCAG citation corrected). peer APPROVE - probes: style setter reaches the ListBox's inner templated ScrollViewer (aah=False, viewport delta 16px on flip - parenthesized syntax NOT silently no-oping); replace_all counts exact (4/4, 6/6, file-total ScrollViewer counts match); Settings RAR tab re-measured IDENTICAL 408/416 (stable equilibrium, no new bar at default size); logList perf negligible (inheritance walk already paid by Foreground/FontFamily).
- Peer declined my stronger-assert suggestion (inner-ScrollViewer assert would couple to Fluent template internals; propagation is framework behavior) - agreed. Optional hardening noted (one viewport-delta assert vs framework regression) - not taken.
- LOOP-CLOSER (peer): all verification is Windows headless; the defect is Linux-specific. One Ubuntu screenshot of the wizard step confirms visually. Queued in user smoke + 1.4.11 dark-theme thumb contrast eyeball.
- Codex catch-up queue now NINE (adds scrollbar-overlay). Quota returns 2026-07-29 ~07:05.

## 2026-07-26: picker start-location app-wide (app-only 747ec3c, user-approved scope)
- Linux Browse always opened $HOME (no SuggestedStartLocation ever passed; Windows Win32 MRU masked it). Fix: IFileDialogService open-methods gain optional initialPath; AvaloniaFileDialogService.ResolveStartDirectory (dir->itself, else surviving parent, else null; NEVER throws - peer stress-probed 30 hostile inputs, failures=0) -> TryGetFolderFromPathAsync; SaveFileAsync shares the helper. WCAG framing: 3.3.7 Redundant Entry adjacency (a11y). Windows delta documented: populated field now beats dialog MRU.
- All 31 sites forward anchors (table in review thread): Creator x5/BeginnerRestore->InputPath; SRSCreator main-file ?:InputPath (movie beside sample - the user-cited wizard); SRSRecon media ?:SRSFilePath; SampleRestorer media ?:SRRFilePath; Compare left/right ?:sibling; Reconstructor verification ?:ReleasePath + GUARD: never anchors into the SRR import's scratch SFV dir (_sfvTempDir prefix predicate reused; peer verified polarity against the cleanup site - same predicate, opposite action, both right); ImportSRR->ReleasePath; ImportConfig deliberately null (commented). a11y ruling: directory-only, NO SuggestedFileName (XDG portal can't preselect on OpenFile; stale name = hazard).
- REVISE round: my Write emitted a RAW NUL byte in AvaloniaFileDialogServiceTests.cs -> file binary to git -> INVISIBLE in the peer's diff (caught by the peer via byte-count/od; the \t in the same literal was properly escaped). Fixed bytewise (escape now in source; runtime string identical). Lesson recorded: tool-emitted control bytes make sources diff-proof; peer checks od/file on new files.
- Tests: +8 resolver cases (Manager 214) incl garbage/40k never-throw; +3 VM forwarding (App.Core 692: folder-field, file-field, empty-field fallback). QUEUED (peer, non-blocking): temp-dir-arm test (~15 lines on ArchiveSetTests FixtureDialogService pattern via SfvTempDirForTest); SettingsViewModel.Save coverage still absent (standing).
- Dual gate: a11y-pickers APPROVE (6 advisories adopted). peer REVISE->APPROVE (all four fixes independently re-verified; suites re-run by peer match: 692/214/0W0E).
- LOOP-CLOSER: XDG portal honoring SuggestedStartLocation unverifiable on Windows headless - one Ubuntu Browse re-click closes the report (with the wizard scrollbar screenshot).
- Codex catch-up queue now TEN (adds picker-start-location). Quota returns 2026-07-29 ~07:05.

## 2026-07-26: v2.0.0 RELEASED (user go-ahead "NOW it looks releaseable")
- Sequence executed: lib main pushed aa0fdd9->f9d0101 (NeWbY100/ReScene.Lib); app main pushed ae0d655->ac54e51; tag v2.0.0 @ac54e51 pushed. Credential gotcha: GCM offered prijkes for ReScene.Manager.git (403) - resolved with one-shot `-c credential.helper=!gh auth git-credential` (NeWbY100 active), no config changed.
- Release run 30197080202: ALL GREEN (4 RID builds + release job); assets ReSceneManager-2.0.0-{win-x64.zip,linux-x64/osx-x64/osx-arm64.tar.gz} published: https://github.com/NeWbY100/ReScene.Manager/releases/tag/v2.0.0. Non-fatal annotations: IDE0055 formatting x2 in lib SRRFileParser.cs:505-506 (non-Windows analyzers; follow-up nit) + GH Node20 deprecation notice.
- Codex catch-up (10 changes) NOT run before push - user's informed release call = waiver (queue told to user repeatedly incl in the immediately preceding message); catch-up remains queued as post-release review.
- Build (main) run 30197077459 FAILED: 6 SFX tests - TestData/best_little/best_little_sfxgui.exe exists ONLY locally; .gitignore:23 *.exe swallowed it since the pyrescene import (local pass / fresh-checkout fail). FIX READY uncommitted->committed LOCALLY: lib 137c74f (git add -f + tracked-vs-disk TestData audit: this was the ONLY missing fixture; __pycache__ litter correctly untracked) + outer 37b5aa9; lib suite 1430 re-verified. Self-verified (fixture tracking, no code) - queued for codex catch-up; NOT pushed, awaiting user go-ahead.

## 2026-07-26: cross-platform CI batch (lib c74f41e + bump)
- First-ever non-Windows lib-suite CI run (3-OS build.yml shipped with the port; branch never pushed pre-release) surfaced 12 ubuntu / 14 macos failures, 4 root families:
  1. PRODUCT GetArchiveSetKey: format paths through OS Path APIs - "DVD1\x.rar" never split on Unix. Fix: explicit both-separator split. Peer differential probe (old impl inlined, 18 edge inputs): Windows-identical incl the odd "DVD1/"->"DVD1/" empty-basename case.
  2. PRODUCT GetFinalPath POSIX: existence precheck on the RAW input (lexically collapsed by .NET) rejected symlink-before-dotdot paths - the resolver's own documented hazard. Check moved onto the RESOLVED result. Peer: BIDIRECTIONAL change - also now THROWS where pre-fix returned a silently-wrong nonexistent resolved path (strengthening, recorded).
  3. FIXTURES: no .gitattributes; text-looking fixtures stored LF-normalized (Windows checkout re-CRLFs = green; Unix keeps LF = byte tests red by newline count). NEW .gitattributes TestData/** -text + renormalize: 21 fixtures (peer count-corrected from 22; commit = 21 fixtures + attrs + 3 test + 2 product files), md5-proven CR-only deltas (peer, NUL-delimited incl the Greek-named file). Note: ansi_unix.nfo was ALREADY byte-identical to ansi_dos.nfo pre-batch (unused corpus pair) - -text now locks CRLF into a file named "unix"; future line-ending tests must rebuild the pair.
  4. TESTS: VersionDir stub was "rar.exe" (invalid on Unix; now RarExecutable.FileName); MoveMatchedFile separator no-op Windows-gated (POSIX: '\' is a name char - the spellings ARE different paths); Load_StoreSplitFolder expects Path.Combine (loader normalizes to OS separator - traced: ArchivedFiles feeds NO writer, so no round-trip risk); CaseDistinctSiblings runtime FS-case probe (leak-free, peer-verified); LongPath macOS gate (PATH_MAX 1024; growth path is the WINDOWS GetFinalPathNameByHandleW loop anyway - peer).
- Reproduction rig: docker dotnet/sdk:10.0 + git-bundle clone = CI checkout semantics exactly (pre-fix run reproduced CI's 12 precisely; post-fix 1430/1430). Windows 1430/1430.
- Peer APPROVE (all six angles; macOS pair reasoned-only = skip-guards that fail VISIBLY on CI if wrong). Codex catch-up queue grows.

## 2026-07-26: sweep item 1 - Save-log announcements (app-only b43c5ed)
- All SEVEN surfaces: visible SaveLogStatus TextBlock (LiveSetting=Polite, always in tree, fill child of log header, TextTrimming) bound to new SaveLogAnnouncement on both SaveLog implementations. Outcomes: Saved(filename) / Failed(exMessage, prefix-only difference - doc now says so, no sanitization implied) / Empty ("Nothing to save yet - the log is empty", em-dash confirmed by a11y as product-correct); cancel stays blank (own feedback + clears stale text). Clear-FIRST at both sites defeats CommunityToolkit+Avalonia equal-value suppression so repeat saves re-announce (do-not-simplify comments).
- Base OperationViewModelBase gains the UI-thread snapshot-before-export (verbatim Reconstructor port) - live-collection race closed for the 5 derived VMs.
- Tests: SaveLogAnnouncementTests 11 (5 per impl + Base_RepeatEmptyPress pinning the SYNCHRONOUS clear/set pair - peer probe: first press 1 transition, second press 2); SaveLogStatusTests: ONE looped AvaloniaFact over all 7 surfaces (BeginnerShellTestFactory; wizards realize steps 4/2; Polite+in-tree+empty+delivery+sink-clean). App.Core 703, Manager 215, gate 0/0.
- Dual gate: a11y-savelog APPROVE twice (design rulings A/B/C + post-impl; its parity correction adopted: 2 Reconstructor mirrors). peer APPROVE (empty-path clear measured non-vacuous; Single() safe incl Reconstructor tabs; no new disclosure in Failed - same payload as log line, filename/fullpath asymmetry doc'd; DockPanel squeeze measured 320px button intact 80px; shared-VM reset load-bearing - removal FAILS the test).
- Peer count corrections adopted into this entry (21 fixtures xplat; 702->703 here after the extra empty-repeat test).
- FOLLOW-UPS live: coalescing of same-frame clear/set into one announcement = acceptable-by-design worst case; sweep item NEW: CreateSRS/Restore wizard save-parity check (a11y advisory 7).
- Codex catch-up queue now TWELVE (xplat batch, IDE0055, savelog + prior nine). Quota 2026-07-29 ~07:05.

## 2026-07-26: xplat round 2 (app 0a8e626 + lib 94eaaee bump 35d08f1)
- Round-1 push unblocked App.Core.Tests' FIRST-EVER Linux run: 26 failures, FOUR families (implementer agent xplat-appcore; peer-verified adversarially): A) SIX duplicated junction helpers (one called cmd.exe unconditionally) -> new TestDirLink.cs, platform-branched, target-precreating (peer: behavior-preserving at all six ex-sites); B) Windows path literals -> OS-built fixtures (FakePath helper; ONE inherently-Windows gate: different-drive; peer spot-checks: collision test still collides, remaining literals deliberate opaque handles); C) FileCompare lock test: Windows-gated the delete-expecting-sharing-violation PRECONDITION (POSIX: no mandatory locks - probe asserted falsehood AND unlinked a needed file; Linux arm degrades to VM-state-only, recorded); D) six tests stubbed literal "rar.exe" -> RarExecutable.FileName.
- PRODUCT BUG x3 (platform-independent, user-reachable): GetDirectoryName returns "" not null for a bare typed filename -> `?? "."` never fired: ComputeStoredName (ArgumentException from GetRelativePath - implementer, pinned) + BuildSampleAndSubtitlePlaceholders :607 + create path :823 (scanners throw on "" - PEER measured; fixed same pass per its prescription, pinned by BuildSampleAndSubtitlePlaceholders_InputPathIsBareFileName_DoesNotThrow). Safe siblings :388/:990 left alone. Peer edge note: bare input + stored file under CWD -> CWD-relative stored name (coherent; outside-CWD tested).
- LIB 94eaaee: ApplyComponent resolves adopted link-target's OWN ancestor chain (macOS /var->/private/var), depth-40 guard (catches mutually-referencing target STRINGS - BCL only catches pure symlink cycles). PEER REVISE round: the POSIX regression test had NEVER EXECUTED (early-return reports as PASSED - "1431 green" CONCEALED the gap; 1430->1431 for a never-run body). Closed: container run at 94eaaee = Linux 1431/1431, and the test FAILS without the fix (peer-analyzed). xunit v2 = no Assert.Skip; early-return concealment recorded as accepted limitation, container runs = compensating control. LEDGER ACCURACY (peer): ToExtendedLengthPath shares the walk -> junction-through-junction on Windows now more-resolved = a Windows behavior change (functionally equivalent for opening), NOT zero-change.
- Verification: container App.Core 26->0 (only the 5 container-environmental remain: root user x4 + missing imaging deps x1 - all pass on CI runner); Windows App.Core 705/705; lib Win+Linux 1431/1431.
- Savelog test-parity note (prior review): Reconstructor empty-path repeat assertion still unmirrored (base-only) - micro-item, open.
- Codex catch-up queue grows to THIRTEEN-ish (xplat r2 + product bug batch). Quota 2026-07-29 ~07:05.

## 2026-07-26: xplat round 3 (app 7693541) + sweep item 2 logList Copy (app ddede5c)
- ROUND 3 (the macOS five): ReconstructionPathGuard.ResolveReal had the IDENTICAL adopted-target bug as lib 94eaaee - `target?.FullName ?? candidate` kept a link's stored /var spelling while direct walks resolved it. Same fix + depth-40 guard; IOException REQUIRED not stylistic (peer caller-analysis: every guard catches IOException failing closed; only ReleaseScanner:1114 catches SrrNameException, nowhere near these paths). 3 expectations canonicalized; ResolveOutputChild pin switched to EndsWith (peer: no shared primitive with the code under test). NEW alias-shape regression (POSIX, mirrors lib's) - body EXECUTED on Linux container AND red-verified (fails with adoption reverted; revert grep-confirmed - first attempt used python3 absent from the SDK image and silently tested the FIXED code: caught, redone with sed). Peer probes: dangling-junction pastExisting re-derived not inherited (one caught exception, correct); junction-chain recursion exercised on Windows too; fail-closed suite (9 tests) intact = the over-loosening control; "loosens toward correctness" framing verified (false-REJECT direction; greater accuracy cannot manufacture a false accept). Windows 706/706.
- SWEEP 2 (logcopy-impl, two rounds): LogListCopy attached behavior via logList STYLE setter - ELEVEN panes (peer count-corrected from twelve; doc fixed). Menu (Copy Line + platform chord InputGesture / Copy All Lines), HotkeyConfiguration.Copy matching, open-time disabled-when-inapplicable (a11y: menu items differ from buttons; BruteForce menu's always-enabled-noop guards flagged for later harmonization), snapshot Copy All + empty-snapshot guard (SetTextAsync("")==ClearAsync), FOCUSED-LINE fallback (a11y binding ruling: SelectedItem wins; focus-only when null; non-mutating; capture-at-Opening/reuse-at-Opened/clear-at-Closed; ownership check; chord live via FocusManager). 19 tests incl real-activation ordering guard (peer-measured order=[Click -> Closed] then adopted into the test: Apps/ArrowDown/Enter - a reversed order would silently copy wrong data), cross-pane rejection in ONE window (shared focus scope), Escape+focus-return with vacuity pin, right-press-native decompile-backed + guarded. Peer: CWT state leak-free, _menus.Add THROWS on double-attach (loud invariant), keydown steals nothing, snapshot no mutation window, style setter proven end-to-end, no ContextMenu clobber anywhere (all existing menus are DataGrid/TreeView). Manager 234/234 (215+19), gate 0W/0E.
- Carried notes: SkippableFact (xunit v2 pkg) = the known escape hatch for early-return skip concealment (peer round-2 correction); savelog Reconstructor empty-repeat mirror still open (micro); NVDA smoke for copy = 4 announcement checks.
- Dual gates: a11y-logcopy APPROVE x2; peer APPROVE x2 (round 3 + sweep 2). Codex catch-up queue now ~FIFTEEN. Quota 2026-07-29 ~07:05.

## 2026-07-26: GREEN BOARD - first fully cross-platform green CI run (run 30200683978)
- windows + ubuntu + macos ALL GREEN at 1a08031 (the 4th-instance expectation fix). Round-3 diagnosis confirmed on macOS: 4 of 5 repaired by the resolver fix; the 5th was the fix EXPOSING a raw expectation that passed only while both sides shared the raw /var spelling.
- Peer post-verdict sweep (its own initiative after my file-scoped check): all six canonicalizing-API test files - CLEAN; nothing remains. LEDGER RULES (peer): (1) the exposure is STRING COMPARISON ONLY - File/Directory.Exists(rawPath) is immune (the filesystem resolves aliases; /var and /private/var name the same inode); (2) the trigger generalizes: ANY change making output MORE normalized (canonicalization/trimming/case folding/ordering) can break tests that passed on shared un-normalized forms - ask "which passing tests compare this output to a LITERAL?" by grep BEFORE the push; (3) newly-DIVERGING passes are part of the sweep set, not just newly-failing tests.
- Codex catch-up queue final for Monday: ~15 changes. User smoke list: NVDA x4 (copy menu), Settings keyboard checks, Ubuntu visual pair (scrollbar step-2, picker re-Browse).

## 2026-07-26: Versions tree WPF-classic restore (app-only, user feedback)
- Diagnosis via `git show v1.9.0:...ReconstructorView.xaml`: old/new markup IDENTICAL; delta = Expander theme chrome only (both row-major - the "column flow" was window-width illusion). Fix: scoped Style Template `Expander.versionGroup` (the app's only Expander): left chevron Path (AT-invisible by class), transparent stock ToggleButton header (property-restyled ONLY - focus ring survives, peer measured byte-identical focus signals vs stock incl :focus-visible), IsChecked TemplateBinding Mode=TwoWay (CRITICAL - default OneWay silently kills expansion; pin test revert-proven: Expected True/Actual False on one-way), PART_ContentPresenter (the only registration-sensitive name; Expander has NO part contract in 11.3.18 - verified from assembly), IsVisible-gated content (collapsed leaves leave tab order), 5 accent-state overrides (Fluent :checked = blue bar otherwise), MinHeight 24 (2.5.8 floor - sole density deviation).
- A11y F8 fold-in: ENTIRE tab previously read as unnamed checkboxes; explicit names on toggle/select-all/leaves (RARVersionLeaf.AccessibleName). F8's "confirm with Insights first" deviated from: names additive + visible-text composed - peer concurred runtime confirmation would add nothing.
- Peer notes: Style-Template legitimate (ControlTheme = alternative if variants ever needed); header CheckBox nested in toggle = two named header-row controls, correct + pre-existing (Fluent nests the same); gate earned its keep twice (AVLN-silence proves the CompileBindings scoping; xUnit2031 catch).
- USER VISUAL CONFIRMATION PENDING: headless proves structure not pixels - glance at Versions tab vs old screenshot.
- Dual gates: a11y-verstree APPROVE, peer APPROVE. Manager 235, App.Core 706, gate 0/0. Codex queue ~16.

## 2026-07-26: chevron origin fix (c7123b1) + frame-capture rig established
- User-reported: expanded chevron displaced to a far-left sliver. Cause: RenderTransformOrigin="0.5,0.5" = CORRECT WPF (relative 0-1 Point) but ABSOLUTE half-pixel in Avalonia (RelativePoint needs "50%,50%"); rotation swung the glyph around its corner. Fix: attribute removed (default IS Center); test pins RelativePoint.Center (would have caught the original - genuine guard, peer-verified).
- PEER ADMISSION recorded as ledger rule: "in ported markup, a value that LOOKS right is the most dangerous kind - looks-right means right-in-the-framework-we-came-from"; unit skepticism must extend to bare numeric literals in WPF->Avalonia work.
- RelativePoint bug CLASS swept closed (peer): only RelativePoint-typed XAML props are RenderTransformOrigin + gradient Start/End/Center/GradientOrigin; grep across all .axaml = zero remaining hits, no gradient brushes in the app.
- FRAME-CAPTURE RIG (Avalonia.Headless + UseSkia + CaptureRenderedFrame renders REAL pixels): diagnosis + fix-verification for the chevron; peer retires its own asserted limitation ("headless cannot reach appearance" - wrong, twice). NEW LEDGER RULE: property asserts prove WIRING, rendered frames prove APPEARANCE; "headless" no longer excuses unverified visuals. Prior launch-smoke residuals now closable by rig: Settings RAR-tab fit, scrollbar overlay, dark-theme thumb contrast.
- F4 (2.4.7 focus ring on the compact toggle) CLOSED BY PIXELS per peer recommendation: focused-vs-unfocused frame diff = 669 changed px on ours (double white ring around the full header, visually confirmed in the capture) vs 708 on a stock ToggleButton. The one inference-based item from the restyle review is now evidence-based.

## 2026-07-26: exact-v1.9 Versions chrome (94d9446)
- Real spec found: v1.9.0's App.xaml 515-585 = CUSTOM Expander template (my screenshot-based "plain" reading was wrong - compression hid the chrome): bordered rounded group, Surface header bar 8,6 + hover, MDL2 E76C/E70D chevron (Secondary, 10px), Panel body. Ported faithfully; frame-rig side-by-side matches BOTH states incl the under-header seam (old had it too - verified before "fixing").
- Deviations (invisible, a11y-mandated): header FOCUSABLE (v1.9 Focusable=False = keyboard-unreachable groups, a shipped defect); stroked Path not the Windows-only MDL2 font.
- A11y amendment APPROVE: contrast recomputed for new fills (chevron 5.1/4.0, text 9.2/7.2/10.3/5.7 - all pass); F4 fallback corrected (BorderFocused FAILS 3:1 on Hover - use ForegroundPrimary if ever needed); UIA/NVDA runtime gate still queued.
- Peer APPROVE (all measured): single-paint layering via TemplateBinding (rest=button Background, checked=override - COUPLED, now commented); per-corner radius TemplateBound (3,3,0,0 mirrors MediumRadius=3 - coupled, commented); collapsed non-realization = consistent Avalonia semantics (invisible Border never measured, presenter never builds - checkBoxes 1 vs 2); Stretch vs theme MinWidth=96 floor no-conflict; PART_ContentPresenter name reused 3-4x by Fluent (select by content, never by that name).
- RIG RULES (peer, standing): (1) NEVER capture without ForceRenderTimerTick+RunJobs - stale frames manufacture confidence (the chevron-era captures worked by timing luck = could have false-passed); (2) prefer captures that VISIBLY DIFFER when the change is absent. Same family as sed-vs-python3.
- Verification honesty: gate 0W/0E predates the user's app launch; tree since locked (VS 28020 + app 88560); suite re-runs redirected (bin3). Codex queue ~17.

## 2026-07-26: versionGroup density exact-parity (amendment 2)
- User: "everything a font size bigger". Measured: glyphs ~same; ROW PITCH 18 old vs 22 new; 8 cols old vs 7 new. THREE causes fixed, all probe-verified: WPF TabItem FontSize=12 INHERITED into tab content (port: headers only) -> scoped 12; Fluent checkbox = THREE stacked 20px primitives (MinHeight + box + Viewbox + unnamed wrapper Grid hardcoded height - each alone still floored at 20; the leafMinH=16-while-H=20 probe = the vacuous-fix trap caught) -> scoped trio, realized 16; zero-slack wrap (8x230 == available) + reserved scrollbar + border -> leaf 226 (longest label 165px, 61px slack peer-measured).
- APP-WIDE OPEN DECISION for the user: ALL old tab content was 12px (inheritance); the port renders 14 everywhere. Only the Versions tree changed here.
- SUITE NOW GUARDS DENSITY (peer: "every visual regression on this tab was caught by the user, not the suite"): realized height <= 18 w/ template-regression message; height <= 16 + Thickness(0,1) margin = 18 pitch arithmetically; D1a basis documented in-suite.
- 2.5.8 DEVIATION RECORD (a11y B5 verbatim + peer precision): Versions tab Expander.versionGroup subtree; leaf AND header select-all checkboxes ~16px height / 18px pitch; below 24 minimum AND the spacing exception FAILS TOO (18px centers < 24 required); no exception applies (preference not essential); mitigations: ~226px-wide targets (only vertical axis sub-minimum), full keyboard operability (Tab+Space - LOAD-BEARING invariant: breaking leaf keyboard access VOIDS the deviation), desktop mouse+keyboard, OS scaling; header expansion ToggleButton keeps MinHeight>=24 (H=28 measured); invariants: scoped-only, no leakage. Severity: Major, accepted-and-documented. Revisit on missed-click/precision feedback.
- NVDA SMOKE REOPEN TRIGGER (a11y sign-off condition): unnamed toggle / ExpandCollapse not flipping / stray chevron node in UIA => reopens F3/ruling-A as CRITICAL, fix precedes next release.
- A11y: full code-side sign-off (F4 on new chrome: 2400px focus-ring delta, ring traces the full-width bar). Peer APPROVE (contrast 5.72:1 computed independently - large-text framing runs OPPOSITE: shrinking text can never raise the bar above 4.5; Viewbox scales at 87.5%, no clipping). Codex queue ~18.

## 2026-07-26: App-wide 12px content text (v1.9 WPF parity) — 125d111
User decision ("Let's use 12px") after old/new full-tab screenshots showed Avalonia one size bigger.
Design rationale (a11y ruling B): 12px is the v1.9 WPF-parity size, not a new design choice — WPF's
default FontSize IS 12, so v1.9 rendered 12 everywhere (its TabItem style merely re-asserted the
default); enlargement path is OS display scaling, honored per-monitor by Avalonia. No 2.5.8/1.4.4
deviation record needed for 14->12 itself.
Mechanics: ControlContentThemeFontSize 14->12 (themed controls incl. popup-hosted — menu probe=12)
+ Window FontSize=12 style (unstyled TextBlocks); headings keep explicit resources. INVERTED TOKEN
SEMANTICS restored and commented: FontSizeCaption(13) > inherited content(12); FontSizeBody(14) is
NOT the de facto body size — do not "normalize" (drifts back to 14).
A11y fold-ins (gate F3/F4): ForegroundSecondary #9E9E9E->#AAAAAA (3.97:1 on HoverBackground = 1.4.3
fail on Beginner hub cards; now 4.58:1 — THIN margin, darkening HoverBackground goes under, value
recorded in Tokens.axaml comment) + last 3 MediumLowBrush consumers (~3.7:1) moved onto it
(HomeView x2, Button.statusVersion). Peer swept: shim now has ZERO consumers repo-wide = failure
class closed exhaustively; shim kept-but-dead with do-not-repoint comment. Tab-strip grey stays
#9E9E9E (~5:1 passes) — now a SECOND secondary grey, commented against "tidying" into alignment.
2.5.8 spot-checks post-shrink: Browse 29 / TextBox 29 / Save 26 — all >=24; versionGroup rows stay
under the existing recorded deviation.
Tests: 3 literals track the token (FieldStatusLine/Converter pin ForegroundSecondary, StylesTests
pins statusVersion now on it) — literal-vs-token coupling is DELIBERATE drift detection, expect
breakage on every palette change (intended cost). DensityStyleTests' 9E9E9E pin = different brush,
correctly untouched. Suites Manager 235/235 + App.Core 706/706 (peer re-ran both), rebuild 0W/0E.
Dual-gate: a11y-fontsize APPROVE (F1-F5, rulings A-D), peer-scratch-review APPROVE.

## 2026-07-26: Root-pin fix — the 12px change SHIPPED MASKED (426000f)
USER CAUGHT IT post-relaunch: path TextBoxes/CheckBoxes still 14 (path text ~16% wider = 14/12).
Root cause, two layers + one discovery:
(1) MainWindow root FontSize={FontSizeBody}(14) LOCAL VALUE — out-prioritizes every style; port-era,
NOT parity (v1.9 MainWindow.xaml had no root FontSize). (2) "Window" selector DEAD — Avalonia
selectors match concrete style keys; every app window is a subclass; must be :is(Window).
(3) Red-verification discovery: Avalonia DEFAULT FontSize IS 12 — Fluent never sets 14 on windows;
the 14 came ENTIRELY from root pins; the :is(Window) style is ENFORCEMENT not supply (comments
reworded). NOTE: default-is-12 is SINGLE-verified (my measurement; peer ran the root-pin red, not
the dead-selector red — recorded per peer's (d)).
FALSE-PASS LESSON #3 (ledger rule hardened): first red (revert to dead "Window" selector) STILL
PASSED — reverting to something equally dead is not a red. A red-verification is only evidence if
the failure message NAMES the thing you broke (here: "MainWindow: FontSize 14" + the TextBox
assert line). Prior instances: python3-absent sed rig, stale CaptureRenderedFrame. Verification
harnesses producing false passes is a PROPERTY of verification harnesses, not bad luck.
WHY BOTH GATES MISSED IT ORIGINALLY (recorded verbatim-ish from peer): all four original probes
(button/radio/menu/menuText) travel ONE acquisition path — theme-resource — while the change
depended on window INHERITANCE (TextBox/CheckBox), unprobed. Rule: when a change exists to produce
an observable outcome, the FIRST check is that outcome measured on the surface the user complained
about; probe sets must span acquisition paths (inheritance / theme-resource / explicit token).
v1.9 PARITY MAP (evidence-based, replaces blanket-12 assumption): v1.9 was DELIBERATELY MIXED —
FontSizeBody=14 pins on Inspector path/warning/tree, Compare boxes x2 + trees x2, Home recent NAME,
PromptWindow ROOT+input, CustomPackerWarning; everything else inherited default 12. Port pins were
FAITHFUL; only the 3 root pins were bugs (MainWindow/MessageDialog/WizardWindow removed; PromptDialog
kept = the one sanctioned pin, cited in an exception dictionary in the test).
Change: 3 root pins removed (each commented with the local-value hazard), :is(Window) + honest
comment, R1 wizard-warning pin commented (mirrors v1.9 ReconstructorView.xaml:74), NEW
WindowFontSizeParityTests (reflection over ALL Window subclasses, >=12 count sanity, size map
{PromptDialog:14, rest:12}, fail-loudly on parameterless-ctor-less windows; MainWindow probes span
all 3 acquisition paths + Inspector TreeView==14 element-pin guard [R2] + BruteForce DataGrid==12
[A1]). Peer coverage-model note: tests pin OUTCOME not mechanism — only uncovered combo is "style
deleted while default still 12" which is harmless until it matters, then covered.
Verification: red-green both directions (peer INDEPENDENTLY re-ran: byte-exact md5 restore,
237/237), full Manager suite 237/237, rebuild 0W/0E (CA1310 fixed: StartsWith Ordinal). R3 Compare
frame: pinned 14 boxes over 12 chrome render as designed; GAP RECORDED: DataGrid ROW height >=24
(2.5.8) NOT pixel-checked — needs data plumbing; covered by user's Compare smoke.
Dual-gate: a11y-fontsize APPROVE x2 (R1-R3 folded), peer-scratch-review APPROVE (with recorded
process-miss admission: answered the angles handed to it instead of asking "does this do the thing
it claims").

## 2026-07-26 addendum: peer post-approval correction (counts rule)
WindowFontSizeParityTests = TWO test methods (3 assertion legs inside the MainWindow one), not the
"3/3" I told the peer (that filtered run included the throwaway Compare grab test). Committed ledger
was already correct (237 = 235 + 2). RULE (peer, 4th count discrepancy this stream: 700/702-vs-703,
twelve-vs-eleven log panes, 22-vs-21 fixtures, 3-vs-2 parity tests): counts in dispatch messages are
APPROXIMATE UNTIL MEASURED — measure before a number enters the ledger; a ledger that inherits an
unmeasured count will eventually mislead someone who cannot re-measure.

## 2026-07-27: Press-scale suppression — Versions header slide (8b0f01c)
USER REPORT #3 in the Versions neighborhood, NEW mechanism (distinct from chevron-origin): group
header "still moving to the right — hard to click". ROOT CAUSE: Fluent Button/ToggleButton themes
apply RenderTransform scale(0.98) on :pressed, TRANSITION-ANIMATED; width-proportional slide (1%
per edge) → full-row header (~1846 DIP live) slid chevron/checkbox up to ~18px DURING the press;
226px leaves <2px + CheckBox theme has NO press transform (probed null) → "only groups misbehave".
A11y gate sharpening (upgraded severity to Major): RenderTransform moves HIT bounds + IsPressed
re-evaluates containment on pointer-move → once slid out from under a parked pointer, ANY movement
cancels activation — peer proved even a ZERO-DISPLACEMENT move cancels (clicked=False under bug) =
intermittent activation failure, the literal "hard to click" mechanism.
FALSE-ACQUITTAL LESSON #4 (animated-property sibling of stale-frame): forcing :pressed via
IPseudoClasses and reading RenderTransform immediately returns IDENTITY — the transition means t=0
reads the OLD value; the correct hypothesis was wrongly killed once. Sample DURING a held real
press across ForceRenderTimerTick loop. PEER'S ADDITION (lesson #5): when probing styled properties
on the LIVE app, read the property's SOURCE field, not just value — peer's own first live probe
false-passed via a local-value contamination it caught by source=StyleTrigger check.
FIX (a11y option A app-wide, unconditional): Button/ToggleButton/RepeatButton :pressed →
RenderTransform none. RepeatButton = peer-measured latent hole (own style key; 18px slide with fix
present; unreachable today — no RepeatButton in any axaml, ScrollBar's internal ones have dedicated
ControlTheme, probed 0). Rejected alt: :is(Button):pressed (covers derived types incl. 3rd-party
templates — broader than needed, on record per peer). SplitButton/DropDownButton remain named gaps
if ever introduced. none vs x:Null: none = identity TransformOperations, type-compatible with the
theme's transition (identity→identity = never engages; firstFrameDrift=0.000); x:Null would hand
the transition a null.
FEEDBACK PARITY (peer challenged the a11y gate's "all five have :pressed" as overcounted — link/
toolbar/versionGroup have :pressed == :pointerover): verified against v1.9 App.xaml — its FIVE
IsPressed triggers are ALL Button templates (matching our classes with distinct pressed brushes);
Hyperlink/ToolbarToggleButton/Expander-header had NO IsPressed trigger → no-during-hold-change on
those surfaces IS literal v1.9 parity, recorded qualified.
Tests: PressStabilityTests ×2 — held-press drift <0.5px on real Versions header (red 18.42px =
exactly 1% of 1842 test width) + synthetic 1800px recentItem (red magnitude TIMING-DEPENDENT:
mine 10.34, peer 8.75 — do not quote as fixed) + jitter-still-clicks (peer: strongest assert in
the file). Red-green BOTH reviewers independently (peer: byte-identical restore SHA256-confirmed,
live-app repro of both bug and fix, RadioButton/CheckBox 0-drift under bug). Suites 239/239
(237+2), rebuild 0W/0E. GAP recorded: none — live verification done by peer this time.
Dual-gate: a11y-press-scale APPROVE (fresh agent — a11y-fontsize + peer-scratch-review sessions
ended; successors carry the standing rules), peer-press-scale APPROVE.

## 2026-07-27: ReScene.NET rebrand leftovers purged (32521f5, lib c76814a)
User: "still 'ReScene.NET' references around — check, rename/remove". SWEEP SCOPE (state it — peer
rule): source-file globs (cs/axaml/csproj/slnx/md/yml/json) excluding .superpowers/, docs/superpowers/,
obj/bin = 138 pre-change hits; peer measured 119 post-change same scope; ~5.4k on the raw worktree
incl. process docs/.git — a bare grep will still show thousands (ledger+specs are history).
FIXED (functional): (1) THE stamp — released v2.0.0 wrote "ReScene.NET v2.0.0 (…)" into created
SRR/SRS files; GetDefaultAppName → "ReScene Manager v…"; NormalizeAppName matches BOTH new AND
legacy "ReScene.NET v" prefixes (v1.9- and v2.0.0-era stored settings must keep refreshing —
dropping the legacy match freezes them forever; theory test ×2 added; peer red-verified the match
AND verified the outcome LIVE: user's real settings.json carries "ReScene.NET v1.0.0 (5eea776…)"
→ Creator field showed "ReScene Manager v1.0.0 (00c7448…)" — normalize-on-load is in-memory, file
untouched). (2) %TEMP%\ReScene.NET → ReScene.Manager (no cleanup coupled to the root — confirmed;
NOTE: ~209 orphaned old-root dirs on user's machine now permanently stranded, may contain KEPT
work files — user's call to delete, never auto-sweep). (3) AppInfo.DisplayName/"ReScene Manager" +
AppDataConfig.FolderName/"ReScene.Manager" class defaults aligned (WPF head deleted; startup
overrides kept as belt). (4) CreatorViewModel blank-fallbacks ×3. (5) LIB default AppName →
"ReScene.Lib" (byte-length-identical 11 chars — moot per peer: no length-sensitive assert exists,
golden NormalizeDeep rewrites the field both sides). (6) PEER FINDING: ReScene.Cli relied on the
lib default ("app always passes explicit" was FALSE for the CLI) → CreateCommand now stamps
"ReScene.Cli" explicitly (pyrescene convention: tool names itself; CLI is CI-built but not
released). (7) PEER FINDING: .editorconfig headers + LICENSE holders in BOTH repos → "ReScene
Manager"/"ReScene.Lib" (LICENSE = recorded decision: project-name-as-holder pattern preserved,
no personal name per standing rule). (8) Tokens.axaml banner, coding-guidelines header.
KEPT (recorded): ~30 ported-from-WPF XML-doc citations (ReScene.NET.* type names are historical
fact), v1.9 provenance comments, CHANGELOG/README "formerly" + old release URLs (redirect),
FullPipelineGoldenTests explicit vectors (inert — golden normalizes appname), multiset README
fixture-bytes line, NormalizeAppName legacy literal + its test data (back-compat, not leftovers).
LEDGER LESSON #6 (peer): sed -i in Git Bash silently converts CRLF→LF; FormatUtilities.cs is CRLF
while most of the tree is LF — content-diff clean, only the HASH caught it. Hash-verify sed-based
red checks, don't diff-verify.
Suites lib 1431 / App.Core 709 (706+3) / Manager 239, rebuild 0W/0E (CS1503 caught by gate:
CreateAsync's 3rd positional is storedFiles → named options:). Lib pushed 94eaaee→c76814a (ff
checked first). Dual-gate: a11y ACK ("ends the title-vs-stamp split-brand"), peer APPROVE.

## 2026-07-27: Compare name-column black text — always-real-brush at every tier (f43dc63)
USER: "we still are showing text in black (I though we fixed this??)" — user RIGHT both ways:
840fb8f fixed this bug class at the CELL tier; the name column's OWN TextBlock (one tier deeper)
kept the single-key form. FALSE-PASS #5 THEN THE MECHANISM: first regression test PASSED on
unfixed code — fresh binds inherit the fixed cell foreground; the bug needs the grid's per-tree-
click REPOPULATION where a RECYCLED container rebinding indented→plain lands UnsetValue on
TextBlock's BLACK default instead of re-inheriting ("Expected: #ffd4d4d4, Actual: Black").
RULE: recycled-rebind is a distinct bind path — regression tests for binding fixes must exercise
repopulation, not only initial population (the initial asserts are decorative; commented so).
FIX: IndentDiffBrushConverter (IMultiValueConverter, [IsIndented,IsDifferent] → Medium/AccentError/
ForegroundPrimary — v1.9-verified: its row trigger reached non-indented names) + MultiBinding at
both grid sites; fail-safe short-list guard resolves ForegroundPrimary (peer nit: returning
UnsetValue there is the failure mode the class exists to prevent).
PEER REFUTED "TREE IS SAFE" (my assertion — gate had required the probe): recycling IS safe
(probe: recycled labels revert to inherited WHITE — NOT ForegroundPrimary! pinning primary would
DIM the tree, gate's warning), but IN-PLACE IsDifferent flip true→false lands BLACK — TreeNodeViewModel
is [ObservableProperty] (binding re-evaluates without rebind) while PropertyItem is non-notifying
(grid can only fail via recycling) — THE structural insight. Unreachable today (CompareHighlighter
only SETS the flag = monotonic invariant) but hardened anyway: two-key AccentError|BaseHigh(#FFF =
measured steady state, zero visual delta). TreeForegroundRecyclingTests pins recycling + in-place
(red both reviewers: "Expected: White, Actual: Black").
PEER BLOCKER CAUGHT PRE-COMMIT: my debug frame-capture hardcoded the scratchpad path — would have
broken dotnet test on ALL THREE CI runners (peer proved with a repoint: DirectoryNotFoundException).
Removed. RULE: throwaway capture blocks must never survive into permanent tests.
A11Y RECORD (gate corrections, outlive the consult): AccentError-as-text = 3.39:1 on diff tint /
4.26:1 on panel — BELOW 4.5 AA floor; my "4.6-5:1" was the on-black figure and 840fb8f's approval
used the same wrong arithmetic. Fix approved anyway (cures black at 1.3:1; parity + value-column
consistency govern). MAJOR FOLLOW-UP OPENED (a11y sweep item 9): lighter AccentError TEXT-variant
token (candidate #FF7B7B ≈4.9:1 on tint) across 5 markup sites; blast radius 9 test asserts in 6
files (ConverterTests:123, FieldStatusLineTests:45/46, FileCompareViewTests ×3, MessageDialogTests:44,
StylesTests:24, TreeForegroundRecyclingTests:70) — all break together when the token lands, NOT a
mass regression. PARITY-VS-AA CONFLICT logged: exact v1.9 red vs AA floor — USER DECIDES the shade.
Also noted: 1.4.1 color-only diff signaling (pre-existing, minor, in the follow-up ticket); tree
plain labels inherit WHITE while grid uses #D4D4D4 (pre-existing split, now enshrined as steadyPlain).
Suites Manager 241/241 (239+2), 0W/0E. Dual-gate: a11y APPROVE ×2 + required tree probe discharged
+ consult closed; peer REVISE→APPROVE (independent red-green ×2 with per-revert hash backups —
its own lesson: re-backup before EACH revert, comment edits change the hash).

## 2026-07-27 addendum: peer post-landing ratification + restore-hygiene rule
Peer re-verified the COMMITTED BLOB of f43dc63 (not just working tree): both red gates fire on
current bytes (recycled assert line 219 "Actual: Black"; in-place assert line 100 "Actual: Black"),
restores byte-identical, 241/241 + 0W/0E re-confirmed post-cycle. RULE (peer, bitten twice — the
change was edited under it mid-review three times): before EVERY red-check revert, re-backup and
re-hash the CURRENT file — a stale backup silently reverts intervening fixes; pairs with the
CRLF/sed lesson as "verify the file you're about to restore is the file you backed up."

## 2026-07-27: Grid header colors — v1.9 band + dark separators + sort census (see commit above)
USER: headers black vs v1.9's panel-blended band. Port's header style had dropped v1.9's
Background=SurfaceBackground + BorderSeparator 0,0,1,1 (App.xaml:1172-1180) — re-added; red
"Actual: Black" ×4. PEER FOUND the second half: Fluent's interior separator Rectangle carries a
stock #66FFFFFF fill = BRIGHT line on the new band (v1.9's separators were dark). First fix
attempt (/template/ Rectangle#VerticalSeparator Fill) FAILED — LEDGER RULE (peer: "keep verbatim"):
Avalonia BindingPriority orders Template(2) ahead of Style(3) — a TemplateBinding beats a plain
style setter; the distinguishing question is NOT "is the element inside a template" but "does the
template already BIND that property" (the TabControl WrapPanel Background override works because
nothing template-binds it). Fix: the header's SeparatorBrush styled property (template-bound by
the Rectangle) → #333; peer live-verified before(#66ffffff)/after(#ff333333), source stays
Template = mechanism proof. Red "Actual: #66ffffff".
SORT CENSUS (peer): 9 DataGrids; EIGHT set CanUserSortColumns=False, BruteForce VersionGrid
inherited Avalonia's TRUE default = live click-to-sort on the unassessed flat band TODAY (the
a11y gate's "if sorting is ever enabled" caveat was live, not hypothetical). Closed: explicit
False, nine agree (peer re-census: 9/9 + comment as 10th grep hit). V1.9 DEVIATION RECORDED:
WPF default-allowed sorting there but with its own themed sort chrome; a11y ratified as REMOVING
a latent 2.4.7 surface (transient progress dialog, sort inherited-not-designed); re-gate
2.4.7/hover before re-enabling sorting anywhere. Red "Expected: False, Actual: True".
Contrast: #AAAAAA on #2D2D30 = 5.905:1 (gate + peer independently) — beats actual v1.9 (~5.1
with old #9E9E9E). Frame matches the v1.9 reference. Suites 241/241 (asserts landed in existing
tests — count unchanged, stated), 0W/0E. Dual-gate: a11y APPROVE + additions ratified; peer
APPROVE (3 red gates, live measurements, hash restores).

## 2026-07-27 addendum: grid-header peer notes (post-landing resend)
Note 1 (standing fact): DataGridColumnHeader's template Border 'HeaderBorder' template-binds
Background, BorderBrush AND BorderThickness — all three header setters render; the border half of
the assert is meaningful, not decorative. Note 3 reach (coverage stated per no-silent-caps): the
app-wide header style lands on NINE grids across EIGHT views (Compare ×2, CreatorView, InspectorView,
SampleRestorerView, CreateSRRWizardBody, RestoreWizardBody, StoredFilesManagePanel, BruteForce
VersionGrid); the band assert covers the Compare 4 headers only — 7 of 9 grids inherit it untested
(v1.9's style was global too; visual risk uniform). Mitigation detail recorded: headers are
Focusable=False/IsTabStop=False, so the closed sorting exposure was mouse-only, never keyboard.

## 2026-07-28: SRR-guided assembly — SPEC + PLAN phase COMPLETE (codex-gated per user directive)
User: Linux reconstruction fails (EXTTIME structural divergence, itw-gaor evidence); approved the
assembly design; delegated per-step review to CODEX (quota back). SPEC: 5 revs, codex APPROVE
(rounds: 7B/3A, 2B/3A, 1B/1A, 0B/1A) — killed the RR-flag guard (RecoveryBlocksRemoved is
UNCONDITIONAL in every writer), the impossible non-CAV await, RARStream snapshot misuse, carrier-
finalization bug, name-only multiset filtering, ASCII/Unicode seam mismatch, unimplementable tests.
PLAN: 8 revs, codex APPROVE (rounds: 8B/3A, 7B/2A, 6B/2A, 7B/1A, 4B/1A, 2B/1A, 0B/2A) — 11 tasks;
biggest catches: vol1-only verification (would fail every multi-volume match), FakeRunner latch
that couldn't prove observation, zero-candidate harness (regex/empty-ranges/empty-args), quiet-
observer swallowing late producer faults on WINNING paths, Error-arm retention violation,
directory-order-dependent duplicate assertions, unbuildable verification block. Spec rev 5 @7a7b21b
(+2 advisory folds), plan rev 8 (approved) — the review loop repeatedly caught design errors that
would each have surfaced as broken mid-execution tasks. NEXT: subagent-driven execution, codex
reviews EVERY task diff (user directive), tasks 1-11 per the plan.
