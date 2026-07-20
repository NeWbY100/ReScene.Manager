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
