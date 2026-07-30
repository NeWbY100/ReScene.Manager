# Small-Window Layout Degradation — Design

Status: rev 1 — DRAFT, pending codex + accessibility review

## Problem

Every task view carries fixed minimum heights that sum to a floor above the app's declared
window minimum (MainWindow `MinWidth=700 MinHeight=450`). Below the floor nothing scrolls:
the layout grid overflows and is clipped by the shell's content presenter, leaving the page
tail (part of the tab area, the splitter, the log — on some views the primary action button)
unreachable by scrollbar, wheel, or pointer, while Tab still moves keyboard focus into the
clipped region (WCAG 2.4.11 Focus Not Obscured failure, Level AA). The floor also grows at
runtime (conditional warning rows, link rows re-wrapping at narrow widths), so a height that
fit can stop fitting mid-session.

Measured floors / defects (a11y survey 2026-07-29):

- **ReconstructorView** ≈ 565px: four Auto header rows (~165px: intro, links, toolbar, tip)
  + tab strip (~30) + TabControl row `2*` MinHeight 220 (duplicated on the control) +
  splitter 8 + log row `1*` MinHeight 140.
- **CreatorView**: stored-files row fixed `Height="150" MinHeight="150"` (cannot shrink);
  bottom half (Output + Options + Action inside a `*` MinHeight 100 grid of six Auto rows +
  a `*` MinHeight 40 log) has **no ScrollViewer at all** — clips with zero scroll path.
- **SRSCreatorView / SRSReconstructorView / SampleRestorerView**: DockPanel roots with ~10
  `Dock="Top"` children and the log as fill child, **no ScrollViewer anywhere**; at small
  heights the log collapses to zero and the last docked items — including the primary
  action row — clip off-screen entirely. SampleRestorerView also docks a DataGrid
  (MinHeight 100 / MaxHeight 250) above its action row.
- **SettingsWindow**: already compliant (its pages scroll; own MinHeight 360). Audit only.

## Approach (user-selected 2026-07-30)

**Shrink panes first; no page-level scrollbar.** Panes relax their minimums and scroll
internally; the splitter arbitrates space. **Header chrome auto-collapses** below a per-view
threshold. At normal sizes every view stays pixel-identical to today.

## Design

### 1. Compact-mode mechanic

A view root observes its own bounds height and toggles a style class `compactHeight` on
itself when below a per-view threshold — a pure function of height (no dependence on its own
collapse result, so no oscillation). Implementation: one small attached behavior
(`Behaviors/CompactHeightBehavior`, attached property `Threshold`), applied on the view
root; styles scoped `.compactHeight …` do all visual changes. Threshold is chosen per view
as "the height below which the primary work pane would drop under its comfortable size",
not the hard fit floor — Reconstructor: **560** (keeps the tab pane ≥ ~220 effective before
chrome gives way). Exact thresholds for the other views are fixed at plan time from the same
rule and recorded in the view XAML beside the behavior.

### 2. Chrome collapse (per view, styles under `.compactHeight`)

- Intro paragraph and tip line: `IsVisible=False`.
- Download-links rows: replaced by one compact expander line "ⓘ Help & links" containing
  the same link controls (same commands/URLs — the links are functional, not prose, and
  must stay reachable). The expander is keyboard-focusable with an AutomationProperties.Name;
  expanding overlays/inserts the links without re-raising the floor when collapsed.
- Toolbar row (Import/Export/Start) never collapses.
- Reclaims ~110px of the Reconstructor's ~130px explanatory header. Views without link rows
  collapse only their prose lines.

### 3. Minimum relaxation (Reconstructor pattern; Settings-style grids follow it)

- TabControl row and control MinHeight: 220 → **100** (both sites — the control duplicates
  the row minimum and is the stubborner of the two).
- Log row MinHeight: 140 → **60** (log list scrolls; its header + Auto-scroll/Save controls
  remain fixed above the list).
- GridSplitter keeps operating between these minimums — it cannot drive either pane to an
  unrecoverable size (a11y criterion E); keyboard operation (Up/Down, KeyboardIncrement)
  already works and is preserved.
- Resulting Reconstructor floor: compact chrome (~40) + tab strip 30 + 100 + 8 + 60 ≈
  **240px** — comfortably under the 450px window minimum, with slack for the runtime-growth
  rows. With chrome expanded the floor is ≈ 363px, still under 450: chrome collapse is a
  quality measure (keeps the work pane usable), not the fit mechanism.

### 4. Structural fixes for the scroll-less views

- **Three DockPanel views** restructure to three bands:
  1. configuration stack → internally scrolling region (`ScrollViewer`, inset on content
     panel per the house rule — never `Padding` on the ScrollViewer);
  2. **primary action row pinned always-visible** below it (Start/progress never leave the
     screen — judgment call approved with the design);
  3. log as the fill band with MinHeight 60, shrinkable, list scrolls.
  SampleRestorerView's DataGrid stays inside the scrolling band with its existing
  MinHeight/MaxHeight.
- **CreatorView**: stored-files row becomes resizable with MinHeight **80** (was fixed 150;
  splitter or star sizing per its existing structure); the bottom half's fixed content
  (Output + Options + Action) gains a scrolling host on the config portion with the action
  row pinned, mirroring the DockPanel-view pattern; its log keeps MinHeight 40 → unchanged
  (already minimal) but the list must remain the shrinking part.
- Any checkbox label that gains `TextWrapping` in this work MUST also take
  `Classes="wrapLabel"` (recorded binding from the glyph-centering work: centered boxes are
  wrong for multi-line labels under magnification).

### 5. GridSplitter polish (advisory items folded in as cheap wins)

- `AutomationProperties.Name` on each splitter ("Resize options and log" per view).
- A visible `:focus` style (the splitter is `Background="Transparent"` with no focus visual
  today); contrast ≥ 3:1 against both adjacent panes.

## Acceptance criteria (binding, from the a11y ruling)

A. At 700×450, on every sub-tab of every task view, every option is fully visible and
   operable via scrollbar drag, wheel, AND Tab — verified specifically for the last control
   on Reconstructor Options and Output.
B. No content renders outside its scrollable ancestor's visible clip — worst case exercised:
   custom-packer warning row visible, 150% scale.
C. After any Tab step the focused control is entirely inside the window (2.4.11) — a
   headless Tab-walk test asserts every focused control's bounds within the window at
   700×450, per view.
D. The log stays reachable at all sizes: header, Auto-scroll, Save log and the list visible
   and operable; the log shrinks to a reachable state, never a clipped one.
E. GridSplitter stays tab-reachable and Up/Down-resizable; dragging cannot drive either pane
   to an unrecoverable zero.
F. Reading order and tab order unchanged by the fix; at normal sizes all views are
   pixel-identical (frame-rig comparison for the Reconstructor as the representative view).

## Testing

- Per view: headless fit test (700×450: walk all focusable descendants, translate bounds to
  window, assert fully inside after focusing each — criterion C executable form); compact
  threshold test (class present/absent across the boundary); splitter floor test; chrome
  collapse test (prose hidden, links reachable through the expander, automation name set).
- Reconstructor frame-rig captures at normal size before/after (pixel parity), and at
  700×450 + 150% scale for the degraded state.
- Full Manager suite green on forced rebuilds (stale-XAML hazard: verify markers or
  `-t:Rebuild` before trusting any XAML-behavior probe).
- Runtime ava-desktop pass mirroring the user's VM size.

## Out of scope

- The 24px checkbox pitch (2.5.8 target-size conformance) — separate pending user decision.
- Legend tri-state text alternative (pre-existing 1.1.1 gap, tracked).
- Any change to view content or behavior at normal window sizes.

## Rollout

One plan, per-view tasks: Task order — Reconstructor (template, incl. behavior + styles +
tests), CreatorView, the three DockPanel views (one task each, sharing the band pattern),
Settings audit + splitter polish + whole-board verification. Review gates per task: codex
diff review; a11y-lead reviews this spec and the final state against criteria A–F.
