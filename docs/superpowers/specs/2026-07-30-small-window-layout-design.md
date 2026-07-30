# Small-Window Layout Degradation — Design

Status: rev 3 — addresses codex rev-2 (5 still-open + 2 new blocking + 1 advisory) and a11y
rev-2 (2 blocking + 2 advisory); pending re-review.

## Coordinate space (normative for every figure in this document)

All heights are **inner-content DIPs**: the height of each view's inner layout root (the
Grid/DockPanel inside the 12px PageMargin). `CompactHeightBehavior` attaches to that inner
root and compares ITS `Bounds.Height`; the `compactHeight` class is set on the same
element (it ancestors all styled content). The threshold-invariant test computes floors in
this same space. Window↔inner conversion at minimum size, measured: window 450 − menu 26 −
wrapped shell strip 58 (700w: the 8 shell tabs need ~715px and wrap to two rows) − status
23 − PageMargin 24 = **319 inner DIPs available at 700×450**. (At widths ≥ ~720 the strip
is one row and the same window height yields 347.)

## Problem

Below each view's floor nothing scrolls: layout overflows and is clipped by the shell,
leaving the page tail unreachable while Tab still moves focus into the clipped region
(WCAG 2.4.11 AA). Floors also grow at runtime (conditional rows). Measured at 700×450
(base state, inner width 676):

| View | Measured base composition | Fate at 319 |
|---|---|---|
| Reconstructor | header 73 + toolbar 26 + tip 35 + margins + TabControl 220(min) + splitter 8 + log 140(min) ≈ 516 | log + splitter + 60px of tabs below the clip; warning row adds 31–35 |
| Creator | intro 35 + input 65 + options 46 + StoredFiles grid 150(fixed) + splitter 6 + bottom grid crushed to 100 (natural ≈ 325) | bottom half crushed AND clipped; detected-sets adds ~96; Create unreachable |
| SRSCreator | docked stack ≈ 329 + log fill | log = 2px; worst rows (+~92) clip the action row |
| SRSReconstructor | stack ≈ 245 + log 74 | worst rows (+~90) drive log to 0 then clip |
| SampleRestorer | stack + grid 100(min) ≥ 319 | **action row and log measure 0px at BASE — Restore unreachable today** |

## Approach (user-selected 2026-07-30)

Shrink panes first; no page-level scrollbar; header chrome auto-collapses below a per-view
threshold; pixel-identical at normal sizes. With 319 available at minimum, compact is part
of the fit mechanism and is always active at 700×450.

**Universal mechanism rule (rev 3):** every structure is ALWAYS PRESENT in the visual
tree; mode changes ONLY sizing constraints and visibility — no reparenting, no duplicated
content, ever. Styles carry all changes selectors can reach; `RowDefinition` is not
styleable (no Classes — a11y rev-2 NEW-2), so row Height/MinHeight mode values are applied
by `CompactHeightBehavior` from a per-view declarative map (see §1), preserving a
user-dragged splitter height across a compact round-trip.

## Design

### 1. `CompactHeightBehavior`

Attached to the inner layout root; properties: `Threshold` (inner DIPs), optional
`RowSizes` map (`rowIndex → (normalHeight, compactHeight, compactMinHeight)`).

- Compact iff `height < Threshold`; restore iff `height >= Threshold + 12` (hysteresis —
  restore-only, the safe direction; swallows fractional-DIP jitter at 125/150%).
  A FRESH view whose first real measure lands anywhere `>= Threshold` starts expanded —
  hysteresis applies only to an instance already compact (codex rev-2 NEW-B2: the matrix
  tests fresh instances at `Threshold+1` = expanded; restoration transitions are tested at
  `>= Threshold+12`).
- `height <= 0` ignored; subscriptions follow Attached/DetachedFromVisualTree with
  re-evaluation on re-attach; per-layout-pass coalescing via one posted dispatcher update;
  other classes untouched.
- Row application: on mode change, applies the `RowSizes` map (behavior-owned because
  selectors cannot reach RowDefinitions); a splitter-modified height is captured before
  compact and restored on expand.
- **Staged focus transition (codex rev-2 #5):** entering compact — (1) apply compact
  styles/rows, (2) run a layout pass, (3) if the previously-focused element is now
  non-effectively-visible, focus the Help expander header. Leaving compact — same staging;
  if the focused element (e.g. the expander header, hidden at normal size) becomes
  non-effectively-visible, focus moves to the first focusable of the restored header
  region. No focus change otherwise in either direction.

**Threshold invariant (executable):** per view,
`Threshold >= rendered expanded-mode worst floor` (all conditional rows forced visible,
links wrapped at 700w, computed in inner space by a unit test that renders the view).
The same test asserts the **compact-mode worst floor ≤ 319** and the pinned band's worst
height ≤ its headroom (§4). Thresholds cannot drift unsafe, and compact feasibility is
proven, not assumed.

Per-view figures (inner DIPs; log band floor **80** = header 28 + 2×20 rows + 12
horizontal-scrollbar allowance — corrected arithmetic, codex rev-2 NEW-B1; all numbers
re-verified by the invariant test at implementation):

| View | Compact worst floor (≤ 319 required) | Expanded worst floor | Threshold (floor+20) |
|---|---|---|---|
| Reconstructor | expander hdr 24 + toolbar 26 + warning 35 + TabControl 130 + splitter 8 + log 80 + margins ~14 ≈ **317** | 73+26+35+31+130+8+80+margins ≈ 401 | **421** |
| Creator | hdr 24 + config scroll 120 (inputs+detected+grid+output+options inside) + action ≤84 + log 80 + margins ~8 ≈ **316** | natural stack ≈ 161+96+150+6+325 with new log floor ≈ 700 | **720** |
| SRSCreator | hdr 24 + config 120 + action ≤84 + log 80 + ~8 ≈ **316** | ≈ 330 stack + 84 + 80 ≈ 500 | **520** |
| SRSReconstructor | same shape ≈ **316** | ≈ 265 + 84 + 80 ≈ 430 | **450** |
| SampleRestorer | same shape (grid inside config) ≈ **316** | ≈ 350 + 84 + 80 ≈ 515 | **535** |

(Creator's large threshold simply means Creator is compact in most real windows — correct,
given its content volume.)

### 2. Chrome — the "Help" disclosure (always-present, single instance)

One inline `Expander` per view, ALWAYS in the tree, holding the view's intro prose, tip
line (Reconstructor), and link controls (Reconstructor) — the single instance of that
content; no second copy exists anywhere (a11y rev-2 NEW-3).

- **Normal mode (styles):** the Expander renders "flat" — header row hidden
  (`IsVisible=false` via style), body force-expanded and unconstrained → visually today's
  header block. Criterion F's pixel rig covers this region specifically; if Fluent's
  Expander template chrome breaks pixel parity, the fallback (implementation decision,
  rig-evidenced) is a two-slot custom header control with the identical single-instance +
  visibility contract — the spec requirement is the contract, not the template.
- **Compact mode:** header visible ("Help & links" on Reconstructor, "Help" on the other
  views — codex rev-2 advisory; AutomationProperties.Name = the same text, no glyph),
  body collapsed by default, stock ExpandCollapse peer announces state. The USER's
  expand/collapse choice is durable across compact re-entries within the session (codex
  rev-2 #8).
- Body budget: `MaxHeight = 319 − compact floor of the view` at minimum (≈ 120 for the
  three-band views, ≈ 100 Reconstructor — exact residual computed per view in the
  invariant test; codex rev-2 #4), with internal scrolling (inset on the content panel).
  Expanding can therefore never push any band below its minimum.
- Compact order: disclosure header → (body when expanded: intro → tip → links in existing
  order) → toolbar/warning → work area → … Collapsed body is `IsVisible=false` ⇒ out of
  Tab and UIA. Toolbar and the conditional warning row are content — never collapsed.
- Normal-mode order: identical to today (the hidden header contributes nothing).
  Criterion F snapshots BOTH modes (a11y rev-2 NEW-3).

### 3. Minimum relaxation and local-value audit

- Reconstructor TabControl MinHeight 220 → **130** (row + control, strip-inclusive: ~30
  strip + ~100 page).
- Log bands: MinHeight **80** everywhere (list is the shrinking part; header row never
  shrinks; CreatorView's log adopts the same 80 — its current 40 fails the ≥2-rows rule).
- Splitters operate strictly between minimums; their local `Background="Transparent"`
  moves into a style (base + `:focus` both style-supplied so the focus style wins);
  `IsVisible` locals inside chrome regions move to styles. All state verification is of
  RENDERED results (bounds/brushes on rendered nodes), never selector presence.

### 4. Band structure (always-present; constraints per mode)

**Three-band views (SRSCreator, SRSReconstructor, SampleRestorer)** — the DockPanel is
replaced by a Grid whose rows are (codex rev-2 #3):

- Normal mode: `Auto / Auto / *(min 80)` — config renders at natural height, log fills:
  today's rendering exactly (parity).
- Compact mode (behavior-applied rows): `*(min 120) / Auto / *(min 80)` — config band
  becomes the squeezed, scrolling region.
- Band 1: an always-present ScrollViewer hosting the existing config stack unchanged
  (bindings/tooltips/validation intact — nothing reparents at runtime). At natural height
  it shows no scrollbar and renders identically.
- Band 2 (pinned, Auto): per-view feedback inventory (codex rev-2 advisory) —
  SRSCreator: Create/Cancel row + ProgressMessage + ProgressBar + result banner;
  SRSReconstructor: Reconstruct row + result Border;
  SampleRestorer: Restore row + ProgressBar + progress text.
  The band's worst height is asserted ≤ its headroom (319 − 24 − 120 − 80 − margins ≈ 84;
  a11y rev-2 NEW-4); the result banner gets `MaxHeight` + internal scroll/trimming.
  Overlay/adorner implementations forbidden (2.4.11).
- SampleRestorer's `SRSEntriesGrid` stays inside band 1 with boundary contracts: wheel
  hand-off at the grid's extents, cell-focus BringIntoView chaining to the outer viewer,
  inner (cell navigation) and outer (Tab-through) keyboard tests.
- **CreatorView** adopts the same pattern generalized (codex rev-2 #1/#2 — its previous
  compact plan measurably exceeded 319): band 1 hosts everything above the action area —
  inputs, detected-sets region, StoredFiles grid (grid keeps its 150 height normally via
  the RowSizes map → 80 compact; splitter between grid and lower content lives inside
  band 1 and keeps working — pixel rows via the map), output row, options stack. Bands
  2/3 as above. Normal mode: natural Auto sizing ⇒ today's rendering (rig-verified);
  compact: `*(120)/Auto/*(80)`. Detected-sets keeps a MaxHeight + internal scroll.
- Any label gaining `TextWrapping` takes `Classes="wrapLabel"` (standing glyph-work rule).

### 5. Splitter polish

Per-view `AutomationProperties.Name`: Reconstructor "Resize options and log"; Creator
"Resize stored files and output". Visible `:focus` style ≥3:1 against both adjacent panes,
verified by an executable assertion (contrast computed from the rendered focus brush vs
both pane backgrounds), plus a high-contrast-theme smoke capture.

## Acceptance criteria

A. At 700×450, on every sub-tab of every task view, every control is reachable and fully
   visible once scrolled into view via scrollbar drag, wheel, AND keyboard — verified for
   the last control on Reconstructor Options and Output and each view's primary action.
   Wheel and scrollbar-thumb paths exercised with genuine input events (codex rev-2 #7).
B. No content outside its scrollable ancestor's visible clip — worst case: warning row +
   all statuses + progress + result visible, populated DataGrid, 150% render scaling.
C. Real Tab/Shift+Tab traversal from a sentinel in a real MainWindow at 700×450; after
   every step the focused control's bounds lie within the intersection of every clipping
   ancestor's viewport and the window.
D. Log reachable at all sizes: header row (title, status, Save log; Auto-scroll where
   exposed — Reconstructor only) visible and operable; list ≥2 rows, never clipped.
E. Splitters tab-reachable, Up/Down-resizable, bounded by pane minimums, visible ≥3:1
   focus indication (executable check + high-contrast smoke).
F. Normal size: tab order, reading order, and pixels unchanged — ordered tab-order
   snapshot (type + automation name) before/after per touched view + frame-rig pixel
   parity for ALL FIVE views (each is structurally touched); compact: the §2/§4 orders,
   snapshot-locked (both modes).

## Testing

- Threshold-invariant test per view (§1): expanded worst floor < Threshold; compact worst
  floor ≤ 319; pinned-band worst ≤ headroom; Expander body MaxHeight = measured residual.
- Behavior tests: boundary (T−1/T/T+1 fresh instances — T+1 is EXPANDED), restoration at
  ≥T+12, rapid crossing, window-restore burst, reload/reattach, render scales 1.0/1.25/1.5.
- Chrome tests: single-instance (one link set in the tree in both modes); prose+links
  invocable in compact via the expander; durable user expand state across compact
  re-entries; staged focus both directions (collapse: focus → header; expand: focus →
  restored header region when the header hides).
- Criterion C Tab-walk; F snapshots (both modes) + five-view pixel parity;
  splitter floor/focus tests; three-band views: pinned band visible with all feedback
  forced while band 1 is scrolled to both extremes; SampleRestorer inner/outer hand-off.
- Font-enlargement test distinct from RenderScaling: a FontSize bump (12→16 via the
  Density resource) at 700×450 must not clip the pinned band or log header (text growth is
  absorbed by scrolling regions).
- LabeledBy/name audit for the log list and DataGrids ("Log", "Embedded SRS Files" etc.)
  retained or added on the touched surfaces.
- Full Manager suite on forced rebuilds (stale-XAML hazard); runtime ava-desktop pass at
  the VM size with before/after captures.

## Out of scope

Unchanged from rev 2: 24px checkbox pitch (2.5.8) — separate user decision; legend
tri-state text alternative (tracked); any normal-size content/behavior change.

## Rollout

One plan: CompactHeightBehavior + invariant-test rig → Reconstructor (template: expander,
minimums, splitter polish) → the three three-band views (one task each) → CreatorView
(largest restructure, benefits from the pattern being proven) → Settings audit +
whole-board verification. Gates per task: codex diff review; a11y-lead final review
against A–F.
