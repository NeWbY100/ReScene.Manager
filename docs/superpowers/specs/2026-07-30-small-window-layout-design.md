# Small-Window Layout Degradation — Design

Status: rev 8 — rev 7 + a11y focus-contract gaps folded (target-resolution fallback
chain, no-focus-theft precondition, documented obscured-vs-fully-within asymmetry).
A11y conditions from rev 6 unchanged; A–F gate stands. Pending codex re-review.

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

- **Boundary convention (used verbatim everywhere in this spec and its tests):** compact
  iff `height < Threshold`; restore iff `height >= Threshold + 12` (hysteresis —
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
- **Staged focus transition (rev 7 — executable form; replaces the rev-3 wording):**
  the behavior carries TWO named, direction-specific attached targets:
  `CompactFocusTarget` (the Help expander's realized header ToggleButton — the Expander
  control itself is not focusable) and `RestoreFocusTarget` (a per-view named control
  that exists and is focusable at normal size: Reconstructor = the first link Button;
  the three-band views and Creator = the view's first input TextBox).
  Transition algorithm, both directions: (1) CAPTURE the currently-focused element
  BEFORE any change; (2) apply styles/rows; (3) run a layout pass; (4) decide
  obscurement — an element is obscured iff it is detached, `IsVisible==false` anywhere
  in its chain, OR its rendered bounds do not intersect the intersection of every
  clipping ancestor's viewport (`IsEffectivelyVisible` alone is NOT sufficient — it
  ignores clipping); (5) if the captured element is obscured, first call
  `BringIntoView()` on it and re-run the check — scrollable ancestors may recover it;
  (6) only if still obscured, focus the direction's target (entering compact →
  CompactFocusTarget; leaving → RestoreFocusTarget). No focus change otherwise.
  Three riders (a11y rev-7 review):
  — PRECONDITION: steps 4–6 run only if the captured element was focused AND is a
    descendant of THIS view root. A resize while focus sits in the shell menu, the tab
    strip, another window, or nowhere must never pull focus into the view (focus theft
    is worse than the stranding it would fix, and fires on an event the user did not
    initiate).
  — TARGET RESOLUTION: a target can resolve null or unfocusable (the compact target is
    a TEMPLATED part — the header ToggleButton exists only after template application,
    so an early or re-attach pass can miss it). Fallback chain, in order: the resolved
    target → the first focusable descendant of the view root → the view root itself.
    A silent no-op is forbidden; tests assert the resolution was non-null.
  — DELIBERATE ASYMMETRY (do not "harmonize"): relocation triggers on ENTIRELY obscured
    (bounds not intersecting the clip intersection — the WCAG 2.4.11 AA line), while
    criterion C asserts the stricter FULLY WITHIN. Both are correct: C's Tab walk lets
    BringIntoView resolve partial clipping first; the relocation threshold only catches
    what scrolling cannot recover.

**Threshold invariant (executable, ONE budget sum — a11y rev-3 NEW-5):** per view, a unit
test that renders the view asserts, all in inner space with conditional rows forced
visible and links wrapped at 700w:

1. `Threshold >= rendered expanded-mode worst floor`;
2. `compact worst floor (Help closed) <= 307`;
3. `compact worst floor with Help OPEN — donated minimums in effect PLUS the body's
   MaxHeight — <= 307` (a single sum, never two independent checks: the body budget and
   the band minimums spend the same pixels);
4. pinned-band worst height fits its headroom within the same sums.

The 307 bound is 319 minus the 12-DIP jitter allowance (a11y rev-3 advisory): fractional
DIPs at 125/150% and the warning row's 31–35 spread must fail in CI, not on a user's
screen. Thresholds cannot drift unsafe, and compact feasibility is proven, not assumed.

**Donation rule:** while the Help body is expanded in compact mode, the primary work band
donates height — its compact minimum drops further (Reconstructor TabControl 96 → 60;
three-band config 110 → 80), behavior-applied together with the expander state. The body's
`MaxHeight` equals the donated budget at the minimum window (Reconstructor ≈38, three-band
≈40 — test-computed, scrolling internally); closing Help restores the compact minimums. Help is transient reference
content — briefly shrinking the work pane is the correct trade.

Per-view figures (inner DIPs; log band floor **80** = header 28 + 2×20 rows + 12
horizontal-scrollbar allowance — corrected arithmetic, codex rev-2 NEW-B1; all numbers
re-verified by the invariant test at implementation):

Compact minimums are two-tier: the values below are the compact-mode floor; while Help is
open the work band's minimum drops to 80 (donation rule). All floors are design targets —
the invariant test measures the rendered truth against the 307 bound.

| View | Compact worst floor, Help closed (≤ 307) | Expanded worst floor | Threshold (floor+20) |
|---|---|---|---|
| Reconstructor | expander hdr 24 + toolbar 26 + tip (1-line) 18 + warning 35 + TabControl **96** + splitter 8 + log 80 + margins ~18 ≈ **305** | 73+26+35+31+130+8+80+margins ≈ 401 | **421** |
| Creator | hdr 24 + config scroll **110** (inputs+detected+grid+output+options inside) + action ≤75 + log 80 + margins ~8 ≈ **297** | natural stack ≈ 161+96+150+6+325 with new log floor ≈ 700 | **720** |
| SRSCreator | hdr 24 + config **110** + action ≤75 + log 80 + ~8 ≈ **297** | ≈ 330 stack + 84 + 80 ≈ 500 | **520** |
| SRSReconstructor | same shape ≈ **297** | ≈ 265 + 84 + 80 ≈ 430 | **450** |
| SampleRestorer | same shape (grid inside config) ≈ **297** | ≈ 350 + 84 + 80 ≈ 515 | **535** |

(Creator's large threshold simply means Creator is compact in most real windows — correct,
given its content volume.)

### 2. Chrome — the "Help" disclosure (always-present, single instance)

One inline `Expander` per view, ALWAYS in the tree, holding the view's intro prose and
link controls (Reconstructor) — the single instance of that content; no second copy exists
anywhere (a11y rev-2 NEW-3). The Reconstructor TIP line ("Import from SRR…") is NOT in the
body (rev 5): it renders AFTER the toolbar today, so moving it into the body would change
normal-mode reading order (criterion F). It stays always-present in its own row in both
modes; under `.compactHeight` it is styled to a single line — APPROVED with conditions
(a11y rev-5 ruling), all binding:

1. Trimming is VISUAL-ONLY: `TextTrimming` over the full bound text — never a shortened
   string in VM or XAML. Asserted: in compact, the rendered tip's UIA Name equals the
   full tip text (a pre-truncated binding would silently reinstate the deletion defect).
2. `ToolTip.Tip` (pointer users) AND `AutomationProperties.HelpText` (AT description) both
   carry the full text — tooltips are not a keyboard/AT path.
3. Accepted residue, recorded: keyboard-only sighted users at compact size see one trimmed
   line with no route to the remainder; mitigation — the same guidance already lives on
   the Import-from-SRR button's own tooltip.
4. The tip is never the budget donor: if its measured one-line height exceeds 18 DIPs, the
   TabControl minimum gives way; the tip never becomes `IsVisible=false` under budget
   pressure.

- **Normal mode (styles):** the Expander renders "flat" — header row hidden
  (`IsVisible=false` via style), body force-expanded and unconstrained → visually today's
  header block. Criterion F's pixel rig covers this region specifically; if Fluent's
  Expander template chrome breaks pixel parity, the fallback (implementation decision,
  rig-evidenced) is a two-slot custom header control with the identical single-instance +
  visibility contract — the spec requirement is the contract, not the template.
- **Compact mode:** header visible ("Help & links" on Reconstructor, "Help" on the other
  views — codex rev-2 advisory; AutomationProperties.Name = the same text, no glyph),
  body collapsed by default, stock ExpandCollapse peer announces state. The USER's
  expand/collapse choice is durable within a CONTINUOUS compact session only — re-entering
  compact starts with Help collapsed (a11y rev-5 condition 5: with the 60-DIP help-open
  work band, session-durable expansion would turn one transient Help click into a
  permanent ~30px work pane on every later small window; codex rev-2 #8's durability is
  narrowed accordingly).
- Body budget: the **donation rule** of §1 — the body's `MaxHeight` equals the height the
  work band donates while Help is open (≈ 40–50 DIPs at the minimum window; the invariant
  test's one-sum check #3 is the authority), with internal scrolling (inset on the content
  panel). Expanding therefore consumes the donated space and never pushes any band below
  its Help-open minimum.
- Compact order: disclosure header → (body when expanded: intro → links in existing
  order) → toolbar → tip (single-line) / warning → work area → … Collapsed body is `IsVisible=false` ⇒ out of
  Tab and UIA. Toolbar and the conditional warning row are content — never collapsed.
- Normal-mode order: identical to today (the hidden header contributes nothing).
  Criterion F snapshots BOTH modes (a11y rev-2 NEW-3).

### 3. Minimum relaxation and local-value audit

- Reconstructor TabControl MinHeight 220 → **130** normal-relaxed (row + control,
  strip-inclusive: ~30 strip + ~100 page), **96** in compact, **60** while Help is open
  (the latter two via the behavior's RowSizes/donation application; rev 5 — the
  always-visible single-line tip is paid for by the work band, which stays scrollable).
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
- Compact mode (behavior-applied rows): `*(min 110) / Auto / *(min 80)` — config band
  becomes the squeezed, scrolling region (min 80 while Help is open, per the donation
  rule).
- Band 1: an always-present ScrollViewer hosting the existing config stack unchanged
  (bindings/tooltips/validation intact — nothing reparents at runtime). At natural height
  it shows no scrollbar and renders identically.
- Band 2 (pinned, Auto): per-view feedback inventory (codex rev-2 advisory) —
  SRSCreator: Create/Cancel row + ProgressMessage + ProgressBar (NO result banner —
  the outcome lands in the log; corrected inventory);
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
  compact: `*(110)/Auto/*(80)`. Detected-sets keeps a MaxHeight + internal scroll.
  The in-scroller splitter (a11y rev-3 advisory): criterion E's pane-minimum bound applies
  to it at NORMAL size only — in compact it sits inside a scrolling region where it
  adjusts natural heights rather than a visible split; it remains focusable and
  keyboard-operable in both modes.
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

- Threshold-invariant test per view (§1): the four one-sum checks — expanded worst floor
  < Threshold; compact floor (Help closed) ≤ 307; compact floor with Help open + body
  MaxHeight ≤ 307 as one sum; pinned-band worst within the same sums.
- Rendered matrix (restored from rev 2 — a11y rev-3 advisory): run criteria A/B/C as
  RENDERED runs at 700×450 (compact) AND at each view's `Threshold+1` with expanded
  chrome + all worst conditional rows — the expanded fit path must be verified by
  rendering, not only by the computed floor.
- Behavior tests: boundary (T−1/T/T+1 fresh instances — T+1 is EXPANDED), restoration at
  ≥T+12, rapid crossing, window-restore burst, reload/reattach, render scales 1.0/1.25/1.5.
- Chrome tests: single-instance (one link set in the tree in both modes); prose+links
  invocable in compact via the expander; expand state durable within a continuous compact
  session AND reset on compact re-entry; compact tip UIA Name == full tip text (condition
  1) + HelpText present (condition 2); staged focus both directions (collapse: focus →
  header; expand: focus → restored header region when the header hides).
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
