# A11y follow-up — item 1: the Creator keyboard trap

Date: 2026-08-03. Branch: `main` (from `e187764`, v2.1.0). Windows; no push.

## 1. The trap, reproduced

Reproduced against unmodified `e187764` with a genuine cold-start Tab walk — fresh window, nothing
focused, real key events — before anything was changed. The walk:

```
Input path -> Browse input file -> Browse folder for release input
  -> File (menu) -> v0.0 (status) -> Browse folder for release input
  -> File -> v0.0 -> Browse folder for release input -> ... (repeats to step 40)
```

A permanent three-element cycle: the folder-Browse button, the shell's File menu and the status-bar
version button. Stored Files, Output, Options, Create SRR and the log are **unreachable by keyboard
from a cold start**. This matches the defect recorded during the small-window work exactly.

The mechanism, confirmed rather than assumed: the Input row's three controls carry explicit
`TabIndex` 0/1/2 (`CreatorView.axaml`, currently lines 93/100/105). Unscoped, those are compared
against the **whole window's** navigation scope, where every other control carries the default
`int.MaxValue` — so the three sort ahead of the entire form, and once the walk runs off the end of
them it never comes back round to anything else.

## 2. Why the pins could not simply be deleted

Checked before reaching for the scoping fix, since removing them would have been simpler. The row is
a `DockPanel` whose children are declared **rightmost first** — folder Browse, then file Browse,
then the path box — because that is what docking requires. So the tree order is the exact REVERSE of
the visual order, and the pins exist to correct that. Deleting them would have replaced a trap with
a backwards row.

This is now pinned by a test of its own
(`PathRows_TabOrderFollowsVisualOrder_DespiteReversedTreeOrder`) which asserts the INVERSION itself —
`DockPanel.Children` in reverse of the rendered left-to-right order — and not merely the visual
order. Both halves are load-bearing: if the markup is ever reordered so tree order already matches
visual order, the pins have nothing left to correct and the test says so by name rather than quietly
passing on a premise that has rotted.

## 3. The fix

`KeyboardNavigation.TabNavigation="Local"` on the Input row's `DockPanel` — the candidate identified
during the small-window work, now validated rather than assumed. One attribute, one view.

`Local` makes the row its own navigation scope: the `TabIndex` values order the three controls
**among themselves**, and the row as a whole takes its ordinary place in the outer, tree-ordered
sequence. Both halves of the requirement hold: the trap is gone and the row's internal order is
unchanged.

Verified RED → GREEN by removing and reinstating the attribute, rebuilding each time.

## 4. What the walk looks like now

```
Input path -> Browse input file -> Browse folder for release input
  -> Add... -> Remove -> Remove All -> Move Up -> Move Down
  -> Stored Files (grid) -> Resize stored files and output (splitter)
  -> Output path -> Browse
  -> 7 Options checkboxes -> App name
  -> Create SRR -> Save log...
  -> (exits to shell chrome)
```

The whole form, from a cold start.

**Correction to an earlier version of this report**, which claimed the order was "the whole form, in
visual order". It was not, and the transcript showed it: the OUTPUT row read `Browse -> Output path`
while displaying the path box to the LEFT of the button. See §5b — that row had the same reversal,
unpinned, and was tabbing backwards. Fixed in the same round.

## 5. Fixture changes — called out explicitly

**The in-form order genuinely changed, and this is the fix working rather than a re-baseline.**

The committed fixtures had the Input row **last**, after `Save log...`. That was not a design
choice; it was the trap showing through. The old tab-order tests all start from a focused sentinel
(`Add...`) *inside* the form, and from there the only way the walk ever reached the three pinned
controls was by running off the end of everything else — so they were recorded as the tail. The
tests passed while the view was unusable from a cold start, which is exactly why the defect survived
them.

With the row scoped, it takes its natural position at the **front**. Changed accordingly:

- `ResolveIndependentExpectedOrder` — the three Input controls moved from the end of the list to the
  front (compact still prepends the Help toggle ahead of them).
- `NormalModeTabOrderFixture` and `CompactModeTabOrderFixture` — same three entries moved from last
  to first.
- Three doc comments claiming `"Add..."` is "genuinely first" corrected; that claim was true only
  because of the trap.

Nothing else moved: the relative order of every other control is byte-identical in both fixtures.
The discriminating check (`AssertSameControlSequence`, reference-based) and its own
sensitivity test are untouched.

## 5b. The Output row was backwards too — second fixture change

Found by reading this report's own walk transcript rather than the code: the Output row printed as
`Browse -> Output path` while rendering the path box left of the button.

Same construction as the Input row — a `DockPanel` whose Browse button is docked Right and therefore
declared FIRST, so tree order is the reverse of visual order — but with **no `TabIndex` at all**. It
was never trapped (nothing to sort ahead of the window scope) so it drew no attention; it was simply
tabbing backwards, and had been since the row was written.

Reproduced first: `Output row: Tab from TextBox name="Output path" should reach Button name="Browse",
not CheckBox name="Auto-include files ..."` — Tab from the path box skipped the button entirely and
left the row.

Fixed with the same validated pattern: `TabIndex` 0/1 restoring visual order, plus
`KeyboardNavigation.TabNavigation="Local"` scoping them so this row can never do to the window what
the Input row did.

**Second fixture change, called out:** `outputBrowse, outputTextBox` became
`outputTextBox, outputBrowse` in `ResolveIndependentExpectedOrder` and in both fixtures. Two entries,
adjacent, swapped; nothing else moved.

The coverage now runs over BOTH rows: `PathRows_TabOrderFollowsVisualOrder_DespiteReversedTreeOrder`
replaces the Input-only test and asserts each row three ways — markup order is the reverse of visual
order, rendered order is left-to-right as expected, and Tab walks it in that order.

## 6. Tests

| Test | Purpose |
|---|---|
| `ColdStartTabWalk_EscapesTheInputRow_AndReachesThePrimaryAction` | the permanent regression guard: cold start, real Tab events, must reach Create SRR within 40 steps, then Shift+Tab back to the Input path box |
| `PathRows_TabOrderFollowsVisualOrder_DespiteReversedTreeOrder` | both path rows, three ways each: markup order is the reverse of visual order (the premise the pins exist for), rendered order, and Tab order |

Both dump the full walk on failure, so a future regression names the cycle rather than just failing.

`CompactViewRig.StepFocus` was widened from `private` to `internal` (documented): every existing pass
in that rig starts from a focused sentinel, and a cold start is a different entry point into the
order — the one a keyboard-only user meets first, and the one the trap lived in.

One correction I made mid-work, worth recording: the first version of the cold-start test targeted
`Create SRR` without setting the paths, and it failed for the wrong reason — the button is
command-gated on both paths, and Tab correctly skips a disabled control. The test now sets the paths
and asserts the button is enabled as an explicit precondition, so it measures the tab order rather
than the command's `CanExecute`.

## 7. Evidence

`-t:Rebuild` on all three projects, each **0 Warning(s), 0 Error(s)**, then `dotnet test --no-build`:

```
Manager   Passed!  - Failed: 0, Passed: 456, Skipped: 0, Total: 456
App.Core  Passed!  - Failed: 0, Passed: 712, Skipped: 0, Total: 712
```

Baselines Manager 454 / App.Core 712. Delta **+2 Manager** (the two new tests), App.Core unchanged.
All 66 `CreatorCompactTests` green, including the reference-exact forward and reverse walks and the
criterion-C visibility passes.

## 8. Finding: the same pattern in the Beginner wizard — NOT fixed, reported

`CreateSRRWizardBody.axaml` (lines 34/39/43) carries the **identical** construction: a `DockPanel`
with unscoped `TabIndex` 0/1/2 on the same three input controls. I probed it rather than assume
either way.

Measured, real `WizardWindow`, cold start:

```
Release .sfv, first .rar, or folder -> Browse input file -> Browse folder for release input
  -> Close -> Next ›  -> (repeats)
```

**It is latent risk, not an active trap.** The cycle includes `Next ›` and `Close`, so a keyboard
user can both advance the wizard and leave it — and step 1's body contains no other focusable
controls (the progress bar and the detected-sets list are both deliberately non-focusable), so the
cycle covers everything that step offers. Later steps are unaffected: the step panels are
`IsVisible`-bound, so step 1's controls drop out of the order entirely once another step is shown.

The risk is that this is only true by coincidence of step 1 being sparse. The moment anything
focusable is added to that step — a checkbox, a second field — it would be excluded exactly as the
Creator's form was.

**And its step-3 row has the naming gap the Creator's Output row just had, plus one more.**
`CreateSRRWizardBody.axaml`'s step-3 panel — the one headed "Save SRR to", whose `DockPanel` holds a
`Button` bound to `BrowseOutputCommand` and a `TextBox` bound to `OutputPath` — mirrors the row fixed
in §12: same right-docked `DockPanel`, same reversed tree order. MEASURED at runtime on step 3, its
two controls announce as:

```
Button   content="Browse"   peerName="Browse"
TextBox  content=""         peerName=""
```

The button falls back to its bare content, and the text box has no name at all: no
`AutomationProperties.Name`, no `x:Name`, and no `LabeledBy` pointing at the "Save SRR to" heading
above it (unlike the same body's step-1 field, `WizInputTextBox`, which does bind `LabeledBy` to its
own heading). So §12's "every control
in both rows announces itself" mitigation does NOT hold in the wizard, and the gap there is wider
than the one just closed in the Creator. Goes with the §8 pickup.

I did not fix it. It is a second user-facing surface with its own test suite and its own tab-order
fixtures, which would need the same "what did this fixture actually encode" scrutiny this report
gives the Creator's; and this workstream is explicitly sequenced in gated items. The reproduction
above is ready to hand if it is folded into a later item.

**Guidance for whoever picks it up — the naive fix does not work here.** Copying this round's
one-attribute change onto the wizard's row is NOT sufficient, and would produce a different wrong
order rather than the right one:

> "Local alone would place footer controls before BodyHost. Fix later with aligned host tree order
> plus exact real-window walks."

The wizard window's own tree puts its footer (Back / Next / Close) ahead of the body host, so scoping
the row without also aligning that host order would hand the user the footer before the step's
fields. The fix there is therefore two-part — scope the row AND align the host tree order — and it
has to be validated with exact walks against the real `WizardWindow`, not the body in isolation (the
body-only probe in this section is what first suggested a tighter cycle than the real window
actually has).

## 9. Concerns

1. **The wizard (§8)** is the main one — latent rather than live, but it is the same defect one edit
   away from mattering, and its fix is genuinely harder than this one (the footer/BodyHost tree order
   has to be aligned too, or scoping alone reorders the wizard wrongly).

2. **The cold-start test asserts a bounded step count (40).** That is a proxy for "the walk
   terminates somewhere useful" rather than a statement about the true order length; the current
   walk reaches Create SRR in far fewer. If the form grew substantially the bound would need
   raising, and the failure would read as a trap when it was really a budget.

3. **`Local` scoping is verified for this row's shape, not proven in general.** The row has three
   controls and one level of nesting. I have not characterised how `Local` interacts with a nested
   scope inside another scoped container, which is what a future author copying this fix elsewhere
   would meet.

4. **Reverse-direction coverage is the walk, not a fixture.** The cold-start test asserts Shift+Tab
   from the primary action returns to the Input path box; the reference-exact reverse fixture in
   `AssertTabWalk` covers the in-form order. Neither exercises Shift+Tab from a cold start (nothing
   focused, first key is Shift+Tab), which is a real entry point I did not test.

5. **Windows only**, as with everything in this workstream — Avalonia's keyboard navigation is
   platform-independent code, so I would expect this to hold, but CI is what confirms it.

## 10. Retiring the trap language — and the sweep, reproducibly this time

An earlier version of this section claimed a clean sweep after reconciling four sites. **That claim
was false**: five more sites still described the trap as current, one of them load-bearing. The
claim was asserted rather than run. This section replaces it with commands and their output.

### 10a. Sites reconciled

Round 2 (four sites) and round 3 (five more). Each rewritten to say what it NOW guards, or retired:

| Site | Disposition |
|---|---|
| `CreatorView.axaml.cs` — `RestoreFocusTarget` wiring | Rewritten. Target unchanged; the reason is no longer "keep recovery out of the trap". |
| `RestoreFocusTarget_IsNotOneOfTheThreeTrappedControls` | Renamed `RestoreFocusTarget_PrefersTheOutputFieldOverTheTopOfTheForm`; doc and all three assertion messages rewritten. |
| `AssertReachableByAllThreeRoutes` doc | Rewritten — the `keyboardAnchor` parameter is still justified, by the helper's own narrower claim rather than by the trap. |
| Staged-focus test comment citing "TabIndex-trapped controls" | Rewritten. |
| **`AssertTabWalk` doc (the ~25-line block)** | Rewritten. It asserted the Input row "sits LAST in the whole view's tab sequence" and that this was "intentionally NOT fixed here" — wholly obsolete; it now describes the scoped rows and points at the cold-start and premise tests. |
| Compact help-body anchor comment | Rewritten — anchoring is about what the assertion is *about*, not about avoiding a trap. |
| `"Add..."` anchor comment | Rewritten, same. |
| **`SmallWindowBoardTests` — the `hasKnownColdStartTrap` bypass** | **Retired, not reworded** (see 10b). |
| Cold-start test summary | Tightened to unambiguous past tense ("the keyboard trap this view USED to have"). |

### 10b. The board bypass was obsolete behaviour, not just obsolete wording

`AssertViewSurvivesFontGrowth` took a `hasKnownColdStartTrap` flag, true only for the Creator, which
replaced the cold-start keyboard walk with a direct `Focus()` call — justified explicitly by the
trap. Its own comment anticipated this: *"if the trap is ever fixed, AssertReachableByKeyboard's own
pre-check still requires the SAME bar"*.

The trap is fixed, so the parameter, the branch and the flag at all five call sites are **removed**.
The Creator is now walked from a genuine cold start exactly like the other four views, and passes —
verified, not assumed. Net effect: the board case tests strictly more than it did.

### 10c. The sweep

**Method note, learned the hard way.** An earlier version of this section gave survivor counts that
did not match the tree: the inventory had been taken part-way through the round and then edited
around, so it cited stale lines and undercounted (12 where the tree held 15 for one file). The sweep
now runs LAST — after the final edit of the round — and this section quotes that run and nothing
else.

Four greps for language that would indicate an unreconciled site. Run from the repo root against the
final tree; all four empty:

```
$ grep -rniE 'hasKnownColdStartTrap|IsNotOneOfTheThreeTrappedControls|NotDeclarationOrder'     --include=*.cs --include=*.axaml ReScene.Manager/ ReScene.Manager.Tests/ | wc -l
0
$ grep -rniE '(is|are|gets|remains|stays) trapped|TabIndex-trapped|trap loop|the trap those three'     --include=*.cs --include=*.axaml ReScene.Manager/ ReScene.Manager.Tests/ | wc -l
0
$ grep -rniE 'NOT fixed here|deferred to its own follow-up|stays deferred|pre-existing TabIndex defect|sit LAST'     --include=*.cs --include=*.axaml ReScene.Manager/ ReScene.Manager.Tests/ | wc -l
0
$ grep -rniE 'near the work' --include=*.cs --include=*.axaml ReScene.Manager/ ReScene.Manager.Tests/ | wc -l
0
```

Round 5 added a fifth pattern for the unmeasured-count defect it introduced. This one has exactly
one survivor, and it is true:

```
$ grep -rniE 'four buttons' --include=*.cs --include=*.axaml ReScene.Manager/ ReScene.Manager.Tests/
ReScene.Manager/Views/CreatorView.axaml:  ...Reconstruct wizard's four buttons already use.   [1 hit]

$ grep -c 'AutomationProperties.Name="Browse for' ReScene.Manager/Views/Wizards/ReconstructWizardBody.axaml
4
```

(The one hit is in `CreatorView.axaml`'s Output-row naming comment. Its line number is deliberately
omitted for the reason given above.)

That clause is about the Reconstruct wizard, not this view, and the wizard does have four — measured,
not assumed, this time.

The broad grep is deliberately NOT empty. Exact count and per-file breakdown from the same run:

```
$ grep -rniE 'trap' --include=*.cs --include=*.axaml ReScene.Manager/ ReScene.Manager.Tests/ | wc -l
40

$ grep -rniE 'trap' ... | cut -d: -f1 | sort | uniq -c
     11 ReScene.Manager.Tests/CompactViewRig.cs
     15 ReScene.Manager.Tests/CompactViewRigTests.cs
      8 ReScene.Manager.Tests/CreatorCompactTests.cs
      1 ReScene.Manager.Tests/ReconstructorCompactTests.cs
      1 ReScene.Manager.Tests/ScrollReachabilityTests.cs
      2 ReScene.Manager.Tests/SmallWindowBoardTests.cs
      1 ReScene.Manager.Tests/WindowFontSizeParityTests.cs
      1 ReScene.Manager/Views/CreatorView.axaml.cs
```

**Category A — unrelated to this defect (29).** The rig's own vocabulary: an "early trap" is its term
for a tab walk that repeats before it is complete, and `CompactViewRigTests` builds deliberate
`TrapA`…`TrapD` fixtures to prove the rig detects them. `WindowFontSizeParityTests.cs:13` is an
"Avalonia style-key trap", a different thing entirely.

`CompactViewRig.cs` ×11 · `CompactViewRigTests.cs` ×15 · `ReconstructorCompactTests.cs` ×1
(the tab-order fixture's note on indistinguishable "Button:" entries) · `ScrollReachabilityTests.cs`
×1 (the no-expected-stops caveat) · `WindowFontSizeParityTests.cs` ×1 (the class doc's "style-key
trap")

**Category B — historical, explicitly past tense, about this defect (11).** Accepted survivors: each
records that the trap *was* fixed or what the code *used to* do, which is what a future reader needs
to understand why the pins and the scoping exist.

Cited by **member name and quoted text**, not line number. Two rounds of this report carried stale
line cites — the pre-round-4 inventory, and again after round 5's edits shifted seven of these by
+2/+4 — because the inventory is written once and the file keeps moving underneath it. Member names and the quoted
phrases move WITH the code; line numbers do not, and re-refreshing them was the third recurrence of
the same defect. Any line number below is parenthetical, dated, and not the identifier.

| Member | Quoted text |
|---|---|
| `CreatorView` ctor (`CreatorView.axaml.cs`) | "…out of the Input row's keyboard trap; that trap is now [fixed]" |
| `AssertReachableByAllThreeRoutes` doc | "…never reaching the rest of the form. That trap is [fixed]" |
| `ResolveIndependentExpectedOrder` body | "It used to come LAST… the keyboard trap showing [through]" |
| `ColdStartTabWalk_EscapesTheInputRow_AndReachesThePrimaryAction` doc ×3 | "the keyboard trap this view USED to have" · "…somewhere the trap did not hold" · "…not merely 'focus moved': the trap [moved focus perfectly well]" |
| `PathRows_TabOrderFollowsVisualOrder_DespiteReversedTreeOrder` doc | "…trapped the Input row)." |
| `RestoreFocusTarget_PrefersTheOutputFieldOverTheTopOfTheForm` doc ×2 | "…now that the Input row's keyboard trap is fixed" · "This guard was originally about that trap" |
| `AssertViewSurvivesFontGrowth` body (`SmallWindowBoardTests.cs`) ×2 | "…unscoped TabIndex pins trapped a cold-start walk" · "That trap is fixed (the path rows are scoped…)" |

29 + 11 = 40, matching the count above.

## 11. Correction: "the last field before the primary action"

Round 2 justified keeping `OutputTextBox` as the `RestoreFocusTarget` by calling it "the last field
before the primary action". **That is false** — the seven Options checkboxes and the App-name field
sit between it and Create SRR. Corrected in both places it appeared (`CreatorView.axaml.cs` and the
guard test's doc).

What replaced it is deliberately weaker, because the stronger claim is not available: the choice was
originally *forced* by the trap, and removing the trap removes the force rather than confirming the
choice. The honest statement is that `OutputTextBox` is a named, always-present field partway down
the form, so recovery lands there instead of resetting the user to the very first row — and that no
claim beyond "not the top" is being made for it. Whether some other control is a better landing is
an open question this work did not answer.

## 12. UIA tree order recorded and asserted

The markup inversion is not only a keyboard concern: a UIA tree-walker reads the automation peer
tree, which follows children order, **not** `TabIndex`. So a screen-reader user navigating either
path row structurally meets Browse *before* the path box, and the pins cannot change that — they fix
keyboard order only.

`PathRows_TabOrderFollowsVisualOrder_DespiteReversedTreeOrder` previously asserted
`DockPanel.Children`, which is the same underlying order but not stated in UIA terms. It now also
asserts the peer children explicitly, via
`ControlAutomationPeer.CreatePeerForElement(dockPanel).GetChildren()`, so the AT-visible order is
recorded as a fact of the design rather than left implied. It passes: the UIA order IS the reversed
order.

Mitigation: every control in both rows announces itself, so the reading is unambiguous whichever
order an AT presents them in. That was not quite true when this was first written — the Output row's
Browse button had no `AutomationProperties.Name` at all and announced as the bare "Browse". It now
carries **"Browse for output path"**, following the `Browse for <target>` phrasing the Reconstruct
wizard's four buttons already use. (The Input row's two were left alone in THIS round, as
"Browse input file" / "Browse folder for release input"; bringing the Browse buttons onto one
convention was deferred to a separate batch. **That batch is item 2**, which renamed the file one to
**"Browse for input file"** and established that the folder one must NOT join the convention at all —
its visible Content is "Browse folder…", so "Browse for release folder" would break Label-in-Name.
See §C2 and its deviation list.)

**Census, measured** — because a first draft of this fix asserted an unmeasured "four buttons in this
view read Browse" in two shipped comments, which is the exact failure mode §10c's method note was
written about, re-committed one round later.

`grep -c 'Content="[^"]*Browse' CreatorView.axaml` returns **three**, identified by what they are
rather than where they sit:

| Button (`Command` binding) | `Content` | `AutomationProperties.Name` | Bare "Browse"? |
|---|---|---|---|
| `BrowseInputFolderCommand` | "Browse folder…" | "Browse folder for release input" | no |
| `BrowseInputCommand` | "Browse" | "Browse input file" | **yes** |
| `BrowseOutputCommand` | "Browse" | "Browse for output path" | **yes** |

> **Superseded 2026-08-03 by item 2 (§C2).** `BrowseInputCommand`'s name is now
> **"Browse for input file"** — that row is the only cell of this table item 2 changed. The
> `Content` column and the bare-"Browse" count of two are unchanged (no visible label moved), and
> `BrowseInputFolderCommand` keeps its name for the §I2 reason, which item 2 re-confirmed rather
> than re-tested by accident. This note is here because the table above is a round-4 record and is
> deliberately not being rewritten; the live assertions are in
> `PathRows_TabOrderFollowsVisualOrder_DespiteReversedTreeOrder` and
> `AccessibleNamingTests.Creator_InputRowBrowseButtons_UnifyExceptWhereLabelInNameForbidsIt`.

So exactly **two** render the bare word. The "four" was carried over from the adjacent clause about
the Reconstruct wizard, which genuinely has four (`grep -c 'AutomationProperties.Name="Browse for'
ReconstructWizardBody.axaml` → 4). Both comments now state the measured two, and every count here
was verified locally rather than taken from the review on faith.

The expected names are asserted as **literal strings written in the test**, not read back off the
controls. Deriving them from the very controls under test is tautological — it passes through any
rename, including one that strips a name back to the bare "Browse". Verified discriminating by a
throwaway sabotage (reverted): removing the Output button's name fails the test on a string
comparison.

Making the tree order match the visual order would mean restructuring the rows away from
right-docking — a layout change, not an attribute — and is not attempted here.

## 13. Evidence (rounds 3–4)

`-t:Rebuild` on all three projects, each **0 Warning(s), 0 Error(s)**, then `dotnet test --no-build`:

```
Manager   Passed!  - Failed: 0, Passed: 456, Skipped: 0, Total: 456
App.Core  Passed!  - Failed: 0, Passed: 712, Skipped: 0, Total: 712
```

Baselines Manager 456 / App.Core 712 — unchanged across rounds 3 and 4. No tests were added: the
work was reconciliation, one behavioural removal (the board bypass, which made an existing case
cover more), and assertions added inside an existing test (the UIA order, then the literal names).

Round 4 also produced one fixture consequence worth noting: naming the Output Browse button changed
its `Describe()` output, so both tab-order fixtures moved from `Button name="Browse"` to
`Button name="Browse for output path"`. One entry each; nothing reordered.

**Round 6 changed the citation FORM, not just the citations.** Round 5's edits shifted
`CreatorCompactTests.cs` by +2/+4 lines and §10c's survivor table was not re-run, so 7 of its 11
cites went stale — the second time line-number cites in this report had rotted. Refreshing them
again would have bought one more round. They are now keyed by member name and quoted text,
both of which move with the code; the same form is applied to §12's census (which cites the three
Browse buttons by their `Command` binding) and to §8's wizard row. The class of defect is closed,
not the instance.

Round 5 changed comments only — two false counts, one rationale that round 4 had silently falsified
(naming the output button made "two of the three carry explicit names, the third falls back to its
Content" untrue, 378 lines above the change), one over-broad "these buttons all share", and two
`<paramref>` tags left pointing at a renamed parameter. That last one went uncaught because the test
project does not set `GenerateDocumentationFile`, so `paramref` targets are never validated by the
compiler; enabling it was deliberately NOT done here, as it would surface unrelated documentation
warnings across the whole project and belongs in its own change. The broad `trap` census is
unchanged at 40 and the per-file breakdown above still matches, re-run after the final edit.

---

# Item 2 — naming/announcement batch: ATTEMPTED, REVERTED, findings recorded

Date: 2026-08-03. Base `main` @34dcbba. **No commits. Working tree returned to 34dcbba; both suites
green (Manager 456/456, App.Core 712/712) on forced rebuilds, 0W/0E.**

I built most of the naming pass and one announcement, then reverted it rather than land it. The
reasoning is in §I5. What follows is everything the attempt established, so a re-dispatch starts
from knowledge rather than from scratch.

## I1. The named requirements source does not exist

The brief cites `.superpowers/sdd/2026-07-30-small-window-layout/a11y-gate-report.md` and states
"this workspace carries the gate report the requirements come from". It does not:

```
$ ls .superpowers/sdd/2026-07-30-small-window-layout/
a11y-followup-report.md   derived-threshold-fix1-package.txt   derived-threshold-package.txt
derived-threshold-report.md   flash-fix-package.txt   progress.md

$ find . -iname "*a11y*" -not -path "*/obj/*" -not -path "*/bin/*"
./.superpowers/sdd/2026-07-30-small-window-layout/a11y-followup-report.md
```

Nor is its content recoverable elsewhere in the repo. So everything below derives from the brief's
own SCOPE enumeration, not from the gate report — items attributed to the gate (its exact wording,
its full target list, whether its (b)/(d)/NEW-5 sections contain targets the brief omitted) could
not be checked. **The scope list should be treated as the brief's, not the gate's.**

## I2. A rename in the brief's own scope violates WCAG 2.5.3, and must not be made

The brief asks to unify Creator's Input row to "Browse for &lt;target&gt;". One of those two buttons
**cannot** take that phrasing.

Measured — visible `Content` of every Browse button across the four views:

```
$ grep -n 'Content="Browse[^"]*"' <the four views>
CreatorView.axaml:90   Content="Browse folder…"      <-- the only non-bare one
CreatorView.axaml:97   Content="Browse"
CreatorView.axaml:204  Content="Browse"
SampleRestorerView.axaml:72, 98, 119    Content="Browse"
SRSCreatorView.axaml:63, 96, 120        Content="Browse"
ReconstructorView.axaml:166, 183, 200, 217  Content="Browse"
```

WCAG 2.5.3 (Label in Name, level A) requires the accessible name to CONTAIN the visible label, so a
speech-input user can activate a control by saying what they see. "Browse for release folder" does
not contain "Browse folder" — renaming that button breaks a level-A criterion to satisfy a naming
convention. The existing "Browse folder for release input" is correct *because* it contains the
visible label.

Caught by an existing test that already encodes this rule —
`CreatorViewFolderBindingTests.FolderBrowseButton_HasLabelInName_AccessibleName`, whose own comment
says "Label-in-Name (WCAG 2.5.3): the accessible name contains the visible 'Browse folder'". I had
made the rename, the test failed, and I reverted it. **Every other button in scope has content
"Browse", so "Browse for &lt;target&gt;" is safe for all of them** — the conflict is specific to
this one control.

Recommendation for the re-dispatch: unify the *file* Browse ("Browse input file" →
"Browse for input file") and leave the folder Browse alone, with the reason recorded at the site.

## I3. What was built and worked

All of the following compiled, and its own tests passed:

- **Reconstructor** — the four path pickers named ("WinRAR versions folder path", "Release files
  path", "Verification file path", "Output folder path") and their four Browse buttons named. The
  four button names were adopted VERBATIM from `ReconstructWizardBody.axaml`, which already ships
  exactly those four strings for the identical four commands (`BrowseWinRARCommand`,
  `BrowseReleaseCommand`, `BrowseVerificationCommand`, `BrowseOutputCommand`) — so the Advanced tab
  and the wizard identify the same functions identically (WCAG 3.2.4). That correspondence is a
  genuinely useful discovery: no new strings had to be invented for the headline defect.
- **Reconstructor Options** — `SwitchMTStart`/`SwitchMTEnd` ("Thread count range start"/"end"),
  `VolumeSize` ("Volume size") and its unit ComboBox ("Volume size unit").
- **Reconstructor tri-state legend** — the three disabled glyphs are decorative (each sits beside a
  caption that states the meaning in full text), so `AutomationProperties.AccessibilityView="Raw"`
  removes them from the control view rather than naming them, which would only duplicate the
  caption. **`AccessibilityView` compiles and works in this Avalonia version** — it is used nowhere
  else in this codebase, so that was worth establishing.
- **Paths TabItem** — announced as `"Avalonia.Controls.ScrollViewer"` because a composite header
  leaves the peer nothing to fall back on. Fixed with a VM property `PathsTabAccessibleName`
  returning "Paths" or "Paths — needs attention", change-notified from all four path properties, so
  the warning glyph's signal reaches a screen reader; the glyph itself goes `AccessibilityView=Raw`
  as it now duplicates the name.
- **SampleRestorer / SRSCreator / Creator / wizard step 3** — remaining pickers, the ISO ComboBox,
  both AppName boxes, and the wizard's previously-nameless output row (Browse named; TextBox
  `LabeledBy` its "Save SRR to" caption, matching step 1's `WizInputTextBox` pattern).
- **Custom-packer announcement** — the banner toggles `IsVisible`, so it announces nothing. Added an
  always-in-tree `LiveSetting="Polite"` TextBlock bound to the same `CustomPackerWarning`, sharing
  the log-header row with `SaveLogStatus` via a fixed `1*/1*` split — copying SRSReconstructor's
  `ResultStatus` precedent exactly rather than inventing placement. No VM change needed: the VM
  already clears the warning at run start, which is what re-arms the empty→text transition.
- **`AccessibleNamingTests`** — four tests, literal expected strings throughout (the round-4
  lesson), covering all of the above. All four passed.

## I4. The fixture fallout, measured

The renames change `CompactViewRig.Describe` output, so every tab-order fixture that contains a
renamed control must move. Measured: **25 failures across five suites**, all of one kind, none
mysterious:

- 4 view suites × 2–3 fixtures each, containing both the renamed Browse buttons AND the renamed
  TextBoxes/ComboBox (I initially updated only the buttons and left the boxes, which is the error
  that stopped me — see §I5).
- `CreatorViewFolderBindingTests.FolderBrowseButton_HasLabelInName_AccessibleName` — §I2.
- **Two covering tests lose their premise outright**, and this is design work rather than fixture
  work: `AssertSameControlSequence_SwappedIdenticallyDescribedBrowsePositions_FailsNamingTheMismatch`
  in both `ReconstructorCompactTests` and `SampleRestorerCompactTests` exists to prove the
  reference-based order comparison catches a positional swap of *identically described* controls.
  Naming the buttons removes the identical descriptions those tests select on — Reconstructor drops
  from four bare "Browse" to zero, SampleRestorer from three to one. Each needs a new
  identically-described pair (SampleRestorer's own doc notes its row checkboxes describe
  identically, which is a candidate; the Reconstructor needs one found) or a rethink. Silently
  re-pointing them at a different pair without checking that the pair is genuinely
  indistinguishable would hollow the test out.

## I5. Why I reverted rather than finished

I got the Browse-button fixture mapping right and the TextBox mapping wrong, and only found out by
running. That told me I did not actually know those walk orders without measuring them — and I was
at the end of a long session, with ~20 more positional fixture edits and two test redesigns to go.

Positional edits to accessibility fixtures fail silently in the dangerous direction: a wrong string
encodes a wrong expectation that then passes. This whole review chain has been about exactly that
class of error in my own work — unmeasured counts, stale cites, self-derived expectations,
re-baselined fixtures. Finishing under context pressure was a bad bet against it.

So: tree returned to 34dcbba, both suites verified green, nothing committed, and the knowledge
written down. A re-dispatch has the WCAG conflict resolved, the wizard-name correspondence found,
`AccessibilityView` proven, the announcement placement settled, and an exact inventory of the
fixture fallout — which is most of the hard part.

## I6. Not attempted

- **Config import/export outcome announcements** — in the brief's scope, not started.
- **`DescribedBy` caption associations** — in the brief's scope, not started.
- **The wizard's TabIndex construction** — correctly out of scope; stays ledgered with §8's
  two-part guidance.

---

# Item 2 (completed) — naming/announcement batch

Date: 2026-08-03. Base `main` @34dcbba. One commit. Nothing pushed.

The re-dispatch. §I1–I6 above are the previous attempt's investigation, and most of them held up.
Two did not, and both were load-bearing — see §C1. Everything below was measured on this tree.

## C1. Two things the previous attempt got wrong, caught by measuring

**`AccessibilityView="Raw"` does not do what §I3 says it does.** §I3 records "`AccessibilityView`
compiles and works in this Avalonia version — it is used nowhere else in this codebase, so that was
worth establishing", and the brief carried that forward as "I3 proved it works". It compiles, and
`GetAccessibilityView` reads `Raw` back, which is exactly what makes it look like it works. It has
no effect on the peer tree. Measured, and pinned in the shipped test: a `StackPanel` with **two**
children, one of them marked `Raw`, returns **both** from
`ControlAutomationPeer.CreatePeerForElement(panel).GetChildren()` — the `Raw` one included, with its
name intact (`AccessibleNamingTests.AccessibilityViewRaw_DoesNotPruneThePeer_…`, which asserts
`children.Count == 2`).

(An earlier draft of this paragraph said "four children … returns all four". That described the
throwaway probe, which had four; the committed test has two. The probe is deleted, so the count a
reader can actually go and check is two — and a report about not writing unverifiable counts should
not carry one in the paragraph that establishes the rule.)

This was already known in this repo. `StylesTests`' `HelpDisclosure_ExposesCoherentAutomationPeers_InBothModes`
says so in its own doc comment — "`AutomationProperties.AccessibilityView=Raw` was MEASURED not to
prune the peer from its parent's children walk at all" — which is why that fix went to the behavior
rather than the peer. Shipping `Raw` on the legend checkboxes would have been a decorative attribute
plus a comment claiming an effect the code does not have.

Consequences, both changed from the brief:
- The three legend checkboxes are **`LabeledBy` their own caption** instead, so each announces the
  full-text meaning next to it. That closes the gate's 4.1.2 finding ("announce as unnamed
  disabled") with a mechanism that demonstrably works, and keeps the visible text the single source
  of the label. `AccessibilityViewRaw_DoesNotPruneThePeer_WhichIsWhyTheLegendIsNamedInstead` pins
  the platform fact, so if a future Avalonia starts honouring `Raw` the decision gets reconsidered
  rather than silently kept.
- The Paths header's warning glyph is **left alone**. It cannot be pruned (`Raw` is a no-op) and it
  cannot be silenced (`AutomationProperties.Name=""` on a `TextBlock` still announces its `Text` —
  measured: the peer returned "⚠" anyway). It also turns out not to matter: a `TabItem` peer does
  **not** expose its header's `TextBlock`s as children at all, measured on both a synthetic
  `TabItem` and the real view. So the glyph was never reachable through the header; the reason to
  put its meaning in the tab's NAME is that there was otherwise no channel for it whatsoever, not
  that the glyph was duplicating anything.

**`AutomationProperties.DescribedBy` does not exist in Avalonia 11.3.18.** Item 6 of the brief asks
for caption associations "via `AutomationProperties.DescribedBy` (or `LabeledBy` where the caption
IS the label)". Enumerated from the shipped `Avalonia.Controls.dll` metadata, the attached class
exposes `AcceleratorKey, AccessibilityView, AccessKey, AutomationId, ControlTypeOverride, HelpText,
LandmarkType, HeadingLevel, IsColumnHeader, IsRequiredForForm, IsRowHeader, IsOffscreenBehavior,
ItemStatus, ItemType, LabeledBy, LiveSetting, Name, PositionInSet, SizeOfSet` — no `DescribedBy`.
The rule actually applied is in §C4.

## C2. The scope, and where it differs from the brief

| Surface | Done |
|---|---|
| Reconstructor, 4 pickers | 4 TextBox names + 4 Browse names (wizard's strings verbatim) |
| Reconstructor, Options | -mt From/To, VolumeSize, unit ComboBox, 3 legend checkboxes |
| Reconstructor, Paths sub-tab | `PathsTabAccessibleName` on the VM, bound to the TabItem |
| Reconstructor, announcements | custom-packer + config outcomes, 3-column live-line row |
| SampleRestorer | MediaDir, OutputDir |
| SRSCreator | MainFile, Output, ISO ComboBox, AppName |
| SRSReconstructor | MediaFile, Output |
| Creator | file Browse renamed, AppName |
| Create-SRR wizard | step-3 Browse + OutputPath; step-0 file Browse (3.2.4) |

Deviations, each deliberate:

1. **`Verify file path`, not `Verification file path`** (which §I3 used). The row's visible caption
   reads "Verify". WCAG 2.5.3 wants the accessible name to contain the visible label, and
   "Verification" does not contain "Verify". The wizard's own field keeps `LabeledBy` on a caption
   that reads "Verification file (.sfv or .sha1)" — the two surfaces differ because their visible
   labels differ, which is what 2.5.3 requires of each.
2. **`Thread count from` / `Thread count to`, not `…range start` / `…range end`** (which §I3 and the
   gate used). Same criterion: each box's visible label is the "From:"/"To:" beside it. This is the
   identical trap §I2 caught on the folder Browse, one control over.
3. **SRSCreator's Output box and SRSReconstructor's two pickers are included** though the brief's
   enumeration lists neither. Gate item (b) says "SRSCreator/SRSReconstructor siblings", and leaving
   one of three pickers unnamed in a view whose other two were just named is not defensible.
4. **The Create-SRR wizard's step-0 file Browse is renamed too.** The brief names only CreatorView's.
   They are the same command on the same ViewModel type; renaming one and not the other would create
   the 3.2.4 inconsistency this batch exists to remove.
5. **SampleRestorer's, SRSCreator's and SRSReconstructor's Browse buttons are NOT renamed.** Only
   their TextBoxes were in scope. Unlike the Reconstructor's four — whose names already existed in
   the wizard and needed no invention — these have no cross-surface twin, and inventing eight new
   strings is a wider change than this pass took on. Recorded in each suite's own fixture note.

## C3. The fixture fallout, measured on THIS tree

**28 failures across 5 suites**, not §I4's 25. The count differs because the scope differs
(deviation 5 above: §I4's attempt renamed the sibling views' Browse buttons, this one did not).
Counts are quoted from the run, not carried over:

```
SRSCreatorCompactTests        5
SampleRestorerCompactTests    5
CreatorCompactTests           6
ReconstructorCompactTests     7
SRSReconstructorCompactTests  5
                             --
                             28   (of 456)
```

Two consequences worth stating because the brief expected otherwise:

- **`CreatorViewFolderBindingTests.FolderBrowseButton_HasLabelInName_AccessibleName` stayed green.**
  §I2's trap is real and was avoided rather than re-sprung: the folder Browse was not renamed, and
  the reason is now recorded at the site in both CreatorView and the wizard body, plus asserted from
  the Label-in-Name side in `Creator_InputRowBrowseButtons_UnifyExceptWhereLabelInNameForbidsIt`.
- **ONE covering test lost its premise, not two.** §I4 predicted both
  `AssertSameControlSequence_SwappedIdenticallyDescribedBrowsePositions_FailsNamingTheMismatch`
  tests would break. SampleRestorer's did not, and could not: its three "Browse" buttons still
  describe identically because they were not renamed. Its premise was checked, not assumed — it
  passed unmodified through the whole run. Only the Reconstructor's broke (§C5).

**Every fixture was regenerated from a measured walk, never edited entry by entry.** Each suite got
a temporary dump method that hosted the view at that mode's own height using the suite's OWN setup
(same VM construction, same sentinel, same `CaptureTabOrderControls` call — and, for the four suites
that have one, only after `AssertSameControlSequence(independentOrder, forwardOrder)` passed, so the
dumped order is one the reference-based oracle already agrees with), printed the real `Describe`
sequence in fixture-literal form, and was then removed. That is the step §I5 stopped at, and it is
what turned up two entries a positional edit would have got wrong:

- The Reconstructor's TabItem reads **`Paths — needs attention`**, not `Paths`. The inert VM every
  fixture is captured against has all four paths empty, which is exactly the state that raises the
  glyph. Writing `Paths` from the markup would have been wrong.
- Both App-name boxes and the ISO ComboBox announce their caption **with its trailing colon** —
  `App name:`, `File inside ISO:` — because `LabeledBy` resolves to the caption's literal `Text`.
  Recorded as measured rather than tidied; a fixture saying `App name` would have been asserting
  something the app does not do.

## C4. The caption-association rule actually applied

`DescribedBy` does not exist (§C1), so there is no channel that says "this text explains this field"
without also making it the field's NAME. The rule, applied per site and recorded at each:

- **Caption is a short standalone label** → `LabeledBy` it. The caption IS the name, the visible text
  stays its single source, and 2.5.3 holds by construction. Applied to: both App-name boxes, the ISO
  ComboBox, the wizard's step-3 output box (matching step 0's `WizInputTextBox`), and the three
  legend checkboxes.
- **Caption is a subject+prose sentence** (every picker row in the Advanced views: *"WinRAR — Folder
  containing WinRAR version subfolders used to recompress. Older releases need older WinRAR
  versions."*) → **explicit short name**, subject taken from that caption so the name still contains
  the visible label. `LabeledBy` here would make a 100-character sentence the field's name, read out
  in full every time focus lands on it.

Deliberately NOT done: duplicating the prose into `AutomationProperties.HelpText`. It is the only
remaining channel, and it would mean a second copy of every caption sentence in the same file, free
to drift from the first. The prose stays an adjacent text node, which is where a screen reader reads
it in document order anyway.

## C5. The Reconstructor covering test, redesigned rather than re-pointed

`AssertSameControlSequence_SwappedIdenticallyDescribedBrowsePositions_FailsNamingTheMismatch` proved
two things at once: that the order check is sensitive to a permutation, and — because it swapped two
of four controls that described *identically* — that it is sensitive in a way a description-based
comparison could not be. Naming the four Browse buttons removed the last identically-described pair
from the view, so the selector matched zero controls.

Checked before deciding: after the renames the Reconstructor's walk contains **no** identically
described pair in either navigation scope — every stop carries a distinct accessible name or x:Name.
That is the naming pass working, and it removes the test's premise rather than relocating it. The
brief suggested finding a real pair per suite; there isn't one here.

Split into two, so neither claim is dropped:

| Test | Claim |
|---|---|
| `AssertSameControlSequence_SwappedPositions_FailsNamingTheMismatch` (renamed) | positional sensitivity, against the REAL scope-B reverse walk |
| `AssertSameControlSequence_IdenticallyDescribedControls_AreDistinguishedByReference` (new) | reference-vs-description sensitivity, against a pair constructed to describe identically |

The new one is **stronger than what it replaces**: it asserts that a description-based
`Assert.Equal` genuinely *passes* on the swapped sequence before asserting the reference-based
comparison *fails* on it. The old test asserted only the second half and took the first on trust.

`ResolveExpectedStops_FixtureExpectsMoreThanExist_ThrowsNamingTheShortfall` had the same problem —
it appended a 5th `Button name="Browse"` and expected "expects 5, this window has 4". It now appends
a second copy of one of the real, distinct names and expects "expects 2, this window has 1": same
code path, same counted-multiset property, real strings.

Four doc comments in that file asserted "the four Browse buttons describe identically" as live
justification for a mechanism. All four are rewritten to say the mechanism is now kept as house rule
rather than forced — the class of stale-rationale defect §13's round 5 was about.

## C6. Announcements

Both live lines follow `SaveLogStatus` and the `SRSReconstructorView.ResultStatus` precedent
exactly: always in the tree, `LiveSetting="Polite"`, no `AutomationProperties.Name` (the announced
name IS the text), empty text renders nothing. The visible custom-packer Border stays
announcement-free — it toggles `IsVisible`, so it is not realized when its text arrives and there is
no transition for an AT to notice. That is asserted, not just commented: the test fails if someone
"fixes" it by putting `LiveSetting` on the banner.

Placement: the log-header row, in a `1*/1*/1*` Grid alongside `SaveLogStatus` — three fixed
proportions rather than SRSReconstructor's `1*/2*` since none of the three is the obvious primary.
`AllThreeLiveLines_KeepANonZeroShare_WithLongTextInEachAtOnce` puts long text in all three at once
and asserts each is arranged at non-zero width, which is the specific failure SRSReconstructor's own
comment documents. No focusable control was added, so no tab-order fixture moved.

**Two VM changes the brief did not anticipate, both required for the announcements to actually
fire:**

1. `ConfigAnnouncement` is new — §I6 is right that config outcomes were never started. Both commands
   reported only into `LogEntries`, which is deliberately not a live region.
2. **§I3's claim that the custom-packer warning needs "no VM change" is wrong.** It says "the VM
   already clears the warning at run start, which is what re-arms the empty→text transition". That
   clear is in `Reset()`, which the Beginner wizard calls — `ImportSRRAsync` does not. So importing
   two SRRs carrying the same warning text set an equal value, raised no change notification, and
   would have announced nothing the second time. `CustomPackerWarning = null` now sits beside the
   existing `HasImportedSRR = false` at the top of the command, with the same clear-first reasoning
   `OperationViewModelBase.SaveLogToFileAsync` records. It also fixes a smaller pre-existing bug:
   a failed import used to leave the previous SRR's warning on screen.

Both clears are pinned by `ReAnnounces`/`ClearsAPreviousCustomPackerWarning` tests, because a comment
saying "do not simplify this away" does not fail a build.

## C7. RED-first evidence

Five mechanisms were sabotaged simultaneously and the run checked to see exactly the matching tests
fail, then all five reverted and the suite re-run green:

| Sabotage | Went RED |
|---|---|
| TabItem's `Name` binding removed | `Reconstructor_PathsTab_AnnouncesItsNameAndItsNeedsAttentionState` |
| `CustomPackerStatus` given `IsVisible="{Binding HasCustomPackerWarning}"` | `CustomPackerWarning_…_NotTheVisibleBanner`, `AllThreeLiveLines_…` |
| `ConfigStatus` renamed away | `ConfigOutcome_AnnouncesThroughAnAlwaysInTreeLiveLine` |
| One legend `LabeledBy` removed | `Reconstructor_TriStateLegend_CheckBoxesAnnounceTheirCaption` |
| WinRAR Browse name reduced to "Browse" | `Reconstructor_PathPickers_…`, `ReconstructorAndWizard_…` |

7 of 14 red, each traceable to its own sabotage; 14/14 green after reverting.

Every expected name in the new tests is a **literal string written in the test**, and every control
is resolved by something OTHER than the name under test — a bound command reference, an x:Name, a
distinctive bound value, or structural position. The two -mt boxes are structurally identical, so
each is found by the value bound into it rather than by document order, which would pass silently if
the two bindings were ever swapped.

## C8. Tests added

| Test file | Count | Covers |
|---|---|---|
| `ReScene.Manager.Tests/AccessibleNamingTests.cs` | 11 | every rename, by literal; the 3.2.4 wizard correspondence; the Label-in-Name exception; the `Raw` platform fact |
| `ReScene.Manager.Tests/ReconstructorAnnouncementTests.cs` | 3 | live-line wiring, banner stays announcement-free, three-way width |
| `ReScene.App.Core.Tests/ReconstructorAnnouncementTests.cs` | 10 | config outcome strings + clear-first; failed-import clear; `PathsTabAccessibleName` values AND its notification from all four paths |

Plus, in `ReconstructorCompactTests`, one renamed and one new (§C5). No test was deleted.

## C9. Evidence

`-t:Rebuild` on all four projects, each **0 Warning(s), 0 Error(s)**, then `dotnet test --no-build`:

```
Manager   Passed!  - Failed: 0, Passed: 471, Skipped: 0, Total: 471
App.Core  Passed!  - Failed: 0, Passed: 722, Skipped: 0, Total: 722
```

Baselines Manager 456 / App.Core 712. Delta **+15 Manager** (11 naming + 3 announcement + 1 new
covering test) and **+10 App.Core**, matching §C8.

A first draft of this section said 470 and "+14". Both were written from a count of the test methods
I thought I had added, before the suite was run — the exact defect §10c's method note and the
ledger's counts-are-approximate-until-measured rule are about, so it is recorded rather than quietly
corrected. The numbers above are quoted from the run.

## C10. The sweep, run LAST

Run against the final tree, after the last edit of the round, per §10c's method note. Two of the
four counts in this section's first draft were also written before running and were both wrong;
these are from the run. Recursive over `ReScene.Manager/` and `ReScene.Manager.Tests/`, `*.cs` and
`*.axaml`.

**`Browse input file` — 1 survivor, and it must stay.**

```
ReScene.Manager.Tests/CreatorCompactTests.cs:1951   AutomationProperties.Name="Browse input file"
```

It sits inside `OldFullMarkup`, a deliberately FROZEN verbatim reconstruction of the pre-task
`CreatorView.axaml` (git blob `67aa5e8`) that the frame rig parses to compare today's render against
the pre-change one. Updating it would defeat its purpose. Harmless for what it does: an accessible
name is not rendered, so the pixel comparison is unaffected — and the frame-rig tests pass.

**`Avalonia.Controls.ScrollViewer` — 3 survivors, all historical prose**, each explaining what the
Paths TabItem *used to* announce and why it now announces something else: `ReconstructorView.axaml`'s
own note at the TabItem, `ReconstructorCompactTests`' fixture doc, and `AccessibleNamingTests`' test
doc. No live occurrence.

**`AccessibilityView` — 10 hits in 3 files, and NOT ONE is a live use.** `StylesTests.cs:140` is the
pre-existing note recording the same finding from the Expander side; `ReconstructorView.axaml:500`
is inside the legend comment explaining why it is not used; the remaining 8 are
`AccessibleNamingTests.cs`'s pinning test and its doc. The attribute appears in no shipped view's
markup, which is the point.

**The `TextBox name=""` census — the gate's item (b), measured on both trees:**

```
$ git grep -c 'TextBox name=\"\"' 34dcbba -- ReScene.Manager.Tests
34dcbba:ReScene.Manager.Tests/CreatorCompactTests.cs:2
34dcbba:ReScene.Manager.Tests/ReconstructorCompactTests.cs:12
34dcbba:ReScene.Manager.Tests/SRSCreatorCompactTests.cs:6
34dcbba:ReScene.Manager.Tests/SRSReconstructorCompactTests.cs:4
34dcbba:ReScene.Manager.Tests/SampleRestorerCompactTests.cs:4

$ git grep -c 'TextBox name=\"\"' -- ReScene.Manager.Tests
(no matches)
```

**28 → 0.** Every picker TextBox in every tab-order fixture now announces something. (28 fixture
LINES, not 28 controls — each control appears in two or three fixtures per suite.)

## C11. Not attempted — disclosed

1. **`ReconstructWizardBody`'s own custom-packer banner** has the identical defect: same
   `IsVisible`-toggled Border, same silent warning, on step 0. Not fixed. The Advanced tab's fix
   works because that view has a log-header row to host a live line away from the banner; the
   wizard's step 0 has no such neutral home, and putting one directly under the banner would render
   the same sentence twice on screen. It needs its own placement decision and its own tests. This is
   the same shape as §8's wizard pickup.
2. **Nine Browse buttons still announce the bare word "Browse"** — three each in
   `SampleRestorerView`, `SRSCreatorView` and `SRSReconstructorView`. MEASURED: `Content="Browse"`
   returns 3 per view across those three files, and all nine appear in the regenerated fixtures as
   `Button name="Browse" id=""`, so the count is of controls that genuinely announce nothing more
   than the word. This is §C2's deviation 5, restated here because a not-attempted list is where the
   next reader looks for inherited debt, not a scope paragraph. It leaves a **WCAG 3.2.4 asymmetry**:
   "browse for a file/folder for this field" now announces as "Browse for &lt;target&gt;" in the
   Reconstructor, the Creator and both wizards, and as an undifferentiated "Browse" in these three.
   Not a regression — they announced that before this batch too — but the batch widened the gap by
   fixing the others. Doing it needs nine new strings invented from each row's own visible caption
   (there is no wizard twin to copy from, which is what made the Reconstructor's four free), plus
   the fixture regeneration and the covering-test consequences in §C5 — SampleRestorer's,
   SRSCreator's and SRSReconstructor's `AssertSameControlSequence_SwappedIdenticallyDescribedBrowsePositions_…`
   tests all select on exactly these nine, and all three would lose their premise the way the
   Reconstructor's did.
3. **The wizard's TabIndex construction** — still out of scope, still ledgered at §8.
4. **`HelpText` on the prose-caption picker rows** — §C4's reasoning.
5. **No real screen-reader session.** Everything here is asserted through Avalonia's automation
   peers, which is the same API an AT calls but not the same thing as NVDA or Narrator actually
   speaking it. Gate item (e) remains user-skipped.
6. **Windows only**, as with everything in this workstream.
7. **`CreatorCompactTests`' own `AssertSameControlSequence_SwappedPositions_FailsNamingTheMismatch`
   has the weaker form** this round just fixed in the Reconstructor: it swaps two distinguishable
   controls and its doc says the reference comparison "must catch this swap exactly as readily as a
   description-colliding one" — true, but it does not prove the description-colliding case. It
   predates this batch and nothing here broke it, so it was left alone; the Reconstructor's new
   `AssertSameControlSequence_IdenticallyDescribedControls_AreDistinguishedByReference` is the
   pattern if anyone wants to strengthen it.

## C12. Concerns

1. **`Raw` being a no-op is a platform fact that could change.** The legend's `LabeledBy` treatment
   is the right call today and a slightly verbose one — the AT reads the caption twice, once as the
   checkbox's name and once as the adjacent text. If a future Avalonia honours `Raw`, pruning them
   is better. The pinning test fails at exactly that moment, which is the intent, but whoever sees
   it fail needs to read this section rather than just updating the assertion.
2. **`App name:` and `File inside ISO:` announce their colons.** Correct given `LabeledBy`, and the
   alternative (an explicit `Name` without the colon) trades a self-maintaining association for a
   duplicated string. Worth revisiting only if a real AT session finds it grating.
3. **The three-way `1*/1*/1*` split is narrower per line than the two-way it replaces.** At 700 DIPs
   each line gets roughly a third of the header's fill area, so long outcomes trim visually sooner.
   The announcement is unaffected (an AT reads `Text`, not the rendered glyphs), and the width test
   only proves non-zero, not comfortable.
4. **`Verify file path` reads slightly oddly.** It is the 2.5.3-correct choice given the caption says
   "Verify", but the better fix would be to change the visible caption to "Verification" and then
   use the natural name. That is a UI-copy change, out of scope here.
5. **This batch made the app LESS consistent about Browse buttons before it makes it more so.** The
   nine bare-"Browse" buttons of §C11.2 are the residue. Before item 2, "Browse" was the app's
   near-uniform (if uninformative) answer everywhere; now the Reconstructor, the Creator and both
   wizards say "Browse for &lt;target&gt;" and three views still say "Browse". A screen-reader user
   moving between them meets two conventions for one function, which is the failure mode WCAG 3.2.4
   describes, and it is worse than a uniform-but-poor name in exactly the way a half-finished rename
   always is. The judgement was that fixing four buttons for free (the wizard already had their
   names) beat leaving all thirteen unnamed, and that inventing nine more strings belonged in its own
   reviewed change rather than being tacked on unmeasured at the end of this one. That judgement is
   worth revisiting soon rather than at leisure — this is the kind of debt that stops looking like
   debt once everyone is used to it.
6. **The `PathsTabAccessibleName` notification test iterates four properties**; a fifth path added
   later without its `NotifyPropertyChangedFor` would leave the announced name stale behind the
   glyph and this test would not know to look for it.
7. **RESOLVED — this file is now under version control.** It was not when item 2 was committed:
   `.gitignore:66` ignores `.superpowers/`, so `e725154` carried the code and the tests but not this
   report — the exact mechanism §I1 records for the original `a11y-gate-report.md`, which "lived in
   the feature worktree's gitignored workspace, which was emptied after the worktree was retired"
   and had to be reconstructed from a conversation transcript. Escalated rather than force-added,
   because overriding a deliberate ignore is a repo-policy call. The controller's decision was to
   move the artifacts somewhere tracked instead: this file, `a11y-gate-report.md` and
   `derived-threshold-report.md` now live under `docs/superpowers/reports/`, with breadcrumb stubs
   left at the old `.superpowers/` paths for the other records that reference them. The concern is
   kept rather than deleted because the underlying hazard is not: anything else written into
   `.superpowers/` is still invisible to git and still one worktree cleanup from being gone.

---

# Package A — the gate's test-hardening trio (NEW-2, NEW-3, NEW-4)

Date: 2026-08-03. Base `main` @18b8907. One commit. Nothing pushed. **Test-only** — no view, no
ViewModel, no style was changed.

Three findings the gate raised against the suites rather than the app: tests that would keep passing
through a real defect. Each is fixed and each is BREAK-VERIFIED — the fix is only worth the diff if
the old form can be shown to miss something the new one catches.

## D1. What the three were

| | Where | The hole |
|---|---|---|
| NEW-2 | `ReconstructorCompactTests` splitter contrast | reads a logical property against assumed resource keys; cannot see deletion of the `:focus` style |
| NEW-3 | `ReconstructorCompactTests` reverse tab oracle | reverse expectation derived from the forward walk it is checking — self-referential |
| NEW-4 | `SRSCreatorCompactTests`, `SRSReconstructorCompactTests` | containment measured against the window rectangle only, ignoring clipping ancestors |

## D2. NEW-2 — the contrast test measured the markup, not the screen

The old test read `splitter.Background`'s logical colour and compared it against two ASSUMED resource
keys (`SurfaceBackground`, `PanelBackground`). Both halves measured what the markup is supposed to
say rather than what a user sees.

**The failure it could not detect, measured rather than argued.** Deleting the real
`GridSplitter:focus` style from `Styles.axaml` and re-running:

```
splitter.IsFocused = True          (still focused)
splitter.Background = Transparent  (base style's fallback)
OLD form: 13.73:1 vs tab strip, 15.31:1 vs log   -> PASSES the 3:1 bar
NEW form: 1.00:1                                 -> FAILS
```

`Transparent`'s `Color` is `#00FFFFFF` — white, with a zero alpha that a colour-only contrast
computation simply discards. Against this app's dark panes that computes as a *very good* ratio. So
the old test would have gone on passing, reassuringly, while the focus indicator had ceased to
exist. The new form samples the real rendered pixel and reports `1.00:1`: the splitter is now
indistinguishable from the pane behind it, which is the truth.

The style was restored immediately (`git diff` on `Styles.axaml` is empty) and the suite re-run
green. Method backported from `CreatorCompactTests.MeasureSplitterFocusContrast`, which fixed the
identical defect there: in-bounds check first, then sample the rendered pixel at the splitter's
centre and 3 DIPs above and below.

`Splitter_FocusVisual_UnpaintedSplitter_FailsTheCheck` is added as the permanent discriminating
case — `Opacity = 0` leaves `IsVisible`, `IsEffectivelyVisible`, the layout bounds AND the logical
`Background` all unchanged, and only the rendered pixel reverts. All four are asserted, because they
are exactly why the old property-reading form could not have failed.

**Promotion question, answered as asked:** kept LOCAL. `MeasureSplitterFocusContrast` now exists in
two suites and they are the same shape but not the same contract — the Creator's neighbours are its
stored-files grid and output section, this one's are the Paths/Options TabControl and the log, and
each doc explains its own geometry. What is genuinely shared is a four-line technique. The
containment helper is a different story: see D4.

## D3. NEW-3 — the reverse oracle checked the walk against itself

Both per-scope reverse walks were checked against `forwardOrder.Skip(k).Reverse()`. An oracle
derived from the thing it is testing cannot fail in the way it most needs to: a defect in the visual
tree moves the forward order and the expectation derived from it TOGETHER, so reverse "agrees" with
an order that is already wrong.

`ResolveIndependentExpectedOrder` now resolves every stop by an identifier that has nothing to do
with tab order — a bound command reference for the action buttons, an x:Name for the four path
TextBoxes, the sole `GridSplitter`, the settings `TabControl`'s own first item, authored `Content`
strings for the three help links and Auto-scroll. Forward and both reverse walks check against that
one authored list. The starting sentinel comes from it too, so "which control is first" became a
claim the reverse walk's boundary-landing assertion PROVES rather than a presumption baked into the
setup. The per-scope machinery is untouched — this view nests a second `TabControl` and no single
reverse walk can cross that boundary.

**Break-verified with a real permutation**, not a description of one:
`SelfReferentialReverseOracle_PassesAPermutedTree_WhereTheIndependentOracleFails` swaps the WinRAR
and Release picker rows in the live visual tree, then evaluates BOTH oracles against that broken
tree. The self-referential expectation still matches the reverse walk exactly — it passes on a
broken view, which is the whole finding. The independent expectation fails, naming the position, in
both directions.

**Scope, stated because it would be easy to overclaim.** At gate time the hole was reachable through
the forward check too: all four Browse buttons described identically, so the description fixture
could not tell a swap of two of them from no swap. Item 2's renames closed that particular door by
accident. The discriminating test therefore swaps whole ROWS, keeping the description multiset
intact at the pair level, and asserts the old oracle's blindness directly. The independence is worth
having regardless of whether today's view exposes the hole — the next repeated row template
re-opens it.

**One deletion, disclosed.** `ResolveExpectedStops` (and its covering test) converted a
description-based fixture back into live references, and existed precisely BECAUSE there was no
independent oracle. Every real caller moved to `ResolveIndependentExpectedOrder`, leaving the helper
alive only by the test that proves the helper works — dead scaffolding of the exact kind this chain
has repeatedly punished. Nothing was lost: its counted-multiset property guarded against a fixture
silently resolving a duplicated description down to one control, and the forward walk's
`Assert.Equal(fixture, forwardOrder.Select(Describe))` is exact whole-sequence equality that already
catches any divergence, duplicates included. It removes one test from the count.

## D4. NEW-4 — containment ignored every clipping ancestor

Both suites compared a control's two translated corners against the WINDOW'S OUTER RECTANGLE. That
false-passes anything hidden behind an intermediate `ClipToBounds` ancestor, and in these two views
that is not a corner case — the config band's `ScrollViewer` is row 1 and the pinned action band and
log occupy rows 2 and 3 BELOW it, so content scrolled past the band's own bottom edge lands in
window space that is still comfortably inside the window.

**Measured, at 700x450, with each band scrolled to the top:**

| View | band extent / viewport | controls the old check false-passed |
|---|---|---|
| SRSCreator | 240.0 / 127.0 | **41** |
| SRSReconstructor | 162.0 / 132.0 | **20** |

`OutputTextBox` is among them in both, and is what the new committed test pins: realized, effectively
visible, positive size, every corner inside the window — and completely hidden.

`ClipAwareContainment_CatchesAControlScrolledBehindTheBandClip_WhichTheWindowRectCheckMisses` (one
per suite) asserts BOTH halves, because either alone proves nothing: that the OLD form passes (so
this really is a false pass), and that the new one fails. The old form is reproduced inline as
`NaiveWithinWindowRectOnly` rather than described — a comment claiming what deleted code used to do
is not checkable.

**Promotion question, answered differently from D2 and deliberately so.** The geometry is delegated
to `CompactViewRig.IsFullyVisibleWithinWindow`, which already owns this exact cumulative-clip walk,
is already `internal`, and is the very algorithm the Creator's and SampleRestorer's local copies say
they "mirror". This is not a promotion — there is no new abstraction, the shared implementation
already existed and was simply not being used here. Copying a subtle 25-line geometry walk a third
and fourth time to satisfy a rule aimed at NEW shared code would be following the letter of the rule
against its purpose. What stays local is the diagnostics: the visibility and positive-size
pre-checks carry each view's own degenerate-control lesson and name the specific failure, which a
bare bool cannot. (The Creator's and SampleRestorer's own copies were left alone — they are correct,
and rewriting passing tests outside the gate's scope is not this package's business.)

## D5. Evidence

`-t:Rebuild` on all four projects, each **0 Warning(s), 0 Error(s)**, then `dotnet test --no-build`:

```
Manager   Passed!  - Failed: 0, Passed: 474, Skipped: 0, Total: 474
App.Core  Passed!  - Failed: 0, Passed: 722, Skipped: 0, Total: 722
```

Baselines Manager 471 / App.Core 722. Manager delta **+3 net**, which is **+4 added − 1 deleted**:

| Test | Finding |
|---|---|
| `Splitter_FocusVisual_UnpaintedSplitter_FailsTheCheck` | NEW-2 |
| `SelfReferentialReverseOracle_PassesAPermutedTree_WhereTheIndependentOracleFails` | NEW-3 |
| `ClipAwareContainment_…_WhichTheWindowRectCheckMisses` ×2 (SRSCreator, SRSReconstructor) | NEW-4 |
| *(removed)* `ResolveExpectedStops_FixtureExpectsMoreThanExist_ThrowsNamingTheShortfall` | NEW-3 |

App.Core is untouched — this package changed three test files and nothing else, which `git status`
confirms.

## D6. Concerns

1. **The NEW-2 sabotage was manual.** Deleting the `:focus` style, observing RED, and restoring it
   is evidence that the check works today; it is not a permanent guard. The committed
   `Opacity = 0` test is the permanent one, and it covers "painted but invisible" rather than
   "style deleted". A test that deletes a style at runtime would be better and is not obviously
   possible against a compiled `Styles.axaml`.
2. **Three rendered pixels are not a pane survey.** Both suites' contrast helpers sample the
   splitter's centre and 3 DIPs above and below. That proves the indicator is distinguishable from
   what is immediately adjacent along that line — a pane with a light region elsewhere would not be
   caught. Stated in the helper's own doc too.
3. **The NEW-3 permutation test mutates the visual tree directly.** It is the sharpest available
   simulation of a tree-level defect, but it is not the same as a markup change: `TabIndex` pins,
   `KeyboardNavigation` scopes and template-level ordering could all permute a walk in ways this
   particular swap does not model.
4. **`ResolveIndependentExpectedOrder` encodes the expected order by hand**, so it is only as good
   as the reading of the markup that produced it. It is checked against the measured fixtures and
   against two real walks in both modes, which is what makes a mistake in it loud rather than
   silent — but it is authored, not derived, and that is the point and the risk in one.
5. **The clip-aware helpers now exist in four suites in two forms** — two hand-copies (Creator,
   SampleRestorer) and two delegating to the rig (SRSCreator, SRSReconstructor). That is more
   consistent than before in behaviour and less consistent in shape. Converging the other two is a
   small, safe follow-up that was out of this package's scope.

---

# Package B — the nine bare-"Browse" buttons

Date: 2026-08-03. Base `main` @2800f23. One commit. Nothing pushed.

Closes §C11.2 / §C12.5 — the debt item 2 created by fixing four Browse buttons and leaving nine.

## E1. The census, re-verified on this tree first

Nine, three per view, every one with visible `Content="Browse"` and no
`AutomationProperties.Name`:

| View | Commands |
|---|---|
| `SampleRestorerView` | `BrowseSRRCommand`, `BrowseMediaDirectoryCommand`, `BrowseOutputDirectoryCommand` |
| `SRSCreatorView` | `BrowseInputCommand`, `BrowseMainFileCommand`, `BrowseOutputCommand` |
| `SRSReconstructorView` | `BrowseSRSCommand`, `BrowseMediaCommand`, `BrowseOutputCommand` |

The bare `Content` is what makes the shared phrasing SAFE here: WCAG 2.5.3 requires the accessible
name to contain the visible label, and every "Browse for &lt;target&gt;" contains "Browse". That is
the exact condition CreatorView's folder picker fails — its Content reads "Browse folder…" — which
is why it keeps its own name and always will. `AssertBrowseButton` asserts the Content alongside the
name, so if one of these nine ever gains a longer label the convention breaks loudly instead of
quietly violating a level-A criterion.

## E2. The names

Each target is phrased from that row's OWN visible caption subject, which is why SampleRestorer says
"directory" and SRSReconstructor says "file" for what is otherwise the same shape of control:

| Command | Name | Caption subject |
|---|---|---|
| `BrowseSRRCommand` | "Browse for SRR file" | "SRR File" |
| `BrowseMediaDirectoryCommand` | "Browse for media directory" | "Media Directory" |
| `BrowseOutputDirectoryCommand` | "Browse for output directory" | "Output Directory" |
| `BrowseInputCommand` (SRSCreator) | "Browse for sample file" | "Sample File" |
| `BrowseMainFileCommand` | "Browse for main file" | "Main file" |
| `BrowseOutputCommand` (SRSCreator) | "Browse for output path" | "Output" |
| `BrowseSRSCommand` | "Browse for SRS file" | "SRS File" |
| `BrowseMediaCommand` | "Browse for media file" | "Media File" |
| `BrowseOutputCommand` (SRSReconstructor) | "Browse for output path" | "Output" |

**Four surfaces now share "Browse for output path"** — CreatorView, the Create-SRR wizard,
SRSCreatorView and SRSReconstructorView — because all four pick where an output FILE is written.
`OutputPathPickers_ShareOneName_AndTheFolderPickerDeliberatelyDiffers` pins that against one literal
(never against each other, which would pass if they drifted together) AND asserts the Reconstructor's
own picker still reads "Browse for output folder". That difference is deliberate: it chooses a
DIRECTORY, and collapsing the two would be false consistency rather than 3.2.4 compliance.

## E3. Fallout, measured — and one thing the brief did not expect

**18 failures across 3 suites**, six per suite: five fixture-dependent plus the covering test.

The brief anticipated SampleRestorer's covering test losing its premise. **All three did** —
`SRSCreatorCompactTests` and `SRSReconstructorCompactTests` carry the same
`AssertSameControlSequence_SwappedIdenticallyDescribedBrowsePositions_…` test, each selecting on
`Button name="Browse" id=""` in its own view, and each dropped to zero matches.

All fixtures regenerated from measured walks (temporary dump per suite using that suite's own setup
and its own independent oracle, captured, removed) — never edited entry by entry.

## E4. The covering tests: two different answers, on purpose

The brief asked whether any identically-described pair remained per suite. Measured from the
regenerated walks, and the answer differs by view:

- **SampleRestorer STILL has one**: the SRS grid's per-row checkboxes both describe as
  `CheckBox name="Restore this sample" id=""` — same name from the shared cell template, no x:Name,
  different objects. So its test is **re-pointed, not redesigned**, to
  `AssertSameControlSequence_SwappedIdenticallyDescribedRowCheckboxes_FailsNamingTheMismatch`. The
  pair's indistinguishability is now ASSERTED in the test (equal `Describe`, non-equal reference)
  rather than assumed — the brief's explicit warning, and the thing that stops it hollowing out if
  the grid ever gains a per-row distinguisher.
- **SRSCreator and SRSReconstructor have none left.** Every stop in their measured walks now carries
  a distinct name or x:Name. So each follows the §C5 precedent: the positional half stays as
  `AssertSameControlSequence_SwappedPositions_FailsNamingTheMismatch` against the real walk, and the
  reference-versus-description half moves to a new
  `AssertSameControlSequence_IdenticallyDescribedControls_AreDistinguishedByReference` against a
  constructed pair.

A real pair is better evidence when one exists; a constructed one is honest when it does not.

**Per-suite, not shared — asked and answered.** `AssertSameControlSequence` is a PRIVATE helper
duplicated in each suite, so "this suite's ordering check compares by reference" is a per-suite claim
about a per-suite method. One shared test would prove it for whichever copy it happened to call and
leave the others free to drift to a description comparison undetected. Promoting the helper into the
rig would make one test correct and is a five-suite refactor belonging in its own change — recorded
as a follow-up rather than smuggled in here.

## E5. Evidence

`-t:Rebuild` on all four projects, each **0 Warning(s), 0 Error(s)**, then `dotnet test --no-build`:

```
Manager   Passed!  - Failed: 0, Passed: 478, Skipped: 0, Total: 478
App.Core  Passed!  - Failed: 0, Passed: 722, Skipped: 0, Total: 722
```

Baselines Manager 474 / App.Core 722. Delta **+4 Manager**: two in `AccessibleNamingTests`
(`SiblingViews_BrowseButtons_UseTheSharedConvention`,
`OutputPathPickers_ShareOneName_AndTheFolderPickerDeliberatelyDiffers`) and two constructed-pair
tests (SRSCreator, SRSReconstructor). The three covering tests were RENAMED, not added.

## E6. The sweep, run LAST

Run after the final edit. **The first draft of this section carried two counts written before the
grep was run, and one of them I then talked myself into "explaining" with arithmetic that did not
work. Both are replaced below by the run.** That is the third recurrence of the same defect in this
chain and it is recorded rather than quietly fixed.

**Named Browse buttons in shipped views — 23, per file:**

```
$ grep -rn 'AutomationProperties.Name="Browse' ReScene.Manager/Views/
CreatorView.axaml            3
SampleRestorerView.axaml     3
ReconstructorView.axaml      4
CreateSRRWizardBody.axaml    3
SRSReconstructorView.axaml   3
SRSCreatorView.axaml         3
ReconstructWizardBody.axaml  4
                            --
                            23
```

23 DECLARATIONS across 7 files, not 23 distinct buttons a user can meet: the Create-SRR wizard
re-declares the Creator's input pair and its output picker, and the Reconstruct wizard re-declares
the Reconstructor's four, so the same function is authored twice on two surfaces by design (WCAG
3.2.4 — and asserted, on both surfaces, against one literal). Every one of the 23 is now named;
before item 2 the count was 4.

**Stale-rationale sweep.** Nine hits for "three 'Browse' buttons" / "deliberately left alone"
language across the three suites. Eight are past-tense records of what those buttons USED to be,
which is what a future reader needs to understand why the covering tests have the shapes they do.
One was a live claim — `SRSCreatorCompactTests`' covering-test doc still said it swaps "two of the
three identically-described Browse positions" when it no longer does — and is rewritten. The ninth
hit is in `CompactHeightBehaviorTests` about `e.Handled` and is unrelated.

**Visible labels, unchanged:** `grep -c 'Content="Browse"'` over the views is unchanged by this
package. No visible label moved — only accessible names were added, which is what keeps WCAG 2.5.3
satisfied for all nine.

## E7. Concerns

1. **Nine new strings were invented**, where item 2's four were adopted verbatim from an existing
   surface. Each is derived from a visible caption rather than chosen freely, and each is pinned by
   a literal assertion, but there is no second surface to cross-check them against the way the
   Reconstructor's four had the wizard. If any reads badly to a real screen-reader user, only a real
   session will find it — gate item (e), still skipped.
2. **"Browse for main file" is the weakest of the nine.** Its row's caption is "Main file (optional
   — populates MatchOffset, matches pyrescene's -c flag)", so the subject is clear but the field's
   PURPOSE is not conveyed by the name alone. The row's caption remains adjacent text, which is
   where a screen reader reads it, but this one leans on that more than the others do.
3. **SampleRestorer's covering test now depends on the grid's cell template staying
   undifferentiated.** That is a genuine coupling — a future per-row accessible name (say
   "Restore sampleA.srs", which would be an accessibility IMPROVEMENT) would break it. The test
   fails loudly with an explanation rather than passing vacuously, which is the best available
   outcome, but whoever makes that improvement will have to redesign this test at the same time.
4. **The app is now consistent about Browse buttons for the first time**, which removes §C12.5's
   concern rather than mitigating it: thirteen buttons, one convention, one documented exception.
   The exception (CreatorView's folder picker) is the only place a speech-input user meets different
   words for a similar action, and it is different because the visible label is different.
