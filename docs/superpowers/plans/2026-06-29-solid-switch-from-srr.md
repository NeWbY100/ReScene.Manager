# Enable-Solid (`-s`) Switch from the SRR — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconstruct a solid original as solid — add a `-s` (enable-solid) switch, set from the SRR's solid flag on import, mutually exclusive with the existing `-s-`.

**Architecture:** A new `SwitchS` flows the same path the existing `SwitchSDash` does (VM observable → `RarSwitchSettings` → `RarCommandLineBuilder`), is set on SRR import by `SrrSwitchMapper`, round-trips through config, and is exposed as an advanced-tab checkbox. `-s` and `-s-` are radio-exclusive via the generated partial change-hooks.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm (`[ObservableProperty]` partial properties + `partial void On<Name>Changed`), xUnit.

**Spec:** `docs/superpowers/specs/2026-06-29-solid-switch-from-srr-design.md`

## Global Constraints

- **App only** (`ReScene.NET`), branch `feature/solid-reconstruction`. No `ReScene.Lib` change.
- **Build/test only with `-p:BaseOutputPath=bin2/`** (the running app locks `bin/`). NEVER kill the app.
- **Verify non-incrementally:** `dotnet build … --no-incremental` → **0 warnings, 0 errors** (strict analyzers).
- After verifying, delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- **`-s` and `-s-` are mutually exclusive** (radio pair) — never both set, never both emitted.
- **End the commit message** with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## Task 1: Add the `-s` (enable-solid) switch, driven by the SRR

**Files:**
- Modify: `ReScene.NET/ViewModels/ReconstructorViewModel.cs` (`SwitchS` observable + two exclusion hooks; `BuildSwitchSettings`; the import-apply block)
- Modify: `ReScene.NET/ViewModels/Reconstruction/RarSwitchSettings.cs` (`SwitchS`)
- Modify: `ReScene.NET/ViewModels/Reconstruction/RarCommandLineBuilder.cs` (emit `-s`/`-s-`)
- Modify: `ReScene.NET/ViewModels/Reconstruction/SrrSwitchMapper.cs` (`SwitchDiff.SwitchS` + `Map`)
- Modify: `ReScene.NET/Models/ReconstructorConfig.cs` + `ReScene.NET/ViewModels/Reconstruction/ReconstructorConfigMapper.cs` (`SwitchS` round-trip)
- Modify: `ReScene.NET/Views/ReconstructorView.xaml` (`-s` checkbox)
- Test: `ReScene.NET.Tests/SrrSwitchMapperTests.cs`, `ReScene.NET.Tests/RarCommandLineBuilderTests.cs`, `ReScene.NET.Tests/ReconstructorConfigMapperTests.cs`, and a VM exclusion test (`ReScene.NET.Tests/ReconstructorViewModelSolidTests.cs`, new).

**Interfaces:**
- Produces: `ReconstructorViewModel.SwitchS` (`bool`, observable); `RarSwitchSettings.SwitchS` (`bool`, init); `SrrSwitchMapper.SwitchDiff.SwitchS` (`bool?`); `ReconstructorConfig.SwitchS` (`bool`).

- [ ] **Step 1: Write the failing tests (RED)**

**(a) `ReScene.NET.Tests/SrrSwitchMapperTests.cs` — extend the existing solid tests.** Find the three solid tests (`Map_SolidArchiveTrue_*`, `Map_SolidArchiveFalse_*`, `Map_SolidArchiveUnknown_*`), the combined `Map_FullyPopulatedSrr_*`, and `Map_EmptySrr_AllGroupsNull`. Add `SwitchS` assertions to each (do not duplicate the tests). The added assertions:

```csharp
// In the IsSolidArchive == true test:
Assert.True(diff.SwitchS);
Assert.False(diff.SwitchSDash);

// In the IsSolidArchive == false test:
Assert.False(diff.SwitchS);
Assert.True(diff.SwitchSDash);

// In the IsSolidArchive unknown/null test AND in Map_EmptySrr_AllGroupsNull:
Assert.Null(diff.SwitchS);
```

(Read each test to place the assertion next to its existing `SwitchSDash` assertion; mirror the existing assertion style.)

**(b) `ReScene.NET.Tests/RarCommandLineBuilderTests.cs` — add `-s` cases.** Mirror the existing `BuildCommandLineArguments_SimpleSwitches_AppearInExpectedOrder` test (which builds a `RarSwitchSettings` and asserts the produced argument strings). Add:

```csharp
    [Fact]
    public void BuildCommandLineArguments_SwitchS_EmitsSolidNotDisable()
    {
        var settings = new RarSwitchSettings { Version2 = true, SwitchR = true, SwitchDS = true, SwitchS = true };

        List<RARCommandLineArgument[]> result = RarCommandLineBuilder.BuildCommandLineArguments(settings);

        string[] args = result[0].Select(a => a.Argument).ToArray();
        Assert.Contains("-s", args);
        Assert.DoesNotContain("-s-", args);
        Assert.Equal(["a", "-r", "-ds", "-s"], args);
    }

    [Fact]
    public void BuildCommandLineArguments_SwitchS_TakesPrecedenceOverSwitchSDash()
    {
        // Defense in depth: even if both reach the builder, only -s is emitted.
        var settings = new RarSwitchSettings { Version2 = true, SwitchS = true, SwitchSDash = true };

        string[] args = RarCommandLineBuilder.BuildCommandLineArguments(settings)[0].Select(a => a.Argument).ToArray();
        Assert.Contains("-s", args);
        Assert.DoesNotContain("-s-", args);
    }
```

(If the existing order test uses a different settings-construction style or `Argument` accessor, match it. `RARCommandLineArgument` exposes `.Argument` — confirmed by `LogBruteForceSettings` usage.)

**(c) `ReScene.NET.Tests/ReconstructorViewModelSolidTests.cs` (new) — VM mutual exclusion.** Build a `ReconstructorViewModel` the way `ReconstructorConfigMapperTests` does (`new(new InertBruteForceService(), new NoOpFileDialogService(), settingsService: null, uiDispatcher: new InlineUiDispatcher())` — copy the `InertBruteForceService`/`InlineUiDispatcher` test doubles or reference the existing ones if accessible):

```csharp
using ReScene.NET.ViewModels;

namespace ReScene.NET.Tests;

public class ReconstructorViewModelSolidTests
{
    // (reuse the InertBruteForceService + InlineUiDispatcher doubles used by ReconstructorConfigMapperTests)
    private static ReconstructorViewModel CreateVm() =>
        new(new InertBruteForceService(), new NoOpFileDialogService(), settingsService: null, uiDispatcher: new InlineUiDispatcher());

    [Fact]
    public void SwitchS_True_ClearsSwitchSDash()
    {
        var vm = CreateVm();
        vm.SwitchSDash = true;
        vm.SwitchS = true;
        Assert.True(vm.SwitchS);
        Assert.False(vm.SwitchSDash);
    }

    [Fact]
    public void SwitchSDash_True_ClearsSwitchS()
    {
        var vm = CreateVm();
        vm.SwitchS = true;
        vm.SwitchSDash = true;
        Assert.True(vm.SwitchSDash);
        Assert.False(vm.SwitchS);
    }
}
```

**(d) `ReScene.NET.Tests/ReconstructorConfigMapperTests.cs` — make solid a radio pair.** In `StampDistinctiveValues` (~:80) the line is `vm.SwitchAI = true; vm.SwitchR = true; vm.SwitchDS = true; vm.SwitchSDash = true;`. Change the solid part to the consistent pair `vm.SwitchS = true; vm.SwitchSDash = false;` (so the round-tripped distinctive value is `SwitchS = true`):

```csharp
        vm.SwitchAI = true; vm.SwitchR = true; vm.SwitchDS = true;
        vm.SwitchS = true; vm.SwitchSDash = false;
```

In `StampOppositeValues`, set the opposite consistent pair where it currently sets `SwitchSDash` (find the line): `vm.SwitchS = false; vm.SwitchSDash = true;`. In the assertion block (~:193) replace `Assert.True(vm.SwitchSDash);` with:

```csharp
        Assert.True(vm.SwitchS);
        Assert.False(vm.SwitchSDash);
```

**Do NOT** leave `vm.SwitchSDash = true` next to a new `vm.SwitchS = true` — the exclusion hook clears `SwitchSDash`, which would break the round-trip assertion.

- [ ] **Step 2: Run the tests to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~SrrSwitchMapperTests|FullyQualifiedName~RarCommandLineBuilderTests|FullyQualifiedName~ReconstructorViewModelSolidTests|FullyQualifiedName~ReconstructorConfigMapperTests" \
  -p:BaseOutputPath=bin2/
```
Expected: **build error** — `RarSwitchSettings.SwitchS`, `SwitchDiff.SwitchS`, `ReconstructorViewModel.SwitchS`, `ReconstructorConfig.SwitchS` don't exist (CS0117/CS1061).

- [ ] **Step 3: Add `SwitchS` to `RarSwitchSettings`**

In `ReScene.NET/ViewModels/Reconstruction/RarSwitchSettings.cs`, add next to `SwitchSDash` (`:73`):

```csharp
    public bool SwitchS { get; init; }
    public bool SwitchSDash { get; init; }
```

- [ ] **Step 4: Emit `-s` in `RarCommandLineBuilder`**

In `ReScene.NET/ViewModels/Reconstruction/RarCommandLineBuilder.cs`, replace the `-s-` block (`:286-289`):

```csharp
                                        if (s.SwitchSDash)
                                        {
                                            switches.Add(new("-s-", 201));
                                        }
```

with (emit exactly one, `-s` first):

```csharp
                                        if (s.SwitchS)
                                        {
                                            switches.Add(new("-s", 200));
                                        }
                                        else if (s.SwitchSDash)
                                        {
                                            switches.Add(new("-s-", 201));
                                        }
```

- [ ] **Step 5: Add `SwitchS` observable + exclusion hooks + `BuildSwitchSettings` in the view-model**

In `ReScene.NET/ViewModels/ReconstructorViewModel.cs`:

(i) Add the observable next to `SwitchSDash` (`:457`):

```csharp
    [ObservableProperty] public partial bool SwitchS { get; set; }
    [ObservableProperty] public partial bool SwitchSDash { get; set; }
```

(ii) Add the two radio-exclusion hooks (place near the other `partial void On…Changed` hooks, e.g. by `OnStopOnFirstMatchChanged`):

```csharp
    partial void OnSwitchSChanged(bool value)
    {
        if (value)
        {
            SwitchSDash = false;
        }
    }

    partial void OnSwitchSDashChanged(bool value)
    {
        if (value)
        {
            SwitchS = false;
        }
    }
```

(iii) In `BuildSwitchSettings()` add `SwitchS = SwitchS,` next to `SwitchSDash = SwitchSDash,` (`:1815`):

```csharp
        SwitchS = SwitchS,
        SwitchSDash = SwitchSDash,
```

(iv) In the import-apply block (`:2177-2181`), apply `SwitchS` **before** `SwitchSDash` and log the solid state (the current block logs nothing — add parity logging):

```csharp
        // Solid archive
        if (diff.SwitchS is { } switchS)
        {
            SwitchS = switchS;
        }

        if (diff.SwitchSDash is { } switchSDash)
        {
            SwitchSDash = switchSDash;
        }

        if (diff.SwitchS is { } || diff.SwitchSDash is { })
        {
            Log(LogTarget.System, SwitchS ? "Solid archiving: -s" : "Solid archiving: -s-");
        }
```

- [ ] **Step 6: Set `SwitchS` on import in `SrrSwitchMapper`**

In `ReScene.NET/ViewModels/Reconstruction/SrrSwitchMapper.cs`:

(i) Add `bool? SwitchS` to `SwitchDiff` immediately before `SwitchSDash` (`:41-45`):

```csharp
    public readonly record struct SwitchDiff(
        CompressionMap? Compression,
        DictionaryMap? Dictionary,
        bool? SwitchS,
        bool? SwitchSDash,
        FormatMap? Format);
```

(ii) In `Map` (`:50-54`), set the pair from `IsSolidArchive` (named args, so order is safe):

```csharp
    public static SwitchDiff Map(SRRFile srr) => new(
        Compression: MapCompression(srr),
        Dictionary: MapDictionary(srr),
        SwitchS: srr.IsSolidArchive,
        SwitchSDash: srr.IsSolidArchive.HasValue ? !srr.IsSolidArchive.Value : null,
        Format: MapFormat(srr));
```

(`srr.IsSolidArchive` is already `bool?` — `true`→solid, `false`→non-solid, `null`→unknown — so `SwitchS: srr.IsSolidArchive` and `SwitchSDash: !srr.IsSolidArchive` give the consistent pair, both `null` when unknown.)

- [ ] **Step 7: Round-trip `SwitchS` through config**

In `ReScene.NET/Models/ReconstructorConfig.cs`, add next to `SwitchSDash` (`:78`):

```csharp
    public bool SwitchS { get; set; }
    public bool SwitchSDash { get; set; }
```

In `ReScene.NET/ViewModels/Reconstruction/ReconstructorConfigMapper.cs`: in `Capture` add `SwitchS = vm.SwitchS,` next to the `SwitchSDash = vm.SwitchSDash,` line (`:73`); in `Apply` add `vm.SwitchS = c.SwitchS;` immediately **before** `vm.SwitchSDash = c.SwitchSDash;` (`:155`) so the "true one first" order holds for a solid config.

- [ ] **Step 8: Add the `-s` checkbox to the advanced tab**

In `ReScene.NET/Views/ReconstructorView.xaml`, add immediately **above** the existing `-s-` checkbox (`:276`):

```xml
                        <CheckBox Content="-s: Solid archiving." IsChecked="{Binding SwitchS}" Margin="0,1" />
                        <CheckBox Content="-s-: Disable solid archiving." IsChecked="{Binding SwitchSDash}" Margin="0,1" />
```

- [ ] **Step 9: Run the tests (GREEN) + clean build + full suite**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~SrrSwitchMapperTests|FullyQualifiedName~RarCommandLineBuilderTests|FullyQualifiedName~ReconstructorViewModelSolidTests|FullyQualifiedName~ReconstructorConfigMapperTests" \
  -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.NET/ReScene.NET.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: focused tests pass; **0 Warning(s) 0 Error(s)**; full app suite green.

- [ ] **Step 10: Commit**

```bash
cd E:/Projects/ReScene.NET
git add ReScene.NET/ViewModels/ReconstructorViewModel.cs \
        ReScene.NET/ViewModels/Reconstruction/RarSwitchSettings.cs \
        ReScene.NET/ViewModels/Reconstruction/RarCommandLineBuilder.cs \
        ReScene.NET/ViewModels/Reconstruction/SrrSwitchMapper.cs \
        ReScene.NET/Models/ReconstructorConfig.cs \
        ReScene.NET/ViewModels/Reconstruction/ReconstructorConfigMapper.cs \
        ReScene.NET/Views/ReconstructorView.xaml \
        ReScene.NET.Tests/SrrSwitchMapperTests.cs \
        ReScene.NET.Tests/RarCommandLineBuilderTests.cs \
        ReScene.NET.Tests/ReconstructorConfigMapperTests.cs \
        ReScene.NET.Tests/ReconstructorViewModelSolidTests.cs
git commit -m "$(cat <<'EOF'
feat(reconstructor): enable solid (-s) from the SRR's solid flag

Add a SwitchS (-s) toggle, mutually exclusive with -s- via the change-hooks,
emitted by RarCommandLineBuilder (-s takes precedence). SrrSwitchMapper sets the
pair from srr.IsSolidArchive on import, so a solid original is now reconstructed
solid instead of defaulting to non-solid. Config round-trips SwitchS; advanced
tab gains a "-s: Solid archiving." checkbox.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification (after the task)

- [ ] Clean non-incremental build of `ReScene.NET` with `-p:BaseOutputPath=bin2/`: **0 warnings, 0 errors**.
- [ ] Full `ReScene.NET.Tests` run: **0 failures**.
- [ ] Delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- [ ] **Manual:** import a solid-release SRR → the advanced tab shows `-s` checked, `-s-` unchecked, and the System log shows `Solid archiving: -s`; import a non-solid SRR → `-s-` checked; toggling one checkbox unchecks the other.

## Notes on cross-cutting concerns

- **Mutual exclusion:** enforced both in the VM (hooks) and the builder (`if/else if`). The mapper always emits a consistent pair, so applying both on import converges regardless of order.
- **Back-compat:** old config JSON without `SwitchS` deserializes to `false`; `SwitchSDash` still loads; a previously-saved solid-release config is improved on re-import, never made worse.
- **No wizard change:** the Beginner wizard reuses the same VM + import path, so it picks up `SwitchS` automatically.
- **YAGNI:** solid is not brute-forced as a dimension (the SRR is authoritative); only the compression version/method axis remains separate and out of scope.
