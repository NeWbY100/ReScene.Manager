# ReScene Manager — WPF → Avalonia Port Design

**Goal:** Port the ReScene.NET WPF desktop app to Avalonia UI so it runs cross-platform (Windows + Linux tier-1, macOS best-effort), rebranded as **ReScene Manager**, with full functional feature parity before the WPF app is retired.

**Status:** Design approved-with-changes by two independent reviewers (2026-07-08); this revision folds in their findings. No implementation has begun. **HARD-GATE: implementation only after this spec is approved by the user.**

---

## 1. Locked decisions (from brainstorming)

1. **Name:** ReScene Manager — project `ReScene.Manager`, exe `ReSceneManager`, display name "ReScene Manager". Disambiguates from the original 2008 "ReScene .NET" GUI (which pyReScene was ported from). `ReScene.Lib` keeps its name.
2. **Port setup:** side-by-side in the **same repo** — new Avalonia project beside the existing WPF project; WPF stays runnable as a reference during the port; WPF project deleted and repo renamed at cutover.
3. **Platforms:** Windows + Linux tier-1 (built & tested); macOS best-effort (binaries published, not dedicated-tested).
4. **Theme:** Avalonia `FluentTheme` (Dark) **base** + accent/token restyle — NOT a re-template of the bespoke WPF dark theme. **This intentionally changes the app's visual appearance** (see §3): parity is *functional*, not pixel-for-pixel visual.
5. **Parity bar:** full 8-tab parity (+ beginner wizard + secondary windows) before retiring WPF. One clean 1.0, no *functional* feature regression.
6. **Avalonia version floor:** 11.3.18 (matches the avalonia-agent-mcp validation bridge).

---

## 2. Survey findings (evidence-backed, reviewer-verified)

**Overall: moderate risk; ~60–65% of the app ports with little change.** The entire non-UI stack is already portable: `ReScene.Lib` (multi-targets `net8.0;net10.0`, no UI), `ReScene.Cli` (`net10.0`, no Windows deps), the bulk of the ViewModels/services, and ~47 app test files. **There are no WPF NuGet packages** — WPF comes only from `UseWPF` + the `-windows` TFM. CommunityToolkit.Mvvm and the xUnit/coverlet stack are framework-agnostic and stay.

### Friction, measured directly (grep + reviewer verification)

| Area | Verified fact | Rating |
|---|---|---|
| ~30 XAML files (~5,600 lines) → AXAML | 89 trigger *elements* (~286 raw `Trigger` string occurrences), 69 plain `<Trigger>` inside App.xaml templates Fluent replaces wholesale; App.xaml has **53 `<ControlTemplate>`** re-templating the stock control set | Moderate |
| DataGrid in **9 XAML files** (14 incl. code-behind) | **NO** GroupStyle / CollectionViewSource / RowDetails; grids are flat. **One exception:** `SampleRestorerView` is genuinely editable (`IsReadOnly=False`, checkbox + editable text column) | Moderate |
| `HexViewControl` (**831** lines, `OnRender`/`FormattedText`/`HitTestByte`/mouse+key selection, **8** DependencyProperties) | Backs the Inspector; no 1:1 Avalonia API; char metrics depend on the mono font | Hard |
| WPF-type leaks in shared VM/service layer | `IUiDispatcher.cs:18` `DispatcherPriority`; `FilePreviewViewModel.cs:58` `BitmapSource`; `MainWindowViewModel.cs:4/129` `System.Windows.Shell` taskbar progress; `FileDialogService` Microsoft.Win32; `DarkTitleBar.cs:12` DWM P/Invoke; 5 `Process.Start UseShellExecute` sites | Moderate |
| **WinRAR binary name hardcoded as `"rar.exe"` at 5 functional sites** | App: `WinRARVersionScanner.cs:23`, `ReconstructorViewModel.cs:359`. Lib: `RARVersionSelector.cs:155/158` (**hard gate — filters out every version dir lacking `rar.exe`**), `Manager.cs:582`, `CommentPhaseBruteForcer.cs:119` | **Hard — the functional (non-UI) blocker** |
| Editable ComboBox — **4** sites (`FilePreviewWindow.xaml:37`, `FileCompareView.xaml:249` & `:428`, `InspectorView.xaml:287`) | Avalonia stock ComboBox has **no** `IsEditable`, settable `Text`, or `SelectedValuePath` → needs control *replacement*, not just re-binding | Moderate |
| `RelativeSource AncestorType` — **20** sites (HomeView + 5 wizard bodies) | Maps to Avalonia ancestor binding / named-control lookup | Trivial-moderate |
| Windows-only fonts | `Tokens.xaml:10` `Segoe UI`; `:11` `Cascadia Mono, Consolas, …` — none exist on stock Linux | Moderate (see Phase 1) |

Already cross-platform, no code change: `JsonFileStore` uses `Environment.SpecialFolder.LocalApplicationData` (resolves per-OS) — **but the folder *literal* needs a rename decision, see §6.4**; the whole lib dependency set (CliWrap, Crc32.NET, DiscUtils, System.IO.Hashing).

---

## 3. Architecture

**Side-by-side with a shared, UI-framework-free core** (both reviewers endorsed this over copy-and-cut-over).

Extract the platform-agnostic layer — ViewModels, services, models, helpers — into a new project **`ReScene.App.Core`** (`net10.0`, **must NOT reference Avalonia or WPF**) referenced by BOTH the WPF app and the new `ReScene.Manager` Avalonia app. Rationale:

- Keeps WPF runnable as a live reference throughout the port.
- **Forced by the tests:** `ReScene.NET.Tests` is pinned to `net10.0-windows` + `UseWPF` today *solely* because it references the WPF app. There is no way to run the ~47 app tests headless on Linux CI until the VMs/services live in a UI-free project the tests reference instead. `App.Core` is the enabler for the Phase 0 3-OS CI guardrail.
- Forces the WPF-type leaks behind interfaces once, not duplicated across two divergent VM copies.

**Seam interfaces live in `App.Core`; platform implementations live in each head.** `App.Core` defines `IUiDispatcher` (with a framework-neutral priority enum — do NOT expose `System.Windows` or `Avalonia` types on the interface), `IFileDialogService`, `ILauncherService` (URL/folder open), and a neutral image abstraction. WPF head supplies `WpfDispatcher` / Win32 dialog / shell-exec impls; `ReScene.Manager` supplies `AvaloniaDispatcher` (`Dispatcher.UIThread`), `StorageProvider` dialogs, and `TopLevel.Launcher` impls. At cutover the WPF app is deleted; `App.Core` may be folded into `ReScene.Manager` or kept as a library.

**Stack:** Avalonia 11.3.18, `net10.0`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Controls.DataGrid`, `Avalonia.Fonts.Inter`, a bundled monospace font (§Phase 1). MVVM plumbing (CommunityToolkit.Mvvm source generators) is unchanged — the primary lever behind the clean port.

**Theme (functional-parity, not visual-parity):** `FluentTheme(Mode=Dark)` base; port `Tokens.xaml` to an Avalonia `Styles`/`Resources` dictionary (`sys:Double`→`x:Double`, DynamicResource refs re-pointed); apply only accent/token deltas via Style selectors + pseudo-classes (`:pointerover`, `:checked`, `:focus`, `:disabled`). The 53 bespoke `ControlTemplate`s are intentionally **discarded** — Fluent supplies the stock control chrome. Consequence to state plainly: **the app will look like a standard Fluent-dark app, not the current bespoke dark theme.** View-level `DataTrigger`s (e.g. `FieldStatusLine`'s 7) become selectors or `IValueConverter`s.

---

## 4. Port sequence (phased; WPF stays green until the end)

**Phase 0 — Foundation + CI guardrail.** Create `ReScene.Manager` (Avalonia, `net10.0`, `OutputType Exe`, drop `UseWPF`/`-windows`/`app.manifest`, keep `.ico` as the Avalonia window icon) and `ReScene.App.Core`, both side-by-side in the repo and `.slnx`. Move the platform-agnostic VMs/services/models/helpers into `App.Core`; repoint the WPF app and `ReScene.NET.Tests` at it (tests can then drop `-windows`). Wire the avalonia-agent-mcp bridge (`#if DEBUG` + Debug-conditional ProjectReference; `.WithAgentBridge()`). **Add `build.yml` — a windows/ubuntu/macos matrix running `dotnet build` + `dotnet test` — BEFORE any UI porting** (there is currently no per-commit/PR CI, only tag-triggered `release.yml`).

**Phase 1 — Theme/tokens & fonts.** `FluentTheme(Dark)`; port `Tokens.xaml`; accent/token restyle via selectors. **Bundle a cross-platform monospace** (e.g. Cascadia Code / JetBrains Mono) and repoint `MonoFontFamily`; confirm `UIFontFamily` maps to bundled Inter (HexView column alignment is mono-metric-sensitive). Replace the single `SymbolThemeFontFamily` glyph source (App.xaml:551) with a bundled icon font or path icons; note the 7 other Unicode-emoji glyph literals across 5 files render via the system emoji font (Noto on Linux) — a benign visual nuance.

**Phase 2 — Shared VM/services seam** (in `App.Core`; WPF + tests updated in lockstep):
- **OS-aware WinRAR binary resolver.** Add a single `ResolveRarExecutable(dir)` helper (`rar.exe` on Windows, `rar` elsewhere) and route ALL five sites through it: app `WinRARVersionScanner.cs:23`, `ReconstructorViewModel.cs:359`; lib `RARVersionSelector.cs:155/158` (the version-dir gate — the decisive one), `Manager.cs:582`, `CommentPhaseBruteForcer.cs:119`. The version-folder scan must match either binary name. Add a headless test asserting the resolver picks `rar` on non-Windows. Unblocks shipping the CLI cross-platform ahead of the UI.
- `IUiDispatcher` → framework-neutral priority; `AvaloniaDispatcher` uses `Dispatcher.UIThread`; update the **7** fake-dispatcher test files together.
- `BitmapSource?` → neutral image abstraction backed by `Avalonia.Media.Imaging.Bitmap?` (drop `Freeze`; Skia decode is a net portability win); `ImageDecoder` → `new Bitmap(stream)`.
- `IFileDialogService` → Avalonia `StorageProvider` impl (needs a `TopLevel` capture). **Move `PromptWindow` forward into this phase** — `FileDialogService` depends on it.
- `ILauncherService` centralizes the 5 URL/folder opens (`TopLevel.Launcher` + platform branch for folder-reveal, which has no clean cross-platform equivalent).
- Delete `DarkTitleBar` (Fluent handles native dark titlebar). Platform-guard taskbar progress behind a no-op interface off Windows.
- Retarget `ReScene.NET.Tests` → `net10.0` + `Avalonia.Headless.XUnit`; port `FilePreviewViewModelTests` (the sole `BitmapSource` test) to `WriteableBitmap` under `[AvaloniaTest]`.

**Phase 3 — Custom controls.** `HexViewControl` (Avalonia `Control` overriding `Render(DrawingContext)`, Avalonia `FormattedText`, manual hit-test geometry, `PointerPressed/Moved`+`KeyDown`, **8** `StyledProperty`) and `FieldStatusLine` (`FieldStatus` `StyledProperty` + selector/class-driven states). Port `TextBoxDropHelper` (→ Avalonia `DragDrop`) and `ListBoxAutoScroll` as attached properties; the two visibility converters (`IndexToVisibilityConverter`, `InverseBoolToVisibilityConverter`) collapse to `IsVisible` bindings. Shared tab dependencies → land before the views.

**Phase 4 — Shell + all 8 tabs, lightest-first, each with its dependent windows.** MainWindow shell — actually **3 XAML files** (`MainWindow` + `AdvancedShellView` + `BeginnerShellView`): Advanced 8-tab `TabControl` ↔ Beginner card-hub → `WizardWindow`; drag-drop via Avalonia `DragDrop`, `KeyBindings`/`KeyGestures`, status bar. Then port each tab; **interleave the secondary window(s) each tab launches** (see Phase 5 list) so each tab is end-to-end MCP-validatable when done:
1. Home
2. SRR Creator (`CreatorView`)
3. SRS Creator (`SRSCreatorView`)
4. SRS Reconstructor (`SRSReconstructorView`)
5. SRS Restorer (`SampleRestorerView`) — **editable DataGrid**: validate Avalonia checkbox single-click toggle + cell `BeginEdit/CommitEdit`.
6. **RAR Reconstructor (`ReconstructorView`) — the heaviest tab**: WinRAR version tree, brute-force, archive-set planning; depends on the Phase-2 WinRAR resolver and on `BruteForceProgressWindow` → `FileCopyProgressWindow` + `CRCValidationProgressWindow`, and `ISOProgressWindow` (via `IsoProgressWindowController`). Land those progress windows with this tab.
7. Inspector (`InspectorView`) — HexView + editable ComboBox; depends on `FilePreviewWindow`/`ImagePreviewWindow`.
8. Compare (`FileCompareView`) — dual-pane, 2× editable ComboBox.

**Editable ComboBox replacements (4 sites):** replace with `NumericUpDown` for the bytes-per-line pickers and `AutoCompleteBox` (or a templated editable combo) for free-text; re-express the `SelectedValue`+`Text`+`SelectedValuePath` dual binding (e.g. `FileCompareView.xaml:247-257`) explicitly.

**Phase 5 — Secondary windows & wizard (9 windows + wizard bodies).** `AboutWindow`, `SettingsWindow`, `PromptWindow` (moved to Phase 2), `FilePreviewWindow`, `ImagePreviewWindow`, `BruteForceProgressWindow`, `CRCValidationProgressWindow`, `FileCopyProgressWindow`, `ISOProgressWindow` — several already interleaved into Phase 4 with their launching tab. Port the two WPF-coupled progress helpers: `ProgressWindowLifecycle.cs` (`Window.Closing` `e.Cancel` cancel-guard + button-content mutation) and `IsoProgressWindowController.cs` — both need an Avalonia window-lifecycle equivalent (`Window.Closing`/`WindowClosingEventArgs`). `WizardWindow` + **6** wizard-area XAML: the 5 bodies (`CreateSRR`, `CreateSRS`, `EditSRR`, `Reconstruct`, `Restore`) **plus `StoredFilesManagePanel`** (a UserControl hosting a DataGrid + 7 command buttons under the EditSRR flow; its `PreviewMouseDown`/`MouseDoubleClick` code-behind → Avalonia `PointerPressed`/`DoubleTapped`).

**Phase 6 — Release/cutover.** Concrete `release.yml` edits:
- Rename the publish project path (`release.yml:40` `ReScene.NET/ReScene.NET.csproj` → `ReScene.Manager/ReScene.Manager.csproj`).
- Rename the artifact/zip and switch to per-OS packaging (`:51`, `:100` `ReScene.NET-<v>-win-x64.zip` → `ReSceneManager-<v>-<rid>.{zip|tar.gz}`).
- Add the OS matrix (`:13` `runs-on` → windows/ubuntu/macos) and RIDs (`:42` `-r win-x64` → `win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`), keeping self-contained + `PublishSingleFile`, bundling native Skia via `IncludeNativeLibrariesForSelfExtract`; `chmod +x` the nix binary.
- **Leave the lib-version gate untouched** (`:57` reads `ReScene.Lib/ReScene/ReScene.csproj`, a submodule path unaffected by the app rename) — the app-only-vs-lib release logic stays correct.
- Smoke-test single-file extraction per RID; verify reconstruction on Linux (proves the WinRAR resolver). Retire the WPF project and rename the repo once 8-tab parity is confirmed.

---

## 5. Testing & validation

- **`Avalonia.Headless.XUnit`** runs VM and UI-touching tests headless on Linux CI (no display server), enabled by the `App.Core` seam. Only `FilePreviewViewModelTests` builds a WPF `BitmapSource` → port to `WriteableBitmap` under `[AvaloniaTest]`.
- **avalonia-agent-mcp bridge** (Avalonia 11.3.18) drives live visual validation of each ported view — the substitute for manual UI checking — including the **`Run.Text` gotcha** (the 3 bound sites `MainWindow.xaml:85` OneTime, `ReconstructorView.xaml:188`/`:206` OneWay are already correctly moded; Avalonia's inline `DataContext` propagation is historically flaky, so per-view smoke test regardless).
- **CI:** new `build.yml` (push/PR, 3-OS matrix) as the guardrail; extended `release.yml` (3-OS × RID publish matrix) at cutover.

---

## 6. Cutover safety

1. **Namespaces/exe/artifacts** rename `ReScene.NET`→`ReScene.Manager` / `ReSceneManager`; itemized `release.yml` edits in Phase 6.
2. **Repo rename** at cutover only, after 8-tab parity.
3. **ReScene.Lib submodule** relationship is preserved by side-by-side-in-same-repo (`.gitmodules` + `ProjectReference ..\ReScene.Lib\ReScene\ReScene.csproj` unchanged); lib release gate path untouched.
4. **User settings folder — OPEN DECISION (needs user confirmation).** `JsonFileStore.cs:17-19` persists AppSettings, RecentFiles, and WindowState to `%LOCALAPPDATA%\ReScene.NET`. Property casing is already safe (`PropertyNameCaseInsensitive`). But if the renamed app writes to a new folder (`ReScene.Manager`), every existing user silently loses settings/recent-files/window placement. **Options:** (a) keep the `ReScene.NET` folder literal unchanged post-rename (zero migration, but folder name mismatches the brand); (b) **[recommended]** one-time migration — on first run, if the new `ReScene.Manager` folder is absent and the old `ReScene.NET` folder exists, copy it over. Decision to be confirmed by the user before Phase 2.

---

## 7. Residual risks carried into the plan (not blockers)

- `PublishSingleFile` self-extraction of native Skia unproven per-RID (linux-x64/osx-x64/osx-arm64) — prove in Phase 0/6.
- macOS is build-only/untested by decision; osx-arm64 Skia + pickers unexercised until a macOS CI leg exists.
- Taskbar progress has no cross-platform equivalent — guarded no-op off Windows.
- Folder-reveal / select-in-file-manager has no clean cross-platform equivalent — platform branch in `ILauncherService`.

---

## 8. Review record

Two independent reviewers (architecture-soundness lens + scope/cutover-completeness lens) verified this design against the codebase on 2026-07-08. Both returned **approve_with_changes** and both endorsed the shared `App.Core` architecture (§3). This revision incorporates their blockers (WinRAR 5-site resolver incl. the `RARVersionSelector` gate; RAR Reconstructor tab restored to Phase 4), important findings (phase dependency-inversion fix; editable-ComboBox control replacement; SampleRestorer editable grid; cross-platform fonts; visual-vs-functional parity statement; `StoredFilesManagePanel` + progress helpers; itemized `release.yml`; settings-folder decision), and count corrections (9 secondary windows, 3 shell XAML, 8 HexView DPs, 7 fake-dispatcher test files, 9 DataGrid XAML files).
