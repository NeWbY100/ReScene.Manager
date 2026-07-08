# ReScene Manager Port — Phase 0: Foundation & Shared Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the `ReScene.App.Core` (UI-framework-free) and `ReScene.Manager` (Avalonia) projects side-by-side with the WPF app, migrate the entire non-View layer into `App.Core` behind framework-neutral seam interfaces, land the OS-aware WinRAR resolver, retarget the app tests to run headless, and add a 3-OS CI guardrail — all while the WPF app keeps building and running.

**Architecture:** Extract the platform-agnostic ViewModel/service/model/helper layer into `ReScene.App.Core` (`net10.0`, references neither WPF nor Avalonia). Seam interfaces (`IUiDispatcher`, `IFileDialogService`, `ILauncherService`, `IImageLoader`, `ITaskbarProgress`) live in `App.Core`; their WPF implementations stay in the WPF head. This lets the WPF app and (later) `ReScene.Manager` both consume the same core, and lets `ReScene.NET.Tests` drop `-windows` and run on Linux/macOS CI.

**Tech Stack:** .NET 10, C#, Avalonia 11.3.18 (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`), CommunityToolkit.Mvvm 8.4.2, xUnit + `Avalonia.Headless.XUnit`, GitHub Actions.

## Global Constraints

- `ReScene.App.Core` MUST NOT reference Avalonia or WPF (`UseWPF` absent; no `-windows` TFM). It is `net10.0`, no `System.Windows.*` / `Avalonia.*` in its compiled surface.
- The WPF app (`ReScene.NET`) MUST remain buildable and runnable after every task — it is the live reference until cutover.
- Avalonia version floor is exactly **11.3.18** (matches the avalonia-agent-mcp bridge).
- Zero-warning build gate: the full `.slnx` builds clean. Build with `-p:BaseOutputPath=bin2/` only (the running WPF app locks `bin/`); never kill the app; delete `bin2` dirs after building.
- Build the full solution (`ReScene.NET.slnx`), not individual csprojs — it now contains: `ReScene.Lib`, `ReScene.Tests` (lib), `ReScene.NET` (WPF), `ReScene.NET.Tests`, `ReScene.Cli`, and the two new projects.
- Seam interfaces expose **no** framework types (no `System.Windows.Threading.DispatcherPriority`, no `Avalonia.*`). Priorities/handles are framework-neutral enums/abstractions defined in `App.Core`.
- Acronym casing per `docs/coding-guidelines.md` (all-caps `SRR`/`RAR`/`MP3`/`SRS`/…; `Flac`/`Riff`/`Vob` PascalCase).
- One top-level type per file.
- Settings folder: `ReScene.Manager` writes to `%LOCALAPPDATA%\ReScene.Manager` (new folder, no migration) — but that literal change lands in the Avalonia head later; `App.Core`'s `JsonFileStore` takes the folder name as a constructor/config value so each head supplies its own.

---

### Task 1: Create `ReScene.App.Core` project and add to the solution

**Files:**
- Create: `ReScene.App.Core/ReScene.App.Core.csproj`
- Create: `ReScene.App.Core/_ProjectMarker.cs` (temporary, removed in Task 3)
- Modify: `ReScene.NET.slnx` (add project)

**Interfaces:**
- Produces: an empty `net10.0` class library `ReScene.App.Core` (root namespace `ReScene.App.Core`) referencing `ReScene.Lib` and `CommunityToolkit.Mvvm`.

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ReScene.App.Core</RootNamespace>
    <!-- Guardrail: fail the build if WPF or Avalonia ever leak in -->
    <UseWPF>false</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <Using Include="System.IO" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <ProjectReference Include="..\ReScene.Lib\ReScene\ReScene.csproj" />
  </ItemGroup>
</Project>
```

Note the explicit `<Using Include="System.IO" />` — this project's SDK config does not include `System.IO` in implicit usings (same gotcha as the other projects; omitting it breaks the build with CS0103 on `Path`/`File`/`Directory`).

- [ ] **Step 2: Add a temporary marker type** so the project compiles to an assembly.

```csharp
namespace ReScene.App.Core;

internal static class ProjectMarker
{
    public const string Name = "ReScene.App.Core";
}
```

- [ ] **Step 3: Add the project to `ReScene.NET.slnx`** (add a `<Project Path="ReScene.App.Core/ReScene.App.Core.csproj" />` entry alongside the others).

- [ ] **Step 4: Build the solution and verify**

Run: `dotnet build ReScene.NET.slnx -p:BaseOutputPath=bin2/`
Expected: build succeeds, 0 warnings. Then delete `bin2` dirs.

- [ ] **Step 5: Commit**

```bash
git add ReScene.App.Core ReScene.NET.slnx
git commit -m "feat(port): add empty ReScene.App.Core (net10.0, UI-free) to solution"
```

---

### Task 2: Move `IUiDispatcher` to `App.Core` with a framework-neutral priority

**Files:**
- Create: `ReScene.App.Core/Services/IUiDispatcher.cs` (moved + neutralized)
- Create: `ReScene.App.Core/Services/UiDispatcherPriority.cs` (new neutral enum)
- Delete: `ReScene.NET/Services/IUiDispatcher.cs`
- Modify: `ReScene.NET/Services/WpfDispatcher.cs` (map neutral enum ↔ `System.Windows.Threading.DispatcherPriority`)
- Modify call sites: `ReScene.NET/ViewModels/FileCompareViewModel.cs:533`, `ReScene.NET/ViewModels/ReconstructorViewModel.cs` (the `DispatcherPriority.Background` usage)
- Modify test fakes (7 files): the fake dispatchers in `ReScene.NET.Tests` (`ReconstructorViewModelDialogTests`, `ReconstructorConfigMapperTests`, `ReconstructorViewModelArchiveSetTests`, `ReconstructorViewModelSolidTests`, `ReconstructorViewModelVersionsTests`, `SRSReconstructorViewModelTests`, plus any `InlineUiDispatcher`/`SynchronousUiDispatcher`/`QueueingUiDispatcher` definitions)

**Interfaces:**
- Produces: `ReScene.App.Core.Services.IUiDispatcher` whose methods take `UiDispatcherPriority` (neutral) instead of `System.Windows.Threading.DispatcherPriority`.
- Consumes (Task 1): the `App.Core` project.

- [ ] **Step 1: Read the current interface and its priority usage.** Read `ReScene.NET/Services/IUiDispatcher.cs`, `WpfDispatcher.cs`, and grep `DispatcherPriority` across the app + tests to enumerate every value used (the survey found `Background`; confirm the full set before defining the enum).

- [ ] **Step 2: Define the neutral enum** in `App.Core`, covering exactly the priority values actually used (start minimal — YAGNI):

```csharp
namespace ReScene.App.Core.Services;

/// <summary>Framework-neutral UI-dispatch priority; mapped to the platform priority by each head.</summary>
public enum UiDispatcherPriority
{
    Normal,
    Background,
}
```

- [ ] **Step 3: Move `IUiDispatcher` into `App.Core`**, replacing the `System.Windows.Threading.DispatcherPriority` parameter type with `UiDispatcherPriority`, and dropping the `using System.Windows.Threading;`. Keep the same method names/shape otherwise.

- [ ] **Step 4: Update `WpfDispatcher`** (stays in the WPF head) to implement the moved interface and map `UiDispatcherPriority` → `System.Windows.Threading.DispatcherPriority` (`Normal`→`Normal`, `Background`→`Background`).

- [ ] **Step 5: Update the two VM call sites and the 7 test fakes** to the neutral enum and the new namespace.

- [ ] **Step 6: Build + run the affected tests**

Run: `dotnet build ReScene.NET.slnx -p:BaseOutputPath=bin2/` then
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/`
Expected: build clean; dispatcher-dependent tests pass. Delete `bin2`.

- [ ] **Step 7: Commit**

```bash
git add ReScene.App.Core ReScene.NET ReScene.NET.Tests
git commit -m "refactor(port): move IUiDispatcher to App.Core with neutral UiDispatcherPriority"
```

---

### Task 3: Migrate the clean non-View layer (~115 files) into `App.Core`

**Files:**
- Move (via `git mv`, preserving history): every `.cs` under `ReScene.NET/{ViewModels,Services,Models,Helpers}` that does **not** appear in the leaky-15 list (see below) into the mirrored path under `ReScene.App.Core/`.
- Modify: namespace declarations `ReScene.NET.*` → `ReScene.App.Core.*` in every moved file, and `using` statements in remaining WPF files that reference them.
- Delete: `ReScene.App.Core/_ProjectMarker.cs`
- Modify: `ReScene.NET/ReScene.NET.csproj` (add `<ProjectReference>` to `App.Core` if not already present from Task 2).

**The leaky-14 that STAY in the WPF head this task** (handled in Tasks 4–5; the 15th leaky file, `Services/IUiDispatcher.cs`, already moved in Task 2): `ViewModels/ReconstructorViewModel.cs`, `ViewModels/MainWindowViewModel.cs`, `ViewModels/FilePreviewViewModel.cs`, `ViewModels/FileCompareViewModel.cs`, `Services/WpfDispatcher.cs`, `Services/ImagePreviewService.cs`, `Services/FilePreviewService.cs`, `Services/FileDialogService.cs`, `Helpers/TextBoxDropHelper.cs`, `Helpers/ProgressWindowLifecycle.cs`, `Helpers/ListBoxAutoScroll.cs`, `Helpers/IsoProgressWindowController.cs`, `Helpers/ImageDecoder.cs`, `Helpers/DarkTitleBar.cs`. (`Services/IUiDispatcher.cs` already moved in Task 2.)

**Interfaces:**
- Produces: the full clean VM/service/model/helper layer under `ReScene.App.Core.*` namespaces.

- [ ] **Step 1: Generate the move-list.** Run `git ls-files 'ReScene.NET/ViewModels/*.cs' 'ReScene.NET/ViewModels/**/*.cs' 'ReScene.NET/Services/*.cs' 'ReScene.NET/Models/*.cs' 'ReScene.NET/Helpers/*.cs'` and subtract the leaky-14 above. The remainder (~115) is the move-list.

- [ ] **Step 2: `git mv` each file** to `ReScene.App.Core/<same-subpath>`. (Windows case-insensitive FS: rely on `git mv`, not disk copy.)

- [ ] **Step 3: Rewrite namespaces** in moved files: `namespace ReScene.NET.ViewModels` → `namespace ReScene.App.Core.ViewModels`, etc. Update `using ReScene.NET.{ViewModels,Services,Models,Helpers}` references across the WHOLE app (Views, App.xaml.cs, DI wiring) and tests to the new namespaces. (A leaky VM that stays in the head but now consumes a moved service needs its `using` updated.)

- [ ] **Step 4: Remove the marker** (`ReScene.App.Core/_ProjectMarker.cs`).

- [ ] **Step 5: Build + full test run**

Run: `dotnet build ReScene.NET.slnx -p:BaseOutputPath=bin2/` then
`dotnet test ReScene.NET.slnx -p:BaseOutputPath=bin2/`
Expected: build clean (0 warnings); all existing tests pass. Delete `bin2`.

- [ ] **Step 6: Verify the WPF app still runs.** Manual/MCP smoke: launch `ReScene.NET`, confirm the shell opens and one tab renders (guards against a broken DI move).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(port): migrate clean VM/service/model/helper layer into App.Core"
```

---

### Task 4: Decouple the 4 leaky ViewModels behind `App.Core` abstractions and move them

**Files:**
- Create: `ReScene.App.Core/Services/IImageLoader.cs` (returns a neutral image handle), `ReScene.App.Core/Services/ITaskbarProgress.cs` (neutral progress state enum + interface)
- Create: `ReScene.NET/Services/WpfImageLoader.cs`, `ReScene.NET/Services/WpfTaskbarProgress.cs` (WPF impls in the head)
- Move + de-WPF: `FilePreviewViewModel.cs`, `MainWindowViewModel.cs`, `FileCompareViewModel.cs`, `ReconstructorViewModel.cs` → `ReScene.App.Core/ViewModels/`
- Modify: DI registration in `ReScene.NET/App.xaml.cs` to bind the new interfaces to the WPF impls; the Views binding to `BitmapSource`/taskbar.

**Interfaces:**
- Produces: `IImageLoader` (`ImageHandle Load(string path)` / `Load(Stream)` where `ImageHandle` wraps an `object` payload the head casts to `BitmapSource`/Avalonia `Bitmap`), `ITaskbarProgress` (`TaskbarProgressState` neutral enum: `None/Normal/Indeterminate/Error/Paused`; `Set(state, value)`).
- Consumes: Task 2's `IUiDispatcher`.

- [ ] **Step 1: `FilePreviewViewModel`** — replace the `System.Windows.Media.Imaging.BitmapSource` property with a neutral `ImageHandle?` from `IImageLoader`; the View resolves the handle to a WPF `BitmapSource` in a converter/code-behind (temporary WPF-side shim). Move the VM to `App.Core`.

- [ ] **Step 2: `MainWindowViewModel`** — replace the `System.Windows.Shell.TaskbarItemProgressState` property with the neutral `TaskbarProgressState` via `ITaskbarProgress`; `WpfTaskbarProgress` maps to the WPF shell type and the `TaskbarItemInfo` binding stays in `MainWindow.xaml`. Move the VM to `App.Core`.

- [ ] **Step 3: `FileCompareViewModel` + `ReconstructorViewModel`** — their only leak was `DispatcherPriority` (already neutral after Task 2). Drop the now-unused `using System.Windows.Threading;`, confirm no other `System.Windows.*` remains (grep), and move both to `App.Core`.

- [ ] **Step 4: Register impls** in `App.xaml.cs` DI: `IImageLoader→WpfImageLoader`, `ITaskbarProgress→WpfTaskbarProgress`.

- [ ] **Step 5: Build + test + WPF smoke**

Run: `dotnet build ReScene.NET.slnx -p:BaseOutputPath=bin2/` then `dotnet test ReScene.NET.slnx -p:BaseOutputPath=bin2/`.
Expected: clean; all tests pass. Launch WPF app, open File Preview + Reconstructor (taskbar progress) to confirm the shims work. Delete `bin2`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(port): decouple 4 leaky VMs behind IImageLoader/ITaskbarProgress; move to App.Core"
```

---

### Task 5: Introduce `IFileDialogService` / `ILauncherService` interfaces in `App.Core`

**Files:**
- Create: `ReScene.App.Core/Services/IFileDialogService.cs`, `ReScene.App.Core/Services/ILauncherService.cs`
- Modify: `ReScene.NET/Services/FileDialogService.cs` → implement the moved interface (stays in head; renames to `WpfFileDialogService` if a VM referenced the concrete type — otherwise keep name); add `ReScene.NET/Services/WpfLauncherService.cs` wrapping the 5 `Process.Start` sites.
- Modify: any moved VM that referenced the concrete `FileDialogService` now depends on the `App.Core` interface; DI in `App.xaml.cs` binds interface→WPF impl.
- Leave in the WPF head (View-adjacent, not needed by `App.Core`): `DarkTitleBar`, `ProgressWindowLifecycle`, `IsoProgressWindowController`, `TextBoxDropHelper`, `ListBoxAutoScroll`, `ImageDecoder`, `ImagePreviewService`, `FilePreviewService` (the last two implement `IImageLoader`/preview behind Task 4's interface).

**Interfaces:**
- Produces: `IFileDialogService` (open/save/folder returning paths), `ILauncherService` (`OpenUrl(string)`, `RevealInFileManager(string)`).

- [ ] **Step 1: Extract `IFileDialogService`** covering exactly the methods VMs call (open file, save file, open folder — the survey found `OpenFolderAsync` used for the WinRAR versions dir). Put it in `App.Core`; make the WPF `FileDialogService` implement it. Update VM dependencies + DI.

- [ ] **Step 2: Extract `ILauncherService`**, move the 5 `Process.Start UseShellExecute` call sites (`AboutWindow.xaml.cs:23`, `HomeViewModel.cs:90`, `ReconstructorViewModel.cs:2002`, `MainWindow.xaml.cs:118`, `ReconstructorView.xaml.cs:53`) behind it — VM sites call the interface; View-code-behind sites call the WPF impl directly. `WpfLauncherService` uses `Process.Start`.

- [ ] **Step 3: Build + test + WPF smoke** (open a file dialog, click an external link).

Run the standard build+test; delete `bin2`.

- [ ] **Step 4: Verify `App.Core` is WPF-free.** Run `grep -rn "System.Windows" ReScene.App.Core` → expect **zero** matches. This is the phase's core invariant.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(port): dialog + launcher seams in App.Core; App.Core verified WPF-free"
```

---

### Task 6: OS-aware WinRAR executable resolver across all 5 sites

**Files:**
- Create: `ReScene.Lib/ReScene/Core/RarExecutable.cs` (the resolver; lib-level so app + CLI + engine share it)
- Modify: `ReScene.Lib/ReScene/Core/RARVersionSelector.cs:155/158`, `ReScene.Lib/ReScene/Core/Manager.cs:582`, `ReScene.Lib/ReScene/Core/CommentPhaseBruteForcer.cs:119`
- Modify: `ReScene.NET/.../WinRARVersionScanner.cs:23` (now in `App.Core` after Task 3 — adjust path), `ReconstructorViewModel.cs:359` (in `App.Core`)
- Test: `ReScene.Lib/ReScene.Tests/RarExecutableTests.cs`

**Interfaces:**
- Produces: `public static class RarExecutable { public static string FileName { get; } // "rar.exe" on Windows, "rar" elsewhere }` and `public static string ResolveIn(string versionDir)` returning the combined path.

- [ ] **Step 1: Write the failing test**

```csharp
public class RarExecutableTests
{
    [Fact]
    public void FileName_IsRarExe_OnWindows_AndRar_Elsewhere()
    {
        var expected = OperatingSystem.IsWindows() ? "rar.exe" : "rar";
        Assert.Equal(expected, RarExecutable.FileName);
    }

    [Fact]
    public void ResolveIn_CombinesVersionDirWithPlatformBinary()
    {
        var dir = Path.Combine("some", "ver");
        Assert.Equal(Path.Combine(dir, RarExecutable.FileName), RarExecutable.ResolveIn(dir));
    }
}
```

- [ ] **Step 2: Run it, verify it fails** (`RarExecutable` not defined).

Run: `dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj --filter RarExecutableTests -p:BaseOutputPath=bin2/`
Expected: FAIL (compile error / not defined).

- [ ] **Step 3: Implement `RarExecutable`**

```csharp
namespace ReScene.Core; // match the lib's Core namespace

public static class RarExecutable
{
    public static string FileName { get; } = OperatingSystem.IsWindows() ? "rar.exe" : "rar";
    public static string ResolveIn(string versionDirectory) => Path.Combine(versionDirectory, FileName);
}
```

- [ ] **Step 4: Route all 5 sites** through `RarExecutable.ResolveIn(dir)` / `RarExecutable.FileName`, replacing every literal `"rar.exe"` + `Path.Combine(dir, "rar.exe")`. At `RARVersionSelector.cs:155-160` the `File.Exists` gate now checks the platform binary, so Linux version dirs containing `rar` pass. Update the log message at `:158` to `$"{RarExecutable.FileName} not found in {dir}"`.

- [ ] **Step 5: Run tests**

Run: `dotnet test ReScene.NET.slnx -p:BaseOutputPath=bin2/`
Expected: new tests pass; no regressions. Delete `bin2`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "fix(port): OS-aware WinRAR binary (rar.exe/rar) across all 5 sites incl. RARVersionSelector gate"
```

---

### Task 7: Retarget `ReScene.NET.Tests` to run headless (drop `-windows`)

**Files:**
- Modify: `ReScene.NET.Tests/ReScene.NET.Tests.csproj` (TFM `net10.0-windows`→`net10.0`, drop `UseWPF`; add `Avalonia.Headless.XUnit` 11.3.18 + `Avalonia.Themes.Fluent`; keep the explicit `<Using Include="System.IO" />`)
- Modify: `ReScene.NET.Tests/FilePreviewViewModelTests.cs` (WPF `BitmapSource` → Avalonia `WriteableBitmap` under `[AvaloniaTest]`)
- Possibly modify: the test project's ProjectReference — reference `ReScene.App.Core` (for VM/service tests) and drop the `ReScene.NET` (WPF app) reference IF no remaining test needs a WPF type. Confirm by build.

**Interfaces:**
- Consumes: `App.Core` (now holds the VMs/services under test).

- [ ] **Step 1: Retarget the csproj** and add Avalonia.Headless packages. Point ProjectReference at `App.Core`; attempt to drop the WPF-app reference.

- [ ] **Step 2: Port `FilePreviewViewModelTests`** — build test images with Avalonia `WriteableBitmap` (Skia, headless) instead of `BitmapSource.Create`; annotate the fixture with the Avalonia headless test attribute so the Skia backend is available.

- [ ] **Step 3: Run the FULL app test suite on this machine**

Run: `dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/`
Expected: all ~47 tests pass with the `net10.0` (non-windows) TFM. Delete `bin2`.

- [ ] **Step 4: Commit**

```bash
git add ReScene.NET.Tests
git commit -m "test(port): retarget app tests to net10.0 headless (Avalonia.Headless.XUnit); port FilePreview test"
```

---

### Task 8: Create the `ReScene.Manager` Avalonia project (empty Fluent-dark shell) + wire the MCP bridge

**Files:**
- Create: `ReScene.Manager/ReScene.Manager.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `Views/MainWindow.axaml`, `Views/MainWindow.axaml.cs`, `app.ico` (reuse the WPF `.ico`)
- Modify: `ReScene.NET.slnx` (add project)

**Interfaces:**
- Produces: a launchable Avalonia desktop app referencing `App.Core`, showing an empty Fluent-dark `MainWindow` titled "ReScene Manager". No tabs yet (those are Phase 4).

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
    <ApplicationIcon>app.ico</ApplicationIcon>
    <RootNamespace>ReScene.Manager</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.18" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.18" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.18" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.18" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.3.18" Condition="'$(Configuration)' == 'Debug'" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ReScene.App.Core\ReScene.App.Core.csproj" />
  </ItemGroup>
</Project>
```

(No `<ApplicationManifest>` block that pins `supportedOS` to Windows — use a minimal manifest or omit; keep the `.ico` as the window icon cross-platform.)

- [ ] **Step 2: `Program.cs`** — standard Avalonia entry point with `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`.

- [ ] **Step 3: `App.axaml`** — `FluentTheme` with `RequestedThemeVariant="Dark"`; include `Avalonia.Fonts.Inter`.

- [ ] **Step 4: `MainWindow.axaml`** — empty window, `Title="ReScene Manager"`, `Width/Height` matching the WPF default, a placeholder `TextBlock` "ReScene Manager — port in progress".

- [ ] **Step 5: Wire the avalonia-agent-mcp bridge** — add a Debug-conditional ProjectReference to `E:\Projects\avalonia-agent-mcp\AvaDevBridge` and call `.AttachAgentBridge()` (or `.WithAgentBridge()` on the `AppBuilder`) inside `#if DEBUG` per the bridge README. Register the MCP server in the project MCP config so the app can be driven.

- [ ] **Step 6: Build + launch**

Run: `dotnet build ReScene.NET.slnx -p:BaseOutputPath=bin2/` then run `ReScene.Manager` (Debug). Confirm an empty dark window titled "ReScene Manager" opens. Optionally attach via the MCP bridge (`ava_attach`, `ava_screenshot`) to confirm the bridge handshake. Delete `bin2`.

- [ ] **Step 7: Commit**

```bash
git add ReScene.Manager ReScene.NET.slnx
git commit -m "feat(port): scaffold ReScene.Manager Avalonia app (empty Fluent-dark shell) + MCP bridge"
```

---

### Task 9: Add the 3-OS CI build/test guardrail (`build.yml`)

**Files:**
- Create: `.github/workflows/build.yml`

**Interfaces:**
- Produces: a push/PR workflow building the solution and running lib + app tests on windows/ubuntu/macos.

- [ ] **Step 1: Write `build.yml`**

```yaml
name: Build
on:
  push:
    branches: [ main ]
  pull_request:
jobs:
  build:
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v5
        with:
          submodules: recursive
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: '10.0.x'
          dotnet-quality: 'preview'
      # Exclude the WPF app + its head-only tests on non-Windows (WPF is Windows-only).
      # App.Core, lib, CLI, app-tests-headless run everywhere.
      - name: Build (non-Windows — skip WPF head)
        if: runner.os != 'Windows'
        run: |
          dotnet build ReScene.App.Core/ReScene.App.Core.csproj -c Release
          dotnet build ReScene.Cli/ReScene.Cli.csproj -c Release
          dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -c Release
          dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -c Release
      - name: Build + test (Windows — full solution)
        if: runner.os == 'Windows'
        run: dotnet test ReScene.NET.slnx -c Release
```

Rationale: the WPF app (`ReScene.NET`) is Windows-only and stays so until cutover, so the non-Windows legs build only the cross-platform projects (App.Core, CLI, lib) and run the now-headless app tests. Windows runs the whole solution. This is the guardrail proving App.Core + the app tests stay cross-platform from here on.

- [ ] **Step 2: Verify the non-Windows leg locally if possible** (e.g. `dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -c Release` already passed headless in Task 7). Commit and let CI confirm the ubuntu/macos legs.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "ci(port): 3-OS build/test guardrail (App.Core+CLI+lib+headless app tests)"
```

---

## Phase 0 Deliverable (definition of done)

- Solution builds clean (0 warnings) on Windows; App.Core + CLI + lib + app-tests build/pass on Linux & macOS CI.
- `ReScene.App.Core` contains the full VM/service/model/helper layer and has **zero** `System.Windows` references.
- The WPF app still builds and runs unchanged (via WPF impls of the App.Core seams).
- WinRAR reconstruction resolves `rar`/`rar.exe` per-OS (verified by unit test; Linux end-to-end proven in Phase 6).
- `ReScene.Manager` launches an empty Fluent-dark window and is drivable via the MCP bridge.
- `build.yml` guardrail is green on all three OS.

**Next phase:** Phase 1 (theme/tokens & cross-platform fonts) — separate plan, written when Phase 0 is complete.
