# Small-Window Layout Degradation — Design

Status: rev 2 — addresses codex review rev-1 (7 blocking / 1 advisory) and a11y review rev-1
(4 blocking / 6 advisory); pending re-review. Rev 1 was DRAFT.

## Problem

Every task view carries fixed minimum heights that sum to a floor above what the window's
declared minimum (`MainWindow` 700×450) actually leaves for the view. Below the floor
nothing scrolls: the layout overflows and is clipped by the shell's content presenter,
leaving the page tail unreachable by scrollbar, wheel, or pointer, while Tab still moves
keyboard focus into the clipped region (WCAG 2.4.11 Focus Not Obscured, AA). The floor also
grows at runtime (conditional status/warning/progress rows, link rows re-wrapping), so a
height that fit can stop fitting mid-session.

### Measured height budget (DIPs, live app @ main 255d04e, window exactly 700×450)

Shell overhead is **width-dependent**: the shell tab strip needs ~715px for one row, so at
700 width it wraps to two rows.

| Shell element | ≥ ~720 px wide | at 700 px wide |
|---|---|---|
| Menu bar | 26 | 26 |
| Shell tab strip | 30 | **58 (wrapped, 2 rows)** |
| Status bar | 23 | 23 |
| **Task-root height at 450 window** | 371 | **343** |
| Inner content (after PageMargin 12×2) | 347 | **319** |

All view figures below are measured at 700×450 (inner width 676) unless noted.

### Per-view measured state at 700×450

| View | Measured composition (base state) | Base fate at 319 inner px |
|---|---|---|
| **ReconstructorView** | header stack 73 + toolbar 26 + tip 35 (+ margins ~14) + TabControl 220 (MinHeight, strip inside) + splitter 8 + log 140 (MinHeight) ≈ **516** | Overflows by ~197: the whole log + splitter + ~60px of the tab area render below the clip. Conditional custom-packer warning adds **31**. |
| **CreatorView** | intro 35 + input 65 + sep + options row 46 + StoredFiles DataGrid **150 (fixed)** + splitter 6 + bottom grid crushed to its 100 MinHeight (natural content ≈ **325**: output 46 + options stack 201 + action/progress 28 + log ≥40) | Bottom grid both crushed (100 of 325) AND clipped (ends 110 below the view). Detected-sets region can add ~96 (codex). Create action effectively unreachable. |
| **SRSCreatorView** (DockPanel) | docked stack ≈ 329 (intro 35, 3 picker groups, options, action row at y292) + log fill | Log = **2px sliver**. Worst-state additions: field statuses +38, progress ~+24, result banner ~+30 → the **action row itself clips** mid-run. |
| **SRSReconstructorView** (DockPanel) | docked stack ≈ 245 + log fill **74** | Fits at base with a small log; worst-state (+~90: statuses, result) drives the log to 0, then clips. |
| **SampleRestorerView** (DockPanel) | intro 35 + 3 picker groups (one label wraps to 35 at 700w) + "Embedded SRS Files" 16 + DataGrid **100 (MinHeight)** … | **Action row = 0px and log = 0px at BASE state** (measured at y331 with h0): the Restore button is unreachable at the declared window minimum today. |

## Approach (user-selected 2026-07-30)

**Shrink panes first; no page-level scrollbar.** Panes relax their minimums and scroll
internally; splitters arbitrate. **Header chrome auto-collapses** below a per-view
threshold. At normal sizes every view renders pixel-identical to today.

Consequence of the measured budget (a11y rev-1 #1/#2): with only 319px available at the
minimum, chrome collapse is part of the **fit mechanism**, not a nicety — the compact state
is simply always active at the window minimum, and the safety-critical invariant is on the
threshold (below).

## Design

### 1. Compact-mode mechanic — `CompactHeightBehavior`

Attached behavior on the view root with one attached property `Threshold` (logical DIPs,
compared against the view root's `Bounds.Height`). Toggles the style class `compactHeight`
on the view root. Contract (codex rev-1 #5):

- Compact iff `Bounds.Height < Threshold`; restore iff `Bounds.Height >= Threshold + 12`
  (hysteresis, prevents chatter and repeated SR structure churn during splitter/resize
  drags; 12 DIPs also swallows fractional-DIP jitter at 125/150% scaling).
- `Bounds.Height <= 0` (initial attach, template churn) is ignored — no class change until
  the first real measure.
- Bounds subscriptions attach on `AttachedToVisualTree`, detach on `Detached…`; re-attach
  re-evaluates from the current bounds. Multiple changes in one layout pass coalesce via a
  posted dispatcher update. Other classes on the root are never touched.
- **Focus guard (a11y rev-1 #4):** when toggling TO compact, if the currently focused
  element is a descendant of a region the compact styles collapse, focus moves to the Help
  expander header before the collapse applies. Toggling back never moves focus.
- Class semantics are height-only and content-independent — no feedback loop is possible
  (collapsing chrome does not change the window-driven root height).

**Threshold invariant (a11y rev-1 #2, executable):** for every view,
`Threshold >= expanded-chrome floor including every conditional row at the view's minimum
width` (warning row, all FieldStatusLines set, progress + result visible, links wrapped at
700w). A per-view unit test computes the expanded worst floor from a rendered instance and
asserts the XAML threshold exceeds it. Tuning a threshold down can therefore never open a
clipping band between the expanded floor and the compact trigger.

Per-view thresholds (view-root DIPs; = expanded worst floor + 20 safety, rounded up; the
floor figures use the NEW minimums of §3/§4 and are re-verified by the invariant test at
implementation time):

| View | Expanded worst floor (new minimums) | Threshold |
|---|---|---|
| Reconstructor | hdr 144 + warning 35 + TabControl 130 + splitter 8 + log 72 + margins ≈ 400 | **420** |
| Creator | intro+input+options 161 + detected-sets worst ~96 + grid 150 + splitter 6 + bottom bands (scroll min 80 + action 30 + log 72) ≈ 600 | **620** |
| SRSCreator | stack worst ≈ 420 + action 30 + log 72 ≈ 520 | **540** |
| SRSReconstructor | stack worst ≈ 335 + action 30 + log 72 ≈ 435 | **455** |
| SampleRestorer | stack worst ≈ 350 + grid 100 + action 30 + log 72 ≈ 550 | **570** |

(At the 343px task root of a 700×450 window, compact is always active on every view —
expected and correct.)

### 2. Chrome collapse — the "Help & links" disclosure

Under `.compactHeight`, the explanatory header (intro paragraph, tip line, download-link
rows) is replaced by a single **inline, real `Expander`** (Avalonia 11.3 ships
`ExpanderAutomationPeer` + `IExpandCollapseProvider` — expanded/collapsed announces for
free; a11y rev-1 #5). Requirements:

- **No content is deleted** (a11y rev-1 #3): the expander body carries the SAME intro
  prose, tip line, and the SAME link controls — moved, not duplicated, so exactly one
  active link set exists (codex rev-1 #4). Links keep their existing `Click` handler +
  `Tag` URL wiring verbatim (they have no Commands — corrected from rev 1).
- Inline expansion only — **no Popup/Flyout** (separate visual tree, focus containment
  burden). The body has `MaxHeight` ≈ 140 with its own vertical ScrollViewer (inset on the
  content panel, per house rule) so expanding below threshold can never push the work pane
  under its minimum (codex rev-1 #4): expanded body 140 + compact floor ≈ 240 ≤ 319 ✓.
- Header text "Help & links" (AutomationProperties.Name = "Help & links" — no decorative
  glyph in the name), keyboard-focusable, Enter/Space toggles (stock Expander behavior).
- Tab/reading order in compact: disclosure header → (body content when expanded: intro →
  links in their existing order) → toolbar → tab strip → … Collapsed body content is
  `IsVisible=false` ⇒ out of both Tab and UIA traversal.
- The toolbar row (Import/Export/Start) and the conditional warning row never collapse —
  the warning is content, not chrome.
- Focus transfer: defined in §1's focus guard for the collapse direction; on expand, focus
  stays where it is (the newly revealed content is reachable by normal Tab order).

### 3. Minimum relaxation (Reconstructor; the pattern for grid-based views)

- TabControl MinHeight **220 → 130** on both the row definition and the control (the two
  sites duplicate; 130 includes the ~30px sub-tab strip, i.e. ~100 of page content —
  codex rev-1 #1's strip accounting). Tab pages already scroll correctly (stage-1 fix).
- Log band MinHeight **140 → 72** = measured header DockPanel ~28 + two 20px list rows +
  horizontal-scrollbar allowance ~12 when a long line triggers it (a11y rev-1 #9's
  "header + ≥2 rows, measured"). The log **list** is the shrinking part; the header row
  (title, live status, Auto-scroll, Save log…) never shrinks or clips.
- GridSplitter operates strictly between these minimums (criterion E). **Local-value
  audit (codex rev-1 #6):** the splitters' local `Background="Transparent"` moves into a
  style (base + `:focus` states must both be styles or the focus style loses to the local
  value); likewise any `IsVisible` local defaults inside collapsing regions move to styles
  so `.compactHeight` styles can win. Verification is of RENDERED state (bounds/pixels),
  never selector presence.

### 4. Structural fixes for the scroll-less views

**Three-band Grid** replaces the DockPanel arrangement in SRSCreatorView,
SRSReconstructorView, SampleRestorerView (a `ScrollViewer` merely dropped into a DockPanel
Auto slot measures unbounded — codex rev-1 #3; the Grid enforces the budget):

```
RowDefinitions:
  *    MinHeight=120   — configuration band: ScrollViewer (inset on content panel)
                          hosting the existing config stack unchanged (DataContext,
                          bindings, tooltips, validation adorners move with the subtree)
  Auto                 — pinned action band: Start/Cancel button row + ProgressMessage +
                          ProgressBar + result banner — ALL state feedback in this one
                          always-visible band (a11y rev-1 #8; codex rev-1 #3)
  *    MinHeight=72    — log band (header fixed, list scrolls/shrinks)
```

- The pinned band is a plain sibling grid row — normal reading order; overlay/adorner
  implementations are forbidden (they would re-introduce 2.4.11).
- SampleRestorerView's `SRSEntriesGrid` (MinHeight 100 / MaxHeight 250) stays **inside**
  the configuration ScrollViewer with explicit boundary behavior: wheel hand-off at the
  grid's scroll extents to the outer viewer, cell focus `BringIntoView` chaining to the
  outer viewer, and keyboard tests covering inner (cell navigation) and outer (Tab
  through) traversal (codex rev-1 #3's required alternative).
- **CreatorView** keeps its own grid but: StoredFiles row stays **fixed 150 at normal
  size** — `MinHeight 80` applies only under `.compactHeight` (pixel parity preserved;
  a11y rev-1 #6); the bottom half becomes the same three bands as above (config scroll
  min 120 hosting Output + Options; pinned action band incl. its ProgressBar; log band
  min 40 → 72 rule applies only if its measured header+2-rows exceeds 40 — measure at
  implementation). The detected-sets region (upper half, growth ~96) gets `MaxHeight`
  with internal scrolling so its growth is bounded (codex rev-1 #2).
- Any checkbox label gaining `TextWrapping` in this work MUST take `Classes="wrapLabel"`
  (recorded binding from the glyph work).

### 5. GridSplitter polish (folded in)

`AutomationProperties.Name` per view ("Resize options and log" / "Resize stored files and
output"), and a visible `:focus` style ≥3:1 against both panes — base `Background` moved
to a style per §3's local-value audit; verified rendered (screenshot/pixel or brush-on-
rendered-node assertion), including a high-contrast smoke note.

## Acceptance criteria (binding; a11y ruling as refined by both rev-1 reviews)

A. At 700×450, on every sub-tab of every task view, every control is **reachable and fully
   visible once scrolled into view** via scrollbar drag, wheel, AND keyboard — verified
   specifically for the last control on Reconstructor Options and Output, and each view's
   primary action.
B. No content renders outside its scrollable ancestor's visible clip — worst case
   exercised: warning row visible, statuses set, progress + result visible, populated
   DataGrid, 150% render scaling.
C. **Real Tab traversal** (2.4.11): in a real `MainWindow` at 700×450, send actual
   Tab/Shift+Tab key input from a known sentinel through the full cycle; after every step,
   assert the focused control's bounds lie within the intersection of every clipping
   ancestor's viewport and the window (codex rev-1 #7). `Focus()` enumeration is not
   acceptable as the criterion test.
D. The log stays reachable at all sizes: header row (title, status, Save log, and
   Auto-scroll where the view exposes it — only the Reconstructor does) visible and
   operable; the list shrinks to ≥2 rows, never clips.
E. GridSplitters tab-reachable, Up/Down-resizable, cannot drive either pane below its
   minimum, and show a visible ≥3:1 focus indication.
F. **Normal size:** tab order and reading order unchanged — asserted by an ordered
   tab-order snapshot (control type + automation name) before/after per touched view, plus
   Reconstructor frame-rig pixel parity; all five views' normal-size layouts compared.
   **Compact:** the explicit orders of §2 apply.

## Testing

- Per view: threshold invariant test (§1 — computed expanded worst floor < XAML
  threshold); compact toggle + hysteresis test (boundary, boundary±1, rapid crossing,
  window-restore burst, reload/reattach, 1.0/1.25/1.5 render scale); chrome-collapse
  content test (prose + every link present and invocable through the expander; nothing
  lost vs normal mode); focus-guard test (focus inside collapsing region relocates to the
  expander header); criterion C Tab-walk; criterion F tab-order snapshots; splitter floor
  + focus-visual (rendered) tests; three-band views: pinned band visible with progress +
  result forced visible while config scrolled to both extremes; SampleRestorer inner/outer
  scroll hand-off tests.
- Matrix: run A/B/C at 700×450 (compact) AND at each view's `Threshold+1` with expanded
  chrome + worst conditional rows (a11y rev-1 #10) — the non-compact fit path is a
  distinct code path and must not be tested only where compact hides it.
- Render-scaling tests (RenderScaling 1.25/1.5) are separate from any font-size
  enlargement test (codex rev-1 #7).
- Full Manager suite on forced rebuilds (stale-XAML hazard: marker-scan or `-t:Rebuild`
  before trusting XAML-behavior probes).
- Runtime ava-desktop pass mirroring the user's VM size, before/after captures.

## Out of scope

- The 24px checkbox pitch (2.5.8) — separate pending user decision.
- Legend tri-state text alternative (pre-existing 1.1.1 gap, tracked).
- Any change to view content or behavior at normal window sizes.

## Rollout

One plan. Task order: CompactHeightBehavior + threshold-invariant test rig first (shared
infrastructure), then Reconstructor (template view: chrome expander, minimums, splitter
polish), then CreatorView, then the three three-band views (one task each), then Settings
audit + whole-board verification. Review gates per task: codex diff review; a11y-lead
reviews this spec (done, rev-1) and the final state against criteria A–F.
