# Derived thresholds — implementation report

Date: 2026-08-02. Branch: `main`. Platform: Windows 11 (Linux/macOS proof comes from CI).

Replaces the five per-view compact-mode threshold constants with a switch height each view derives
from its own measured content, and reworks the tests around an invariant that is
platform-independent by construction rather than by calibration.

---

## 1. The problem, restated with measurements

The Reconstructor's expanded floor measures **419** inner DIPs on Windows against a threshold of
**421** — two DIPs of headroom. On Linux CI the same content measures **438**, i.e. 17 DIPs *above*
its own switch point, and three tests fail there. That is not a test artifact: any window height
between 421 and 438 renders expanded mode with content the window cannot fit, which is precisely
the clipped-and-unreachable state the feature exists to eliminate. Font metrics differ per
platform; a constant cannot follow them. Nor can a constant follow a floor that grows at runtime as
conditional rows appear.

---

## 2. Design decisions

### 2.1 The floor rule: measure what varies by platform, author what is design intent

`Effective threshold = max(explicit minimum, measured expanded floor + 20)`.

The floor is a sum over the root Grid's rows, each row being one of two kinds:

- **GIVABLE** — content can scroll, so the floor owes only the minimum the design insists on
  seeing. Qualifies either as a **Star row** (gives by construction, owed its `MinHeight`) or by
  declaring an **`ExpandedMinHeight`** on its `CompactRowSize` (owed exactly that). The declaration
  wins over the row's kind: it is the more specific statement, and the only one available for the
  three-band views' config band, which is a plain Auto row at expanded size.
- **FIXED** — chrome shown whole or not at all. Pixel rows contribute their height; Auto rows the
  tallest desired height among their children including margins. This is the part that moves with
  the platform's fonts, so this is the part that must be measured.

Counting a scrollable band at content height is not merely pessimistic, it **diverges**. Measured
before the fix, with the naive rule:

| View | Host height | Naive floor | Naive threshold |
|---|---|---|---|
| SampleRestorer | 536 | 532 | **552** (above the host) |
| Creator | 721 | 717 | **737** (above the host) |

Both views cap their config ScrollViewer's `MaxHeight` to the room left over, so the band's content
height is a function of the current window height — the floor chases the height it is compared
against and no window is ever tall enough.

### 2.2 A band is only givable if something actually makes it give

This is the load-bearing distinction and it is **not** uniform across the three-band views:

- **Creator, SampleRestorer** — each caps its config ScrollViewer's `MaxHeight` on every layout
  pass to `root height − chrome − pinned band − log minimum − slack` (per-view code-behind, already
  shipped). That cap is what makes "this band can scroll away the difference" true, so both declare
  an `ExpandedMinHeight`.
- **SRSCreator, SRSReconstructor** — no cap. At expanded size their config row is a plain Auto row
  that takes its full desired height and does not give, so its **measured content height genuinely
  is** what the floor owes it. Neither declares an expanded minimum.

Declaring one on a row that cannot give would move the switch point below the height the row's
content actually needs. That is the failure mode the sweep test catches (§4.2).

### 2.3 Help-state reconciliation

The expanded floor is unconditional — no Help variant. Justification: the donation rule is a
compact-mode mechanism, and `GetHelpOpen` is false throughout expanded mode by construction
(`RecomputeHelpOpen` requires `IsCompact`). Expanded mode renders the Help body flat, expanded and
unconstrained — the largest it ever is — and the floor already carries that cost as measured chrome
in row 0. So the expanded floor is both Help-state-correct and conservative without a second set of
minimums. Pinned by `HelpOpenDonationMinimums_NeverEnterTheExpandedFloor`.

### 2.4 Capture, and two complementary triggers

Both were needed; each was found necessary by an observed failure, not by reasoning ahead.

1. **Post-apply settle pass** (`Evaluate`, posted at `Loaded`). Fires after any pass that leaves the
   view expanded and applied something — a restore, or the first evaluation at normal height where
   flat mode has just forced the Help body open while the capture ran before the body existed.
2. **`LayoutUpdated` recapture** (`RecaptureFloorAfterLayout`). Continuous, for changes the behavior
   does *not* make: content arriving, prose rewrapping, a font growing. None of these resize the
   root — its height is the window's to decide — so none raise a bounds change.

Neither subsumes the other. I removed (1) after adding (2), and
`RestoreThatNoLongerFits_ReturnsToCompact_AndThenRests` failed: in a synthetic root where the
compact class matches no style, a restore invalidates no layout, so `LayoutUpdated` never fires and
the restore is never re-validated. Re-validating a restore must not be conditional on the layout
system having had an opinion about it. Conversely, without (2),
`DerivedThreshold_RisesWithUnscrollableContent_WhereAConstantWouldNot` failed — content growth with
no bounds change was never noticed.

Efficiency: only a *grown* floor can change the verdict from expanded, so (2) queues an evaluation
in that case alone. The ordinary layout pass costs one row walk.

### 2.5 Anti-flap, by construction

A floor that grew while compact is invisible until the expanded layout is back, so a restore *can*
turn out to be wrong. When it does, re-validation returns the view to compact — and the
newly-measured floor has raised the threshold **above the very height that produced the failed
restore**, so restoring again would need a strictly greater height. One flip, then rest.

### 2.6 `Threshold` survives as an optional minimum; derivation is not opt-in

The attached property still binds, but only upward. Derivation happens whenever the behavior is
attached and the root is a Grid — deliberately *not* gated on `Enabled` — because an invariant a
caller can decline is not one. `Enabled` exists solely as an attach trigger for a view that names no
minimum (`Threshold`'s default is already NaN, so assigning NaN raises no change). No shipped view
sets a minimum.

`GetEffectiveThreshold` is the new read-only accessor. No per-view switch height is written down
anywhere in the test suite.

---

## 3. Authored minimums, per view

Back-derived from the design doc's own per-view accounting:
`(old constant − 20 margin) − measured chrome − measured pinned band − log minimum 80`, rounded.

Measured decomposition on Windows (worst-case content, inner width 676):

| View | row 0 chrome | row 1 config | row 2 pinned | row 3 log | Authored `ExpandedMinHeight` |
|---|---|---|---|---|---|
| SampleRestorer | 47 | givable | 68 | 80 (Star min) | **320**  (=515−47−68−80) |
| Creator | 47 | givable | 68 | 80 (Star min) | **500**  (≈505, rounded down) |
| SRSCreator | 47 | 296 measured | 68 | 80 (Star min) | none |
| SRSReconstructor | 47 | 213 measured | 96 | 80 (Star min) | none |
| Reconstructor | 85+34+43+39+8 measured | — | — | 130 + 80 (Star mins) | none |

Resulting switch points:

| View | Derived (Windows) | Old constant | Drift |
|---|---|---|---|
| Reconstructor | **439** | 421 | +18 |
| SRSCreator | **511** | 520 | −9 |
| SRSReconstructor | **456** | 450 | +6 |
| SampleRestorer | **535** | 535 | 0 |
| Creator | **715** | 720 | −5 |

The Reconstructor's +18 is the honest correction: its floor really is 419 on Windows and 421 left
two DIPs, which is how Linux's 438 ended up below the switch point. Both authored minimums are
comfortably below their band's natural content (Creator 500 vs extent 688; SampleRestorer 320 vs
extent 488), so both bands genuinely give.

---

## 4. Test changes

### 4.1 New tests (17 net; Manager 430 → 447)

**`CompactHeightBehaviorTests` (+11)** — a new `DerivedHost` rig with no explicit threshold
(chrome Auto 40 / body Auto caller-sized / tail Star min 60):

| Test | Guarantee |
|---|---|
| `DerivedThreshold_IsTheMeasuredFloorPlusMargin` | the arithmetic, stated once |
| `DerivedThreshold_TracksAGrowingFloor_WhereAConstantWouldNot` | RED-first; content growth moves the switch and flips the mode |
| `ExplicitThreshold_BelowTheDerivedFloor_IsOnlyAMinimum_AndDoesNotBind` | a caller cannot hold a view expanded below its floor |
| `ExplicitThreshold_AboveTheDerivedFloor_StillGoverns` | the other half of "minimum" |
| `GivableRow_ContributesItsAuthoredExpandedMinimum_NotItsContentHeight` | declared givable rule, incl. stability as content grows |
| `StarRow_ContributesItsMinimum_NotItsContentHeight` | structural givable rule (what Reconstructor relies on) |
| `HelpOpenDonationMinimums_NeverEnterTheExpandedFloor` | §2.3 |
| `DerivedThreshold_Hysteresis_RestoreOnlyAtDerivedPlusTwelve` | hysteresis applies to the derived value |
| `RestoreThatNoLongerFits_ReturnsToCompact_AndThenRests` | anti-flap: exactly 2 class changes, then still across 5 further turns |
| `FreshNormalInstance_FloorIncludesWhatTheFirstLayoutRevealed` | the settle pass (§2.4 case 1) |
| `NoExplicitThreshold_AndNoMeasurableFloor_LeavesTheViewAlone` | the NaN "no opinion" path — **previously untested** |

**Per-view (+6)** — `Invariant_ActiveModeFits_AtEveryHeightAroundTheSwitchPoint` on all five, plus
`DerivedThreshold_RisesWithUnscrollableContent_WhereAConstantWouldNot` on the Reconstructor (the
view whose CI failure motivated the change; grows the warning row, which is chrome and therefore
genuinely raises the floor).

### 4.2 The centerpiece invariant

`CompactInvariantRig.AssertActiveModeFitsAroundSwitchPoint` sweeps **fresh** instances (never a
resized ladder — hysteresis is restore-only, so a resized instance's mode is path-dependent) at
heights derived from `ProbeSwitchPoint`: finely at 6-DIP steps across ±36, then coarsely at 60-DIP
steps out to +396. At each height it asserts the *active* mode renders without clipping
(`AssertArrangesWithin` + `AssertNoAlwaysVisibleDescendantIsClipped`), and finally that both modes
actually occurred — an all-compact or all-expanded band would pass while proving nothing.

No height and no verdict in it is a platform-calibrated number, which is what makes it
platform-independent by construction: a platform needing 40 more DIPs moves the switch point and
the swept band together, and the same assertion describes the same promise.

The coarse far leg subsumes what the two hardcoded-height theories covered (the expanded-mode cap
regression); those theories are kept as well, converted from absolute heights to offsets above the
switch point, since they additionally assert specific named controls.

### 4.3 Rewritten, with every prior guarantee preserved

| Change | Was | Now |
|---|---|---|
| `private const double Threshold = NNN` (×5) | hardcoded per view | `static double Threshold => _threshold.Value`, a `Lazy` over `ProbeSwitchPoint`. Every existing `Threshold`/`ExpandedInner` usage compiles unchanged and now means the derived value |
| `ExpandedInner = Threshold + 1` | constant | `Threshold + ExpandedHeadroom` (60), clear of hysteresis |
| `Invariant_ExpandedModeFloor_UnderThreshold` (×5) | `floor < 421` etc. | renamed `..._UnderDerivedThreshold`; `floor < GetEffectiveThreshold(root)` |
| `RenderedMatrix_FreshAtThresholdExactly/PlusOne` (×5) | hardcoded heights | unchanged source, now exercising the derived switch point |
| `Invariant_ExpandedMode_NeverClipsAcrossUnsafeHeightRange` (Creator, SampleRestorer) | `[InlineData(721.0)]`… | `[InlineData(1.0)]`… as offsets above the switch point; named-control assertions kept verbatim |
| Creator shrink-ladder test | `[850, 800, 750, ExpandedInner, Threshold, …]`, host at `900.0` | fully derived ladder + host at `Threshold + 185`; **fixed a real defect introduced by derivation** — the mixed ladder became non-monotonic (750 then 775), so a "continuous shrink" test had a growth step in it. Now asserts strict monotonicity |
| Compact-exit resizes (`window.Height += 250/420`, ×5) | fixed deltas leaving 12–22 DIPs of margin over the derived thresholds | `(Threshold + 12 + ExpandedHeadroom) − CompactInner` |
| `CompactInvariantRig.MeasureFloor` | row-aware, unaware of `ExpandedMinHeight` | honours authored expanded minimums, guarded to expanded mode only (in compact the rows are already Star with compact minimums) |
| `SmallWindowBoardTests.AssertNoDescendantIsClipped` | private duplicate | promoted to `CompactInvariantRig.AssertNoAlwaysVisibleDescendantIsClipped`; board test uses the shared one |
| `MinInvariantMethodsPerView` | 3 | **4** — the sweep is the centerpiece and a view quietly losing it would leave the other three passing |
| `ScrollReachabilityTests` (both facts) | `Assert.Contains("Options", scrollable)`, `Assert.Contains("RAR Reconstruction", …)`, `scrollable.Count >= 3` | overflow set derived from measurement; `scrollable.Count > 0`; every overflowing page still must scroll to its true end. Reconstructor host height now derived from `GetEffectiveThreshold`; stale comment citing TabControl MinHeight 220 / log 140 removed |

Untouched, as required: frozen-generation, no-focus-theft and continued-resize recheck semantics;
`RunStagedRecovery` / `RelocateFocusIfNeeded` signatures; the whole reflection surface
(`IsObscured`, `GetClipVisibility`, `CreateResizeRecheckCallback`, `CreateRecoveryCallback`,
`RelocateFocusIfNeeded`, `MaxBringIntoViewAttempts`, `_states`, `State.Generation`). All 44
pre-existing `CompactHeightBehaviorTests` pass unmodified — their rig carries an explicit
`Threshold = 300` that its derived floor (~210) never reaches, so they pin the explicit path
exactly as before.

---

## 5. Evidence

### RED observed (not asserted — actually run and seen to fail)

1. **The constants were wrong.** Wiring the five views to derivation, before any test rework: 6
   failures, all in constant-pinned tests —
   `ReconstructorCompactTests.RenderedMatrix_FreshAtThresholdExactly/PlusOne`,
   `SRSReconstructorCompactTests.Invariant_ExpandedModeFloor_UnderThreshold` and its two
   `RenderedMatrix_*` siblings, and `SmallWindowBoardTests.EveryTaskView_HasThresholdInvariantTests`
   (which re-runs the third). 424 passed.
2. **The sweep discriminates.** Throwaway sabotage (applied, run, reverted) declaring SRSCreator's
   uncapped config row givable at 100:
   `Invariant_ActiveModeFits_AtEveryHeightAroundTheSwitchPoint` failed with
   `SRSCreator at inner height 315 in EXPANDED mode (its own switch point is 315.0): ScrollViewer
   bottom 337.0 exceeds 315`. This is exactly the "declared givable but cannot give" defect §2.2
   warns about.
3. **Both capture triggers are load-bearing.** With only the settle pass,
   `DerivedThreshold_RisesWithUnscrollableContent_WhereAConstantWouldNot` failed. With only
   `LayoutUpdated`, `RestoreThatNoLongerFits_ReturnsToCompact_AndThenRests` failed at the
   post-restore assertion (traced: threshold stayed 270 with the body's `DesiredSize` at 400 —
   the restore invalidated no layout, so no recapture ever ran).

### GREEN

Forced rebuild of `ReScene.Manager`, `ReScene.Manager.Tests` and `ReScene.App.Core.Tests`
(`-t:Rebuild`), each **0 Warning(s), 0 Error(s)**, then `dotnet test --no-build`:

```
Manager   Passed!  - Failed: 0, Passed: 447, Skipped: 0, Total: 447
App.Core  Passed!  - Failed: 0, Passed: 712, Skipped: 0, Total: 712
```

Baselines Manager 430 / App.Core 712. Delta: **+17 Manager** (11 behavior + 5 sweeps + 1 per-view
derived-threshold test), App.Core unchanged.

---

## 6. Concerns and disclosures

1. **Linux/macOS is unproven here.** I am on Windows; the whole point is that the other platforms
   differ. The new tests are designed to be platform-independent by construction (every height
   derived, every verdict about rendered geometry), but the actual Linux numbers — and whether the
   three previously-failing tests now pass there — are CI's to confirm. Expect the derived
   thresholds to be *larger* on Linux, and the compact-floor checks against the 307 bound are
   unchanged and untouched by this work, so if anything fails there it will most likely be one of
   those, not the new sweep.

2. **`ProbeSwitchPoint` hosts an extra window per test class** (once, `Lazy`-cached; assembly
   parallelization is already disabled). Suite time went 19s → 23s, mostly the five sweeps
   (~95 hosted windows) and their re-invocation by the board's existing cross-view guard, which
   deliberately runs every view's `Invariant_*` a second time.

3. **The two per-view expanded-mode caps are now load-bearing for the floor rule**, not just for
   their own view's rendering: Creator's and SampleRestorer's `ExpandedMinHeight` declarations are
   only *true* because those caps exist. I documented the dependency in `CompactRowSize`'s remarks,
   in both views' constructors and in the spec amendment, but nothing mechanically enforces it —
   deleting a cap while leaving the declaration would be caught by the sweep, which is the
   mitigation, but not at the point of the mistake.

4. **`ExpandedHeadroom = 60` is a judgement call.** It has to exceed the restore slack (12) with
   room for platform variation; 60 is comfortable without hosting views at sizes that stop
   resembling the small windows this feature is about.

5. **`Invariant_ActiveModeFits` uses fresh instances only.** It says nothing about resize paths;
   those remain covered by the hysteresis, rapid-crossing and continued-shrink tests, which are
   unchanged.

6. **Not done, deliberately (scope):** SRSCreator and SRSReconstructor could be given the same
   expanded-mode cap the other two have, which would let their config bands be declared givable and
   lower their thresholds. That is a behavior change to shipped views with no defect motivating it —
   their content is small and fixed, which is why they never needed a cap — so I left it alone.

---

# Round 2 — failed-restore focus race (2026-08-02)

Codex review of `8ae80c6..7170072` returned REVISE with one load-bearing finding: the restore
re-validation was posted *before* the restore's staged focus recovery, so a failed restore could
strand or silently clear keyboard focus.

## 7. The defect, confirmed

Both posts sat at `DispatcherPriority.Loaded` and same-priority jobs run in post order
(verified empirically: `loadedA -> loadedB -> background`), so the re-validation ran first.
On a failed restore that compounds three ways:

1. The restore hides the compact-only controls it had just been showing (flat-mode styles hide the
   Help header toggle), which **clears focus** when the toggle held it.
2. The re-validation's re-compaction then runs as a genuine transition, but its own
   `CaptureFocusedElement` finds **nothing** — focus is already gone.
3. It bumps `State.Generation`, so the restore's queued recovery — the only job holding a capture
   of what the restore hid — rejects itself as stale via `IsSuperseded`.

Net: the behavior cleared focus itself and left it cleared, with the one job that could have
repaired it invalidated. Invisible to `RestoreThatNoLongerFits_ReturnsToCompact_AndThenRests`,
which asserts classes only.

**RED observed** (both new tests, against the shipped ordering — the pre-fix order was temporarily
restored and re-run to confirm):

```
Failed ReconstructorCompactTests.FailedRestore_FromTheFocusedHeaderToggle_EndsCompactWithFocusOnTheHeaderToggle
  a failed restore left focus cleared: the trail was [<none>, <none>, <none>, <none>, <none>, <none>, <none>, <none>]

Failed CompactHeightBehaviorTests.FailedRestore_NeverLeavesFocusCleared_AndLandsOnTheRestoreTarget
  a failed restore left focus cleared: the trail was [<none>, <none>, <none>, <none>, <none>, <none>, <none>, <none>]
```

Not a transient dip — focus was null for the entire eight-drain trail and stayed null.

## 8. Design choice: order the recovery ahead of re-validation

Of the two shapes offered, I took the first — post the staged recovery **before** the
re-validation — because it satisfies the second as a consequence, and because it puts the repair
where the information is.

The restore's recovery is the only job holding a capture of the element the restore hid. Letting it
run first means:

- Nothing stale is ever superseded: the recovery completes before the re-compaction bumps the
  generation, so `IsSuperseded` never fires on it.
- The re-compaction becomes an **ordinary transition with its own correctly-timed capture** — by
  the time it runs, focus has been placed somewhere real by the recovery, so it captures a settled,
  genuinely-focused element and stages its own recovery through the normal path. That is exactly
  codex's second shape ("superseding is fine only if the superseding transition provides its own"),
  obtained without a special case.

Implementation is a source reorder of the two posts inside `Evaluate`, with the `captured is null`
early-return converted to an `if (captured is not null)` block so the re-validation still runs when
there was nothing to capture. The ordering requirement is stated explicitly at the post site
("POSTED LAST, AND THAT ORDER IS LOAD-BEARING — do not move this above the recovery post") with the
three-way failure it prevents spelled out, since the constraint is invisible from the code alone.

I considered posting the re-validation at `DispatcherPriority.Background` instead, which orders it
after all pending `Loaded` work by priority rather than by queue position. Rejected: `Background`
sits below `Input`, so a sustained input stream (a live resize drag — precisely when restores
happen) could defer the re-validation indefinitely. Queue order at the same priority keeps both
jobs in the same dispatcher turn.

Untouched, as required: the no-focus-theft precondition (`CaptureFocusedElement` still gates every
capture on focus being in-root), frozen-generation discipline (`CreateRecoveryCallback` /
`CreateResizeRecheckCallback` unchanged), `RunStagedRecovery` and `RelocateFocusIfNeeded`
signatures, and the whole reflection surface.

## 9. Tests added (2; Manager 447 → 449)

**`ReconstructorCompactTests.FailedRestore_FromTheFocusedHeaderToggle_EndsCompactWithFocusOnTheHeaderToggle`**
— the race on the real view with codex's named subject. Focus the compact-only Help header toggle,
grow the warning row while compact (chrome, so it genuinely raises the floor), raise the height to
`staleThreshold + 12` so the restore fires and then fails re-validation. Asserts the end state is
compact, that the re-measured threshold really is above the height that produced the restore (so
this is a *failed* restore and not a trivially-passing one), and that focus landed **specifically**
on the header toggle — where the rules put it: the restore's recovery relocates the hidden toggle to
the wired `RestoreFocusTarget` (`WindowsPackLink`), and the re-compaction then finds that link
inside the collapsed Help body and hands off through the compact direction's target, which is the
toggle again. The focus trail is recorded per dispatcher drain and asserted non-null from the third
entry on, so a run that ends valid but passed through a dead window still fails.

**`CompactHeightBehaviorTests.FailedRestore_NeverLeavesFocusCleared_AndLandsOnTheRestoreTarget`**
— the sibling to the anti-flap test, asserting focus outcomes alongside class outcomes as codex
asked. Synthetic rig reproducing the compact-only mechanism with the production style pattern (base
style hides the control, a class-scoped style under the root's own `compactHeight` reveals it), and
covering the **other** fallback direction: here the restore target stays usable in compact, so the
re-compaction captures it, finds it settled, and leaves it there. Between the two tests both landing
outcomes of the chain are pinned.

`RestoreThatNoLongerFits_ReturnsToCompact_AndThenRests` keeps its class/threshold scope and now
carries a cross-reference explaining what it deliberately does not cover and which test does.

While building the synthetic rig I hit — and fixed — a rig-honesty problem worth recording: with
the restore target in the trailing star row, a grid whose floor exceeds its height overflows
downwards and clips the target out at exactly the moment the failed restore needs it, so focus
correctly fell through to the chain's root terminal. That is a valid landing but not the one the
real views have, so the rig now places the target in its own always-visible band above the growable
body. The first version's failure ("focus should have settled on the wired RestoreFocusTarget, not
Grid") was a bad fixture, not a product defect.

## 10. Evidence

Forced rebuild of `ReScene.Manager`, `ReScene.Manager.Tests` and `ReScene.App.Core.Tests`
(`-t:Rebuild`), each **0 Warning(s), 0 Error(s)**, then `dotnet test --no-build`:

```
Manager   Passed!  - Failed: 0, Passed: 449, Skipped: 0, Total: 449
App.Core  Passed!  - Failed: 0, Passed: 712, Skipped: 0, Total: 712
```

Round-1 baselines Manager 447 / App.Core 712. Delta **+2 Manager**, App.Core unchanged.

## 11. Concerns (round 2)

1. **The ordering constraint is enforced by a comment and two tests, not by the type system.** Both
   posts are plain `Dispatcher.UIThread.Post` calls at the same priority; nothing prevents a future
   edit from reordering them. The two new tests fail loudly if it happens (verified — that is
   exactly how the pre-fix order was re-confirmed RED), which is the mitigation, but the constraint
   is not visible at the call site without reading the comment.

2. **Same-priority FIFO is relied on deliberately.** Verified empirically in this Avalonia version
   (11.3.x) rather than assumed. It is the defining property of a priority queue, so I am
   comfortable with it, but it is a dependency on dispatcher behaviour rather than on anything the
   API documents.

3. **The focus trail asserts non-null from the third drain onward, not from the first.** The first
   two entries can legitimately be null: that is the window between the restore hiding the focused
   control and its recovery running, which is the transient the staged-focus contract exists to
   repair rather than a defect. A regression that *lengthened* that window to exactly two drains
   would slip through; a regression that leaves focus cleared, which is the defect class here, does
   not.

4. **Still Windows-only verification.** Both new tests derive every height from the behavior's own
   switch point, so they should behave identically on Linux/macOS, but as with round 1 that is CI's
   to confirm.

---

# Round 3 — first-attach flash (2026-08-02)

User-reported: clicking into a tab whose view belongs in compact mode shows one frame of the
expanded layout before compact applies. Repro context given: SRR Creator at ~685 inner height
against a ~715 switch point.

## 12. What I measured, and where it differs from the brief

The brief described the mechanism as affecting **every tab revisit** and prescribed persisting the
verdict and floor across detach/reattach. I reproduced before implementing, and the measurement
does not support that part:

```
=== initial (tab selected) ===
  after HostAt                        classes=[compactHeight] bounds=685 thr=715 row1=star1/min110
=== switch AWAY to tab 0 ===
  immediately after SelectedIndex=0   classes=[compactHeight] ... attached=False
=== switch BACK to tab 4 ===
  SYNCHRONOUS after SelectedIndex=4   classes=[compactHeight] ... attached=True
```

**Per-control state and the style class already survive detach.** `_states` is a
`ConditionalWeakTable` keyed by the control, and `Classes` live on the control itself, so a view
that leaves the tree and comes back still knows its verdict, its captured floor and its applied row
values. Nothing needed to be added for that, and I did not add it — the brief's own point 3 warns
that retaining state across detach is exactly what earlier work cleaned up, so introducing a second
memory mechanism for a property the tree already has would have taken on that risk for nothing.

What *is* real is the **first** visit to a tab, which my rig initially missed because it selected
the tab at `Show()`. A tab's content is not laid out until first selected, so that selection is the
view's first ever layout:

```
=== never visited ===
  before first selection              classes=[] bounds=0 thr=NaN attached=False
=== FIRST click into the tab ===
  SYNCHRONOUS after SelectedIndex=4   classes=[] bounds=0 thr=NaN attached=False
  after drain 0                       classes=[compactHeight] bounds=706 thr=715 row1=star1/min110
```

So: **first visit flashes; later visits do not.** In a session where the user visits several tabs
that all belong in compact, every one of them flashes the first time, which I think is what the
report describes. I have not been able to construct a revisit that flashes, and I would rather say
so than implement against a premise I could not confirm. If the real app does flash on revisits,
this fix does not address that and the repro would need pinning down further — but the RED evidence
below shows the first-visit flash is real on the actual view at the actual size.

## 13. The fix: decide in line on the first bounds of each attachment

`HookBounds` arms `State.AwaitingFirstBounds` on every (re)attachment. While it is set, a bounds
notification is evaluated **in line** instead of through `QueueEvaluate`; the first evaluation that
sees a real height clears it, and every subsequent bounds change takes the existing coalesced path.

Why the first bounds notification specifically: bounds are assigned during the layout pass, so a job
posted from that notification runs only after the pass has finished — by which time the frame built
from it can already have been presented, in the view's default (expanded) shape. The first bounds
notification is the last moment at which the verdict can still be part of the frame. It is also the
*first* moment at which the derived model can reach a verdict, because the floor is read from
`DesiredSize` and measure has completed by then. That is the honest answer to the brief's "say so if
the floor cannot be known pre-first-frame": it can be, on the same notification, because measure
precedes arrange.

The floor read at that instant is very slightly low — `ApplyHelpExpanderDirection` has not yet
forced the Help body open, so the chrome row is measured without its prose (~12 DIPs on Creator).
It does not change the verdict at any height that is not within ~12 DIPs of the switch point, and
the settle pass and the `LayoutUpdated` recapture correct it immediately afterwards.

Deliberately one-shot per attachment. Mutating classes and row definitions from inside a layout pass
costs an extra pass — the right trade once, to avoid a visibly wrong frame, and the wrong one for
every frame of a resize drag, where coalescing is what keeps the cost bounded and no frame is wrong
long enough to see.

**Lifecycle:** nothing new is retained across detach, so the phantom-root-Tab-stop guarantees
(detach-before-run no-op, teardown-during-pass grant revert, `LostFocus` reset) and the no-theft
precondition are untouched — verified by re-running them unmodified (§15). The round-2 ordering
(recovery posted before re-validation) is untouched.

## 14. Tests added (5; Manager 449 → 454)

The executable form of "no flash": a frame is built from a completed layout pass, so record every
pass as `(height the pass gave the root, whether the class was on it)` and require that no pass with
a real height carried the wrong mode. That is a direct statement about what can appear on screen,
rather than a proxy for it.

| Test | Covers |
|---|---|
| `FirstAttach_ReachesItsVerdictInTheSameLayoutPassThatFirstSizesTheView` | the flash itself, synthetic |
| `Reattach_IntoAWindowThatGrew_RestoresWithoutPresentingACompactFrame` | remembered compact + window grew → restores, no compact frame at the larger size |
| `Reattach_IntoAWindowThatShrank_CompactsWithoutPresentingAnExpandedFrame` | the symmetric flash the brief called out |
| `RememberedVerdict_SurvivesDetachAndReattach` | why the reattach cases can decide immediately; also asserts a detached root keeps no transient focusability |
| `CreatorCompactTests.FirstVisitToTheTab_BelowTheSwitchPoint_NeverPresentsAnExpandedFrame` | the user's actual repro on the real view |

The real-view test earns its place beyond the synthetic ones: `CreatorView` runs its own
`LayoutUpdated` handler to cap the config scroller, and the fix now decides in line *during* a
layout pass, so the two run against each other on every first attach.

**RED observed** (fix disabled, rebuilt, re-run):

```
FirstAttach_ReachesItsVerdictInTheSameLayoutPassThatFirstSizesTheView
  first attach below the switch point: 1 of 1 layout passes were presentable frames in the
  WRONG mode (expected compact) — at heights 200
Reattach_IntoAWindowThatShrank_CompactsWithoutPresentingAnExpandedFrame
  reattach into a shrunken window: 1 of 1 ... (expected compact) — at heights 200
Reattach_IntoAWindowThatGrew_RestoresWithoutPresentingACompactFrame
  reattach into a grown window: 1 of 1 ... (expected expanded) — at heights 600
CreatorCompactTests.FirstVisitToTheTab_BelowTheSwitchPoint_NeverPresentsAnExpandedFrame
  2 of 5 layout passes were presentable frames in EXPANDED mode below the switch point (715)
  — at heights 661, 661
```

`RememberedVerdict_SurvivesDetachAndReattach` passed before the fix as well as after — that is the
point of it: it records that the retention the brief asked for already existed.

## 15. Evidence

`-t:Rebuild` on all three projects from a cleaned output directory, each **0 Warning(s),
0 Error(s)**, then `dotnet test --no-build`:

```
Manager   Passed!  - Failed: 0, Passed: 454, Skipped: 0, Total: 454
App.Core  Passed!  - Failed: 0, Passed: 712, Skipped: 0, Total: 712
```

Round-2 baselines Manager 449 / App.Core 712. Delta **+5 Manager**, App.Core unchanged.

Lifecycle guarantees re-run unmodified, all green:
`DeferredRecovery_RootDetachedBeforeThePassRuns_LeavesNoPhantomRootTabStop`,
`DeferredRecovery_RootTornDownDuringThePass_LeavesNoPhantomRootTabStop`,
`RootTransientFocusability_IsRevertedOnDetach`, `Reattach_ReevaluatesFromCurrentBounds`,
`FocusOutsideTheView_IsNeverStolen_ByTransitions`,
`NonTransitionalResize_FocusOutsideTheRoot_IsNeverPulledIn`,
`ResizeRecheck_FocusMovedOutsideBeforeThePassRuns_NeverRecovers`.

**Disclosure on how the builds were run.** The user's own `ReScene.Manager.exe` was running
throughout this round and held a lock on `ReScene.Manager/bin/Debug/net10.0/`, so `dotnet build`
into the default location failed with MSB3027. I did not kill it — it is the user's live session,
plausibly the one the defect was observed in. Instead every build and test run in this round used
`-p:BaseOutputPath=.altbin/ -p:UseAppHost=false`, which is a genuine full rebuild (all XAML
recompiled from a cleaned directory) written to a scratch location; `.altbin` was deleted
afterwards. The default `bin` therefore still holds the pre-round-3 binaries — worth knowing if the
running app is used to re-check the defect by hand before it is restarted.

## 16. Concerns (round 3)

1. **The revisit claim is unconfirmed.** I could not reproduce a flash on any visit after the first,
   and the state that would cause one demonstrably survives detach. If the user still sees it on
   revisits after this fix, the cause is something this change does not touch and the next step is a
   live capture rather than another mechanism.

2. **Deciding in line runs behavior code during a layout pass.** Classes and row definitions are
   mutated from inside a bounds notification, which schedules a further pass before the frame is
   presented. Bounded (one-shot per attachment) and all 454 tests pass, including the pixel-parity
   and frame-rig ones, but it is a genuinely more re-entrant path than the posted one and the whole
   suite is the evidence for it, not a targeted argument.

3. **The floor read at that instant omits the Help body's prose**, because flat mode has not yet
   forced it open. Quantified above (~12 DIPs on Creator) and self-correcting within the same
   dispatcher turn, but it means the in-line verdict can differ from the settled one for a view
   sitting within ~12 DIPs of its switch point — which would show as a single corrective transition
   rather than a wrong steady state.

4. **`Assert.Empty(passes)` in the real-view test asserts the precondition that an unselected tab is
   never laid out.** That is Avalonia's behaviour today rather than a contract; if it changed, the
   test would fail on its precondition instead of silently testing nothing, which is the failure
   direction I want.

5. **Still Windows-only verification**, as in rounds 1 and 2.

---

# Round 4 — 13px content text (2026-08-02)

App-wide content text moved 12 → 13px (user decision, "Commit 13px"). The mechanical flip arrived
already applied; this round resolved the four test failures it produced.

## 17. The measured picture

Worst case, inner width 676 at the 700×450 minimum, 12px → 13px:

| View | Compact floor, Help closed | Help open | Pinned band | Derived switch point |
|---|---|---|---|---|
| Reconstructor | 293 → **295** | 295 → **297** | 22 → **22** | 439 → **441** |
| SRSCreator | 276 → **278** | 281 → **283** | 68 → **70** | 511 → **523** |
| SRSReconstructor | 280 → **285** | 285 → **290** | 72 → **77** | 456 → **467** |
| SampleRestorer | 268 → **270** | 273 → **275** | 60 → **62** | 535 → **537** |
| Creator | 268 → **270** | 273 → **275** | 60 → **62** | 715 → **717** |

Measured by a throwaway probe (deleted) that hosted each view's worst case at the 319-DIP minimum
and read the floors, the pinned row and the switch point, run once against 13px and once against a
temporarily reverted 12px so the two columns are the same measurement.

**The derived thresholds absorbed the change with no code edit**, which is what they are for.
Switch points moved +2 to +12 DIPs and every sweep invariant is stated in terms of each view's own
switch point, so none of them needed touching.

**The one-sum compact invariant is unaffected**: worst floor 297 against the 307 CI bound, Help open
or closed, on every view — 10 DIPs of headroom.

## 18. Item 1 — pinned band 77 vs the 75 bound: path (b), and why

Taken: **amend the bound, to the spec's own number.** Not a fitted 77, and not path (a).

The tests were tighter than the document. Their ≤ 75 came from the per-view compact-floor TARGETS in
§1's table ("action ≤ 75") — figures for the floor SUM, not bounds on the band. What §4 actually
asserts the band against is its HEADROOM: `319 − 24 header − 120 config − 80 log − margins ≈ 84`.
So the bound is now **84**, which is a correction toward the spec rather than a loosening away from
it, shared as `CompactInvariantRig.PinnedBandCeiling` in place of four duplicated literals. It stays
a real guard: the bands measure 62–77, so a band that started genuinely crowding the work area would
still breach it.

Path (a) — reclaim ~2 DIPs of padding in SRSReconstructor's band — was considered and rejected. It
would shave a real visual design to satisfy a number the document never asserted, in one view out of
five, while the budget that number exists to protect measurably holds with 10 DIPs to spare. Fitting
the layout to the test rather than the test to the design is the inversion this whole workstream has
been undoing.

Re-verification requested with path (b) is the table in §17: every view's compact floor, Help closed
AND open, against the 307 CI bound. Nothing else breached, so this stayed a one-bound change.

Item 2 (the board's re-run of the same invariant) resolved with item 1, as expected.

## 19. Items 3 and 4 — recalibrated from measurement, not by widening tolerances

Measured at 13px with a throwaway probe (deleted): a `CheckBox` carrying the versions list's scoped
`MinHeight = 16` realizes at **18.00** — the text sizes the row now, the MinHeight no longer binds —
while the glyph box stays **14.00 × 14.00** and sits **2.00** from the row's top. `(18 − 14) / 2 =
2.00`: still exactly centred. At 12px the same rule gave 1.00 against a 16px row.

**Item 3, `VersionsDensityRow_GlyphCentersWithOnePixelShift`** — the guarded property (the glyph is
centred rather than top-aligned) holds untouched; only the row height moved. The expectation is now
derived from the measured slack, at the same one-decimal tolerance as before, so the assertion is
about CENTRING instead of about a font size. It stays discriminating: a regression to top alignment
reads 0 against an expected 2. Added alongside it, an assertion that the slack is at least 1 DIP —
without slack, centred and top-aligned are the same position and the test would prove nothing.
Renamed to `VersionsDensityRow_GlyphCentersInTheRow`, since "one pixel" is no longer true.

**Item 4, `VersionsTree_WpfClassicExpander_TogglesAndNamesEverything`** — diagnosed as expected
growth, not breakage. The failure was `leaf2 height 18 > 16`; its sibling leaf already allowed ≤ 18
and passed, so the two peers had inconsistent bounds and 13px moved both to 18. Nothing in the
template regressed: the glyph primitives are still 14. The dense list's pitch is therefore 20 rather
than v1.9's 18, which moves the row AWAY from the 2.5.8 target-size deviation §3 granted this list
rather than deeper into it, and the style's stated invariants (header toggle `MinHeight` ≥ 24, every
leaf keyboard-reachable) are untouched. The bound moved 16 → 18 to match its peer, and stays a hard
ceiling that still catches what it exists to catch: a Fluent bump restoring the 20px primitive floor
would read 20 against this 18.

## 20. Evidence

`-t:Rebuild` on all three projects, each **0 Warning(s), 0 Error(s)**, then
`dotnet test --no-build`:

```
Manager   Passed!  - Failed: 0, Passed: 454, Skipped: 0, Total: 454
App.Core  Passed!  - Failed: 0, Passed: 712, Skipped: 0, Total: 712
```

Baselines Manager 454 / App.Core 712 — no test count change, four failures resolved. The full suite
was run rather than the four in isolation: **no other latent pin on the 12px metrics survived**.

## 21. Concerns (round 4)

1. **`FontSizeCaption` (13) now equals content text**, flattening v1.9's inverted hierarchy where
   captions were deliberately larger than content. That is recorded as the user's accepted choice in
   the token comment, and nothing asserts a caption/content ratio, so no test speaks to it — but it
   does mean captions and content are now visually indistinguishable by size alone, and the
   remaining separation is colour (`ForegroundSecondary`) only.

2. **SRSCreator's switch point moved the furthest** (511 → 523, +12) while its neighbours moved 2.
   It is the view whose config band is a plain uncapped Auto row, so its floor tracks its content's
   full height and therefore feels the font change at full strength rather than absorbing it into a
   givable minimum. Nothing is wrong — it is the derived model working as designed — but it is the
   view most exposed to a future metric change.

3. **The dense versions list is now 20px pitch.** Better for 2.5.8 than the 18 it was granted a
   deviation for, but it is a visual density change to the busiest list in the app that nobody
   explicitly signed off; the 13px decision implies it rather than states it.

4. **`PinnedBandCeiling = 84` is still a written-down number**, not a derived one, unlike the switch
   points. Deriving it (CI bound minus the declared band minimums) would put it near 94–99 and make
   it a much weaker guard, so I kept the spec's figure. It will need revisiting on the next content
   metric change, where the switch points will not.

5. **The user's unrelated WIP was left strictly alone** — the App.Core test edits, the lib submodule
   pointer, the behaviors/views lint pass and the publish-profile path. None were touched or staged.
   Worth noting that the App.Core suite was run WITH those edits present and is green, so this
   round's evidence does not depend on them being reverted.
