# Accessibility A–F Final Gate — Small-Window Layout Feature
(RECONSTRUCTION 2026-08-03: the original persisted file lived in the feature worktree's gitignored
workspace, which was emptied after the worktree was retired. Reconstructed by the controller from
the gate agent's verbatim conversation output. Line-number cites reflect the tree AT GATE TIME
(2026-08-02, pre-13px/pre-trap-fix commits) — resolve targets by identity, not line.)

OVERALL VERDICT: PASS-WITH-FOLLOW-UPS (feature landed; NEW-1 fixed pre-landing @f5241ed).

Criteria: A (700x450 reachability+visibility) PASS w/ documented pre-existing Creator-trap exception
[trap since FIXED, 2026-08-03 @34dcbba]; B (clip containment @150%) PASS; C (Tab/Shift+Tab per-step
visibility both modes) PASS; D (log reachable) PASS; E (splitter >=3:1 + HC smoke) PASS; F
(normal-size parity) PASS. Staged-focus contract PASS (line-level code verification). Announcements
PASS on wiring; real-AT session = follow-up (e) [USER ELECTED TO SKIP, 2026-08-03].

NEW findings: NEW-1 unnamed focus-recovery targets (FIXED @f5241ed). NEW-2 Reconstructor splitter
contrast test reads Background post-Focus vs assumed keys — cannot detect :focus style deletion;
backport Creator's rendered-pixel method. NEW-3 Reconstructor reverse tab oracle derived from
observed forward order; adopt ResolveIndependentExpectedOrder. NEW-4 naive AssertFullyWithinWindow
(window-rect only) in SRSCreator + SRSReconstructor suites. NEW-5 unnamed-control inventory
enrichment: SRSCreator MainFilePath TextBox + ISO ComboBox + AppName TextBox; Creator AppName
TextBox + Output-row Browse [Browse since named @c9d54d1]; Reconstructor -mt From/To TextBoxes,
VolumeSize TextBox + unit ComboBox, decorative legend checkboxes (announce as unnamed disabled).

Debt triage: (a) Creator Input-row keyboard trap — FOLLOW-UP-REQUIRED top priority [DONE 2026-08-03
@34dcbba, 6 rounds]. (b) Unnamed picker TextBoxes — SPLIT: recovery targets = NEW-1 [done];
remainder one batch pass w/ "<subject> path" convention: Reconstructor WinRAR/Release/Verify/Output
pickers; SampleRestorer MediaDir/OutputDir; SRSCreator/SRSReconstructor siblings; + NEW-5. (c) No
app-wide HC infrastructure — ACCEPTED for feature; product initiative seeded by the 46-key fixture.
(d) Reconstructor announcement cluster — FOLLOW-UP-REQUIRED Major: Paths TabItem announces
"Avalonia.Controls.ScrollViewer" → name it; 4 identical "Browse" → differentiate; config
import-export outcomes unannounced → always-in-tree polite pattern (SaveLogStatus template);
HasCustomPackerWarning IsVisible-toggled no LiveSetting → same pattern; PathsNeedAttention visual
glyph only → expose via TabItem name/HelpText; caption-to-field associations via DescribedBy. (e)
Native NVDA/Narrator untested — REQUIRED pre-release [USER ELECTED TO SKIP 2026-08-03]. (f)
ScrollHandoffBehavior lifecycle — ACCEPTED (no production path toggles Handoff).

Carried notes: Task-1 unnamed-root fallback terminal (spec-mandated last resort); shrink-only
continued-resize coverage (grow untested, geometrically benign); no text-contrast assertions in
suite (app-wide pre-existing palette — audit alongside (c)).

Ordered follow-ups at gate time: 1. NEW-1 [done]; 2. (a) trap [done]; 3. (e) NVDA [skipped by
user]; 4. (b)+(d)+NEW-5 batch [item 2, in progress]; 5. NEW-2/3/4 test hardening [open]; 6. (c) HC
theme + contrast audit [open].
