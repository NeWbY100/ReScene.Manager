# Per-Sub-Version WinRAR Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user pick individual installed WinRAR sub-versions in the RAR Reconstructor via a grouped tri-state tree, replacing the six flat major-version checkboxes.

**Architecture:** The engine already enumerates the WinRAR versions folder and filters by `VersionRange` (`version >= Start && version < End`), so a hand-picked set is expressed as one tight `[v, v+1)` range per version — no engine algorithm change. A new pure `WinRarVersionScanner` (app) discovers installed versions using the engine's exact rules (rar.exe present + parseable name); a pure `VersionSelectionReconciler` decides which to tick from folder contents × intent; the view-model owns the async scan (latest-wins token) and the tree; a lib `TryParseRARVersion` removes an existing crash on unparseable folder names at two call sites.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm 8.4.2 (`[ObservableProperty]` partial properties, `[RelayCommand]`), xUnit. Lib is `ReScene.Lib` (a git submodule at `E:\Projects\ReScene.Lib`); app is `ReScene.NET`.

## Global Constraints

- **Build/test ONLY with `-p:BaseOutputPath=bin2/`** (the running app locks `bin/`). NEVER kill the app. Verify with `--no-incremental` → **0 warnings / 0 errors** (`AnalysisLevel=latest-All`). After verifying, delete bin2 dirs: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null` (and the same under `E:/Projects/ReScene.Lib`).
- Commit trailer on every commit: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Work on branch `feature/winrar-subversion-selection` (already created). Do not commit to `main`.
- **Submodule mechanics:** lib code commits inside `E:\Projects\ReScene.Lib`; after a lib commit, bump the app's gitlink from `E:\Projects\ReScene.NET` with `git add ReScene.Lib` + commit, so the app builds against the new lib API.
- **`gh` active account must be `NeWbY100`** (only relevant at release; no release in this plan).
- Namespaces: lib `ReScene.Core` (`Manager`, `VersionRange`); app helpers `ReScene.NET.ViewModels.Reconstruction`; config `ReScene.NET.Models`.
- Test-project gotcha: `ReScene.NET.Tests` already carries `<Using Include="System.IO" />`; the lib `ReScene.Tests` does not (the Task 1 lib test needs no I/O, so this does not arise).
- Both `ReScene.Core` and `ReScene.NET` expose internals to their test projects via `InternalsVisibleTo`.
- Label format for a leaf: `$"{version / 100}.{version % 100:D2}"` (e.g. `560` → `5.60`).
- Verify commands assume PowerShell/Git-Bash; run `dotnet` from the directory noted in each task.

---

### Task 1: Lib — `TryParseRARVersion` + harden both unguarded call sites

**Files:**
- Modify: `E:/Projects/ReScene.Lib/ReScene/Core/Manager.cs` (`ParseRARVersion` ~99-118; `CalculateBruteForceProgressSize` ~380-385; `GetValidRarDirectories` ~627-633)
- Test: `E:/Projects/ReScene.Lib/ReScene.Tests/ManagerVersionParsingTests.cs` (create)

**Interfaces:**
- Produces: `public static bool Manager.TryParseRARVersion(string rarVersionDirectoryName, out int version)` — `true` + normalised version when parseable, else `false` + `0`. `public static int Manager.ParseRARVersion(string)` keeps throwing `FormatException` on unparseable input.

- [ ] **Step 1: Write the failing test**

Create `E:/Projects/ReScene.Lib/ReScene.Tests/ManagerVersionParsingTests.cs`:

```csharp
using ReScene.Core;

namespace ReScene.Tests;

public sealed class ManagerVersionParsingTests
{
    [Theory]
    [InlineData("winrar-560", 560)]
    [InlineData("winrar-624", 624)]
    [InlineData("winrar-700", 700)]
    [InlineData("winrar-56", 560)]   // < 100 is normalised x10
    public void TryParseRARVersion_ValidNames_ReturnsNormalisedVersion(string name, int expected)
    {
        bool ok = Manager.TryParseRARVersion(name, out int version);

        Assert.True(ok);
        Assert.Equal(expected, version);
    }

    [Theory]
    [InlineData("winrar-beta")]
    [InlineData("no-digits-here")]
    [InlineData("")]
    public void TryParseRARVersion_Unparseable_ReturnsFalse(string name)
    {
        bool ok = Manager.TryParseRARVersion(name, out int version);

        Assert.False(ok);
        Assert.Equal(0, version);
    }

    [Fact]
    public void ParseRARVersion_Unparseable_Throws()
    {
        Assert.Throws<FormatException>(() => Manager.ParseRARVersion("winrar-beta"));
    }

    [Fact]
    public void ParseRARVersion_Valid_ReturnsSameAsTryParse()
    {
        Assert.Equal(560, Manager.ParseRARVersion("winrar-560"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run from `E:/Projects/ReScene.Lib`:
`dotnet test ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~ManagerVersionParsingTests"`
Expected: FAIL — `Manager.TryParseRARVersion` does not exist (compile error).

- [ ] **Step 3: Implement `TryParseRARVersion` and refactor `ParseRARVersion`**

In `Manager.cs`, replace the existing `ParseRARVersion` method (currently lines ~99-118) with:

```csharp
    public static bool TryParseRARVersion(string rarVersionDirectoryName, out int version)
    {
        version = 0;
        Match versionMatch = _rarVersionRegex.Match(rarVersionDirectoryName);
        if (!versionMatch.Success || !int.TryParse(versionMatch.Groups[1].Value, out int versionNumber))
        {
            return false;
        }

        version = versionNumber < 100 ? versionNumber * 10 : versionNumber;
        return true;
    }

    public static int ParseRARVersion(string rarVersionDirectoryName)
    {
        if (!TryParseRARVersion(rarVersionDirectoryName, out int version))
        {
            throw new FormatException(
                $"WinRAR version not found in directory name:{Environment.NewLine}{rarVersionDirectoryName}");
        }

        return version;
    }
```

(Keep the existing XML-doc comment block above `ParseRARVersion`; add a short `<summary>` for `TryParseRARVersion`. The previously-separate `InvalidDataException` branch is unreachable for regex-matched `\d+` input and is folded into the single `FormatException`.)

- [ ] **Step 4: Harden `CalculateBruteForceProgressSize`**

In `Manager.cs`, inside the `Parallel.ForEach` body (currently line ~380-381), replace:

```csharp
                    string rarVersionDirectoryName = Path.GetFileName(rarVersionDirectoryPath);
                    int version = ParseRARVersion(rarVersionDirectoryName);
                    if (!options.RAROptions.RARVersions.Any(r => r.InRange(version)))
                    {
                        return;
                    }
```

with:

```csharp
                    string rarVersionDirectoryName = Path.GetFileName(rarVersionDirectoryPath);
                    if (!TryParseRARVersion(rarVersionDirectoryName, out int version))
                    {
                        return;
                    }

                    if (!options.RAROptions.RARVersions.Any(r => r.InRange(version)))
                    {
                        return;
                    }
```

- [ ] **Step 5: Harden `GetValidRarDirectories`**

In `Manager.cs` (currently lines ~627-633), replace:

```csharp
            string dirName = Path.GetFileName(dir);
            int version = ParseRARVersion(dirName);

            if (options.RAROptions.RARVersions.Any(r => r.InRange(version)))
            {
                validDirectories.Add((dir, version));
            }
```

with:

```csharp
            string dirName = Path.GetFileName(dir);
            if (!TryParseRARVersion(dirName, out int version))
            {
                _logger.Information(this, $"Unrecognised WinRAR version folder name: {dir}");
                continue;
            }

            if (options.RAROptions.RARVersions.Any(r => r.InRange(version)))
            {
                validDirectories.Add((dir, version));
            }
```

- [ ] **Step 6: Run tests to verify they pass**

Run from `E:/Projects/ReScene.Lib`:
`dotnet test ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~ManagerVersionParsingTests"`
Expected: PASS (8 cases). Then a full lib build with no warnings:
`dotnet build ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental`
Expected: 0 warnings, 0 errors.

> **Note on call-site coverage:** The two hardened private paths are not driven by a dedicated integration test (that would require a full brute-force run against a real `rar.exe`). Their no-throw guarantee comes from the `TryParseRARVersion` unit tests (the shared guard) plus the `WinRarVersionScanner` tests in Task 2, which exercise the identical rar.exe + parse rule against real temp folders. This is an intentional, documented scope choice.

- [ ] **Step 7: Commit the lib change, then bump the app submodule pointer**

```bash
cd /e/Projects/ReScene.Lib
git add ReScene/Core/Manager.cs ReScene.Tests/ManagerVersionParsingTests.cs
git commit -m "feat: add TryParseRARVersion and harden both unguarded parse sites

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
cd /e/Projects/ReScene.NET
git add ReScene.Lib
git commit -m "chore: bump ReScene.Lib (TryParseRARVersion + parse hardening)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: App — `WinRarVersionScanner` + `InstalledRarVersion`

**Files:**
- Create: `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/WinRarVersionScanner.cs`
- Test: `E:/Projects/ReScene.NET/ReScene.NET.Tests/WinRarVersionScannerTests.cs` (create)

**Interfaces:**
- Consumes: `Manager.TryParseRARVersion` (Task 1).
- Produces: `public sealed record InstalledRarVersion(int Version, string FolderName, string Path)` and `public static IReadOnlyList<InstalledRarVersion> WinRarVersionScanner.Scan(string? folder)` — installed versions (immediate subfolders that contain `rar.exe` and parse to a version), ascending by `Version`.

- [ ] **Step 1: Write the failing test**

Create `E:/Projects/ReScene.NET/ReScene.NET.Tests/WinRarVersionScannerTests.cs`:

```csharp
using ReScene.NET.ViewModels.Reconstruction;

namespace ReScene.NET.Tests;

public sealed class WinRarVersionScannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wrvs-" + Guid.NewGuid().ToString("N"));

    public WinRarVersionScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void MakeVersion(string folderName, bool withRarExe)
    {
        string dir = Path.Combine(_root, folderName);
        Directory.CreateDirectory(dir);
        if (withRarExe)
        {
            File.WriteAllText(Path.Combine(dir, "rar.exe"), "stub");
        }
    }

    [Fact]
    public void Scan_NullOrMissingFolder_ReturnsEmpty()
    {
        Assert.Empty(WinRarVersionScanner.Scan(null));
        Assert.Empty(WinRarVersionScanner.Scan(""));
        Assert.Empty(WinRarVersionScanner.Scan(Path.Combine(_root, "does-not-exist")));
    }

    [Fact]
    public void Scan_IncludesOnlyFoldersWithRarExeAndParseableName_SortedAscending()
    {
        MakeVersion("winrar-624", withRarExe: true);
        MakeVersion("winrar-560", withRarExe: true);
        MakeVersion("winrar-590", withRarExe: false);  // no rar.exe -> excluded
        MakeVersion("winrar-beta", withRarExe: true);  // unparseable -> excluded (no throw)

        IReadOnlyList<InstalledRarVersion> result = WinRarVersionScanner.Scan(_root);

        Assert.Equal(new[] { 560, 624 }, result.Select(r => r.Version).ToArray());
        Assert.Equal("winrar-560", result[0].FolderName);
    }

    [Fact]
    public void Scan_TwoDigitName_NormalisedToThreeDigits()
    {
        MakeVersion("winrar-56", withRarExe: true);

        IReadOnlyList<InstalledRarVersion> result = WinRarVersionScanner.Scan(_root);

        Assert.Single(result);
        Assert.Equal(560, result[0].Version);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~WinRarVersionScannerTests"`
Expected: FAIL — `WinRarVersionScanner` does not exist.

- [ ] **Step 3: Implement the scanner**

Create `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/WinRarVersionScanner.cs`:

```csharp
using ReScene.Core;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>One installed WinRAR version folder that the brute-force engine would accept.</summary>
public sealed record InstalledRarVersion(int Version, string FolderName, string Path);

/// <summary>
/// Enumerates the installed WinRAR sub-versions in the WinRAR versions folder, applying the same
/// rules the engine uses (<see cref="Manager.GetValidRarDirectories"/>): an immediate subfolder
/// counts only if it contains <c>rar.exe</c> and its name parses to a version. Pure and
/// I/O-only; the view-model calls it off the UI thread.
/// </summary>
public static class WinRarVersionScanner
{
    public static IReadOnlyList<InstalledRarVersion> Scan(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        List<InstalledRarVersion> found = [];
        foreach (string dir in Directory.GetDirectories(folder))
        {
            if (!File.Exists(Path.Combine(dir, "rar.exe")))
            {
                continue;
            }

            string name = Path.GetFileName(dir);
            if (!Manager.TryParseRARVersion(name, out int version))
            {
                continue;
            }

            found.Add(new InstalledRarVersion(version, name, dir));
        }

        return found.OrderBy(v => v.Version).ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~WinRarVersionScannerTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
cd /e/Projects/ReScene.NET
git add ReScene.NET/ViewModels/Reconstruction/WinRarVersionScanner.cs ReScene.NET.Tests/WinRarVersionScannerTests.cs
git commit -m "feat: add WinRarVersionScanner for installed WinRAR sub-versions

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: App — `RarVersionLeaf` + `RarVersionGroup` tree nodes

**Files:**
- Create: `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/RarVersionLeaf.cs`
- Create: `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/RarVersionGroup.cs`
- Test: `E:/Projects/ReScene.NET/ReScene.NET.Tests/RarVersionTreeTests.cs` (create)

**Interfaces:**
- Produces:
  - `public sealed partial class RarVersionLeaf : ObservableObject` with ctor `(int version, string folderName)`, read-only `int Version`, `string FolderName`, `string Label`, and `[ObservableProperty] bool IsChecked`.
  - `public sealed partial class RarVersionGroup : ObservableObject` with ctor `(int major, IReadOnlyList<RarVersionLeaf> leaves)`, read-only `int Major`, `string Header`, `IReadOnlyList<RarVersionLeaf> Leaves`, computed `bool? IsChecked`, computed `string CountText`, `event EventHandler? SelectionChanged`, `[RelayCommand] void ToggleAll()`, and `void Detach()`.

- [ ] **Step 1: Write the failing test**

Create `E:/Projects/ReScene.NET/ReScene.NET.Tests/RarVersionTreeTests.cs`:

```csharp
using ReScene.NET.ViewModels.Reconstruction;

namespace ReScene.NET.Tests;

public sealed class RarVersionTreeTests
{
    private static RarVersionGroup MakeGroup(int major, params (int v, bool ticked)[] leaves)
    {
        var list = leaves
            .Select(l => new RarVersionLeaf(l.v, $"winrar-{l.v}") { IsChecked = l.ticked })
            .ToList();
        return new RarVersionGroup(major, list);
    }

    [Fact]
    public void Leaf_LabelDerivedFromVersion()
    {
        Assert.Equal("5.60", new RarVersionLeaf(560, "winrar-560").Label);
        Assert.Equal("7.00", new RarVersionLeaf(700, "winrar-700").Label);
        Assert.Equal("6.24", new RarVersionLeaf(624, "winrar-624").Label);
    }

    [Fact]
    public void Group_IsChecked_ReflectsLeafState()
    {
        Assert.True(MakeGroup(5, (500, true), (560, true)).IsChecked);
        Assert.False(MakeGroup(5, (500, false), (560, false)).IsChecked);
        Assert.Null(MakeGroup(5, (500, true), (560, false)).IsChecked);
    }

    [Fact]
    public void Group_CountText_CountsTickedOverTotal()
    {
        Assert.Equal("(1 of 2)", MakeGroup(5, (500, true), (560, false)).CountText);
    }

    [Fact]
    public void Group_LeafToggle_RaisesSelectionChangedAndRecomputes()
    {
        RarVersionGroup g = MakeGroup(5, (500, false), (560, false));
        int raised = 0;
        g.SelectionChanged += (_, _) => raised++;

        g.Leaves[0].IsChecked = true;

        Assert.Equal(1, raised);
        Assert.Null(g.IsChecked);
        Assert.Equal("(1 of 2)", g.CountText);
    }

    [Fact]
    public void Group_ToggleAll_FromUncheckedChecksAll_FromCheckedUnchecksAll()
    {
        RarVersionGroup g = MakeGroup(5, (500, false), (560, false));

        g.ToggleAllCommand.Execute(null);          // unchecked -> all checked
        Assert.True(g.IsChecked);
        Assert.All(g.Leaves, l => Assert.True(l.IsChecked));

        g.ToggleAllCommand.Execute(null);          // checked -> all unchecked
        Assert.False(g.IsChecked);
        Assert.All(g.Leaves, l => Assert.False(l.IsChecked));
    }

    [Fact]
    public void Group_ToggleAll_FromIndeterminateChecksAll()
    {
        RarVersionGroup g = MakeGroup(5, (500, true), (560, false));  // indeterminate

        g.ToggleAllCommand.Execute(null);

        Assert.True(g.IsChecked);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~RarVersionTreeTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement `RarVersionLeaf`**

Create `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/RarVersionLeaf.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>A single installed WinRAR sub-version leaf in the version tree.</summary>
public sealed partial class RarVersionLeaf : ObservableObject
{
    public int Version { get; }
    public string FolderName { get; }
    public string Label { get; }

    public RarVersionLeaf(int version, string folderName)
    {
        Version = version;
        FolderName = folderName;
        Label = $"{version / 100}.{version % 100:D2}";
    }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
```

- [ ] **Step 4: Implement `RarVersionGroup`**

Create `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/RarVersionGroup.cs`:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>
/// A major-version group (e.g. "5.x") over its installed sub-version leaves. The header check is a
/// display-only tri-state; clicking it checks all leaves unless all are already checked, in which
/// case it unchecks all. Raises <see cref="SelectionChanged"/> on any post-construction change.
/// </summary>
public sealed partial class RarVersionGroup : ObservableObject
{
    public int Major { get; }
    public string Header { get; }
    public IReadOnlyList<RarVersionLeaf> Leaves { get; }

    public event EventHandler? SelectionChanged;

    private bool _bulkUpdating;

    public RarVersionGroup(int major, IReadOnlyList<RarVersionLeaf> leaves)
    {
        Major = major;
        Header = $"{major}.x";
        Leaves = leaves;
        foreach (RarVersionLeaf leaf in Leaves)
        {
            leaf.PropertyChanged += OnLeafChanged;
        }
    }

    public bool? IsChecked
    {
        get
        {
            int ticked = Leaves.Count(l => l.IsChecked);
            if (ticked == 0)
            {
                return false;
            }

            return ticked == Leaves.Count ? true : null;
        }
    }

    public string CountText => $"({Leaves.Count(l => l.IsChecked)} of {Leaves.Count})";

    [RelayCommand]
    private void ToggleAll()
    {
        bool target = IsChecked != true;  // all-checked -> uncheck; unchecked/indeterminate -> check
        _bulkUpdating = true;
        foreach (RarVersionLeaf leaf in Leaves)
        {
            leaf.IsChecked = target;
        }

        _bulkUpdating = false;
        RaiseStateChanged();
    }

    /// <summary>Unsubscribes leaf handlers before the group is discarded on rebuild.</summary>
    public void Detach()
    {
        foreach (RarVersionLeaf leaf in Leaves)
        {
            leaf.PropertyChanged -= OnLeafChanged;
        }
    }

    private void OnLeafChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RarVersionLeaf.IsChecked) || _bulkUpdating)
        {
            return;
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(CountText));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~RarVersionTreeTests"`
Expected: PASS (6 tests).

- [ ] **Step 6: Commit**

```bash
cd /e/Projects/ReScene.NET
git add ReScene.NET/ViewModels/Reconstruction/RarVersionLeaf.cs ReScene.NET/ViewModels/Reconstruction/RarVersionGroup.cs ReScene.NET.Tests/RarVersionTreeTests.cs
git commit -m "feat: add RarVersionLeaf/RarVersionGroup tri-state tree nodes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: App — `RarSwitchSettings` fields + `BuildVersionRanges` keyed off scan state

**Files:**
- Modify: `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/RarSwitchSettings.cs` (add two members near the `// RAR versions` block, lines ~11-17)
- Modify: `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/RarCommandLineBuilder.cs` (`BuildVersionRanges` lines 16-50)
- Test: `E:/Projects/ReScene.NET/ReScene.NET.Tests/RarCommandLineBuilderTests.cs` (extend)

**Interfaces:**
- Consumes: nothing new.
- Produces: `RarSwitchSettings.SelectedRarVersions : IReadOnlyList<int>` (default `[]`) and `RarSwitchSettings.HasScannedVersions : bool`. `BuildVersionRanges` returns tight `[v, v+1)` ranges (dedup, ascending) when `HasScannedVersions`, else the broad major ranges.

- [ ] **Step 1: Write the failing tests**

Append to `E:/Projects/ReScene.NET/ReScene.NET.Tests/RarCommandLineBuilderTests.cs` (inside the class, after the existing `BuildVersionRanges_*` tests):

```csharp
    [Fact]
    public void BuildVersionRanges_Scanned_TightRangePerSelectedVersion()
    {
        var settings = new RarSwitchSettings
        {
            HasScannedVersions = true,
            SelectedRarVersions = [560, 624],
        };

        List<VersionRange> ranges = RarCommandLineBuilder.BuildVersionRanges(settings);

        Assert.Equal(2, ranges.Count);
        Assert.Equal((560, 561), (ranges[0].Start, ranges[0].End));
        Assert.Equal((624, 625), (ranges[1].Start, ranges[1].End));
    }

    [Fact]
    public void BuildVersionRanges_Scanned_DedupsAndSorts()
    {
        var settings = new RarSwitchSettings
        {
            HasScannedVersions = true,
            SelectedRarVersions = [560, 560, 500],
        };

        List<VersionRange> ranges = RarCommandLineBuilder.BuildVersionRanges(settings);

        Assert.Equal(new[] { 500, 560 }, ranges.Select(r => r.Start).ToArray());
    }

    [Fact]
    public void BuildVersionRanges_Scanned_EmptySelection_ReturnsEmpty()
    {
        var settings = new RarSwitchSettings { HasScannedVersions = true, Version5 = true };

        List<VersionRange> ranges = RarCommandLineBuilder.BuildVersionRanges(settings);

        Assert.Empty(ranges);  // scanned + nothing ticked -> no versions (Start guard blocks the run)
    }

    [Fact]
    public void BuildVersionRanges_NotScanned_FallsBackToBroadMajorRanges()
    {
        var settings = new RarSwitchSettings { HasScannedVersions = false, Version5 = true, Version6 = true };

        List<VersionRange> ranges = RarCommandLineBuilder.BuildVersionRanges(settings);

        Assert.Equal(2, ranges.Count);
        Assert.Equal((500, 600), (ranges[0].Start, ranges[0].End));
        Assert.Equal((600, 700), (ranges[1].Start, ranges[1].End));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~RarCommandLineBuilderTests"`
Expected: FAIL — `HasScannedVersions`/`SelectedRarVersions` do not exist.

- [ ] **Step 3: Add the two `RarSwitchSettings` members**

In `RarSwitchSettings.cs`, after the `public bool Version7 { get; init; }` line (line 17), add:

```csharp

    // Per-sub-version selection (materialised from a folder scan). When HasScannedVersions is
    // true, SelectedRarVersions is authoritative and the major bools above are ignored.
    public IReadOnlyList<int> SelectedRarVersions { get; init; } = [];
    public bool HasScannedVersions { get; init; }
```

- [ ] **Step 4: Rewrite `BuildVersionRanges`**

In `RarCommandLineBuilder.cs`, replace the whole `BuildVersionRanges` method (lines 16-50) with:

```csharp
    public static List<VersionRange> BuildVersionRanges(RarSwitchSettings s)
    {
        // A completed folder scan makes the per-version selection authoritative: one tight range
        // per chosen version. Before any scan (beginner wizard / pre-folder editing) fall back to
        // the broad major-version ranges so behaviour matches the pre-tree UI.
        if (s.HasScannedVersions)
        {
            return s.SelectedRarVersions
                .Distinct()
                .OrderBy(v => v)
                .Select(v => new VersionRange(v, v + 1))
                .ToList();
        }

        List<VersionRange> rarVersions = [];
        if (s.Version2)
        {
            rarVersions.Add(new(200, 300));
        }

        if (s.Version3)
        {
            rarVersions.Add(new(300, 400));
        }

        if (s.Version4)
        {
            rarVersions.Add(new(400, 500));
        }

        if (s.Version5)
        {
            rarVersions.Add(new(500, 600));
        }

        if (s.Version6)
        {
            rarVersions.Add(new(600, 700));
        }

        if (s.Version7)
        {
            rarVersions.Add(new(700, 800));
        }

        return rarVersions;
    }
```

Confirm `RarCommandLineBuilder.cs` has `using System.Linq;` (needed for `Distinct`/`OrderBy`/`Select`); add it if absent.

- [ ] **Step 5: Run tests to verify they pass**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~RarCommandLineBuilderTests"`
Expected: PASS — the four new tests plus the existing `BuildVersionRanges_NothingSelected_ReturnsEmpty` and `BuildVersionRanges_AllVersions_*` (both use `HasScannedVersions == false` by default, so they stay green).

- [ ] **Step 6: Commit**

```bash
cd /e/Projects/ReScene.NET
git add ReScene.NET/ViewModels/Reconstruction/RarSwitchSettings.cs ReScene.NET/ViewModels/Reconstruction/RarCommandLineBuilder.cs ReScene.NET.Tests/RarCommandLineBuilderTests.cs
git commit -m "feat: BuildVersionRanges emits tight per-version ranges when scanned

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: App — `VersionSelectionReconciler` pure helper

**Files:**
- Create: `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/VersionSelectionReconciler.cs`
- Test: `E:/Projects/ReScene.NET/ReScene.NET.Tests/VersionSelectionReconcilerTests.cs` (create)

**Interfaces:**
- Consumes: `InstalledRarVersion` (Task 2).
- Produces: `internal static HashSet<int> VersionSelectionReconciler.ComputeTicked(IReadOnlyList<InstalledRarVersion> installed, IReadOnlyList<int>? pendingExplicit, IReadOnlySet<int> enabledMajors)` — if `pendingExplicit` is non-null, tick installed versions in that list; else tick installed versions whose major (`Version / 100`) is enabled.

- [ ] **Step 1: Write the failing test**

Create `E:/Projects/ReScene.NET/ReScene.NET.Tests/VersionSelectionReconcilerTests.cs`:

```csharp
using ReScene.NET.ViewModels.Reconstruction;

namespace ReScene.NET.Tests;

public sealed class VersionSelectionReconcilerTests
{
    private static readonly IReadOnlyList<InstalledRarVersion> Installed =
    [
        new(500, "winrar-500", "p500"),
        new(560, "winrar-560", "p560"),
        new(624, "winrar-624", "p624"),
    ];

    [Fact]
    public void ExplicitSelection_TicksListedInstalled_DropsMissing()
    {
        HashSet<int> ticked = VersionSelectionReconciler.ComputeTicked(
            Installed, pendingExplicit: [560, 999], enabledMajors: new HashSet<int>());

        Assert.Equal(new[] { 560 }, ticked.OrderBy(v => v).ToArray());
    }

    [Fact]
    public void NoExplicit_TicksAllInstalledInEnabledMajors()
    {
        HashSet<int> ticked = VersionSelectionReconciler.ComputeTicked(
            Installed, pendingExplicit: null, enabledMajors: new HashSet<int> { 5 });

        Assert.Equal(new[] { 500, 560 }, ticked.OrderBy(v => v).ToArray());
    }

    [Fact]
    public void NoExplicit_NoEnabledMajors_TicksNothing()
    {
        HashSet<int> ticked = VersionSelectionReconciler.ComputeTicked(
            Installed, pendingExplicit: null, enabledMajors: new HashSet<int>());

        Assert.Empty(ticked);
    }

    [Fact]
    public void EmptyExplicit_TicksNothing()
    {
        HashSet<int> ticked = VersionSelectionReconciler.ComputeTicked(
            Installed, pendingExplicit: [], enabledMajors: new HashSet<int> { 5, 6 });

        Assert.Empty(ticked);  // an explicit (non-null) empty list wins over majors
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~VersionSelectionReconcilerTests"`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the reconciler**

Create `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/VersionSelectionReconciler.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>
/// Pure decision of which installed versions to tick, from folder contents and current intent.
/// An explicit (config) selection wins when present; otherwise the enabled major versions decide.
/// </summary>
internal static class VersionSelectionReconciler
{
    public static HashSet<int> ComputeTicked(
        IReadOnlyList<InstalledRarVersion> installed,
        IReadOnlyList<int>? pendingExplicit,
        IReadOnlySet<int> enabledMajors)
    {
        if (pendingExplicit is not null)
        {
            HashSet<int> wanted = [.. pendingExplicit];
            return [.. installed.Where(v => wanted.Contains(v.Version)).Select(v => v.Version)];
        }

        return [.. installed.Where(v => enabledMajors.Contains(v.Version / 100)).Select(v => v.Version)];
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~VersionSelectionReconcilerTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
cd /e/Projects/ReScene.NET
git add ReScene.NET/ViewModels/Reconstruction/VersionSelectionReconciler.cs ReScene.NET.Tests/VersionSelectionReconcilerTests.cs
git commit -m "feat: add VersionSelectionReconciler (pure tick decision)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: App — `ReconstructorViewModel` integration

**Files:**
- Modify: `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/ReconstructorViewModel.cs` (add usings; new members near the version props ~390; `OnWinRarPathChanged` ~184; after `SetRARVersionsFromSRR` call ~837; `BuildSwitchSettings` ~1778; empty-selection guard in `StartAsync` after ~987)
- Test: `E:/Projects/ReScene.NET/ReScene.NET.Tests/ReconstructorViewModelVersionsTests.cs` (create)

**Interfaces:**
- Consumes: `WinRarVersionScanner`/`InstalledRarVersion` (Task 2), `RarVersionGroup`/`RarVersionLeaf` (Task 3), `RarSwitchSettings` fields (Task 4), `VersionSelectionReconciler` (Task 5).
- Produces (used by Tasks 7 & 8):
  - `public ObservableCollection<RarVersionGroup> VersionGroups { get; }`
  - `[ObservableProperty] public partial bool HasScannedVersions { get; set; }`
  - `[ObservableProperty] public partial bool ShowNoVersionsHint { get; set; }`
  - `RescanVersionsCommand`, `SelectAllVersionsCommand`, `SelectNoVersionsCommand`
  - `internal IReadOnlyList<int> SelectedLeafVersions { get; }`
  - `internal void ApplyScanResult(IReadOnlyList<InstalledRarVersion> installed, bool folderScanned)`
  - `internal void LoadPendingVersionSelection(IReadOnlyList<int>? explicitVersions)`

- [ ] **Step 1: Write the failing test**

Create `E:/Projects/ReScene.NET/ReScene.NET.Tests/ReconstructorViewModelVersionsTests.cs`:

```csharp
using ReScene.Core;
using ReScene.NET.Services;
using ReScene.NET.ViewModels;
using ReScene.NET.ViewModels.Reconstruction;

namespace ReScene.NET.Tests;

public sealed class ReconstructorViewModelVersionsTests
{
    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, System.Windows.Threading.DispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    private sealed class InertBruteForceService : IBruteForceService
    {
        public event EventHandler<BruteForceProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<BruteForceStatusChangedEventArgs>? StatusChanged { add { } remove { } }
        public event EventHandler<LogEventArgs>? LogMessage { add { } remove { } }
        public event EventHandler<FileCopyProgressEventArgs>? FileCopyProgress { add { } remove { } }
        public event EventHandler<CRCValidationProgressEventArgs>? CRCValidationProgress { add { } remove { } }
        public event EventHandler<TimestampPreservationFailedEventArgs>? TimestampPreservationFailed { add { } remove { } }
        public Task<BruteForceRunResult> RunAsync(BruteForceOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new BruteForceRunResult(true, null));
    }

    private static ReconstructorViewModel CreateVm()
        => new(new InertBruteForceService(), new NoOpFileDialogService(),
               settingsService: null, uiDispatcher: new InlineUiDispatcher());

    private static readonly IReadOnlyList<InstalledRarVersion> Installed =
    [
        new(500, "winrar-500", "p500"),
        new(560, "winrar-560", "p560"),
        new(602, "winrar-602", "p602"),
        new(624, "winrar-624", "p624"),
    ];

    private static int[] Ticked(ReconstructorViewModel vm) =>
        vm.VersionGroups.SelectMany(g => g.Leaves).Where(l => l.IsChecked).Select(l => l.Version).OrderBy(v => v).ToArray();

    [Fact]
    public void ApplyScanResult_ImportIntent_TicksAllInstalledInEnabledMajors()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.Version2 = vm.Version3 = vm.Version4 = vm.Version7 = false;
        vm.Version5 = true; vm.Version6 = true;

        vm.ApplyScanResult(Installed, folderScanned: true);

        Assert.True(vm.HasScannedVersions);
        Assert.Equal(new[] { 500, 560, 602, 624 }, Ticked(vm));
        Assert.Equal(2, vm.VersionGroups.Count);   // 5.x and 6.x
    }

    [Fact]
    public void FolderScannedThenImport_ReTicksToNewMajors()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.Version5 = true; vm.Version6 = true;
        vm.ApplyScanResult(Installed, folderScanned: true);

        // Simulate an SRR import that maps only to 6.x
        vm.Version5 = false; vm.Version6 = true;
        vm.LoadPendingVersionSelection(null);   // import path: no explicit list, reconcile from majors

        Assert.Equal(new[] { 602, 624 }, Ticked(vm));
    }

    [Fact]
    public void ExplicitSelection_TicksSubset_DropsMissing_ThenClears()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.LoadPendingVersionSelection([560, 624, 999]);   // config load sets pending
        vm.ApplyScanResult(Installed, folderScanned: true);

        Assert.Equal(new[] { 560, 624 }, Ticked(vm));

        // A subsequent scan with no new intent must NOT re-apply the (now consumed) pending list;
        // it falls back to majors. With no majors enabled, nothing is ticked.
        vm.Version2 = vm.Version3 = vm.Version4 = vm.Version5 = vm.Version6 = vm.Version7 = false;
        vm.ApplyScanResult(Installed, folderScanned: true);
        Assert.Empty(Ticked(vm));
    }

    [Fact]
    public void ManualLeafToggle_SyncsMajorBooleans()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.Version5 = true; vm.Version6 = true;
        vm.ApplyScanResult(Installed, folderScanned: true);

        foreach (RarVersionLeaf leaf in vm.VersionGroups.First(g => g.Major == 6).Leaves)
        {
            leaf.IsChecked = false;   // untick all of 6.x
        }

        Assert.True(vm.Version5);
        Assert.False(vm.Version6);   // synced from tree
    }

    [Fact]
    public void SelectedLeafVersions_ReflectsTicksAscending()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.LoadPendingVersionSelection([624, 500]);
        vm.ApplyScanResult(Installed, folderScanned: true);

        Assert.Equal(new[] { 500, 624 }, vm.SelectedLeafVersions.ToArray());
    }

    [Fact]
    public void ApplyScanResult_EmptyFolder_ShowsHint_NoGroups()
    {
        ReconstructorViewModel vm = CreateVm();

        vm.ApplyScanResult([], folderScanned: false);

        Assert.Empty(vm.VersionGroups);
        Assert.True(vm.ShowNoVersionsHint);
        Assert.False(vm.HasScannedVersions);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~ReconstructorViewModelVersionsTests"`
Expected: FAIL — `VersionGroups`/`ApplyScanResult`/etc. do not exist.

- [ ] **Step 3: Add usings and the version-tree state block**

Ensure the top of `ReconstructorViewModel.cs` has (add any missing):

```csharp
using System.Collections.ObjectModel;
using ReScene.NET.ViewModels.Reconstruction;
```

Immediately after the `Version7` observable property (line 395), add the new state, tree, commands, and reconcile logic:

```csharp

    // ── Per-sub-version selection (tree over the installed WinRAR versions) ──

    /// <summary>Installed-version tree grouped by major; the checked leaves drive the brute-force.</summary>
    public ObservableCollection<RarVersionGroup> VersionGroups { get; } = [];

    /// <summary>True once a folder scan has completed for an existing folder (even if it had no versions).</summary>
    [ObservableProperty]
    public partial bool HasScannedVersions { get; set; }

    /// <summary>True when the tree is empty, so the view can show the "no versions found" hint.</summary>
    [ObservableProperty]
    public partial bool ShowNoVersionsHint { get; set; }

    /// <summary>Last folder scan result, reused by import/config reconcile without re-hitting disk.</summary>
    private IReadOnlyList<InstalledRarVersion> _lastScan = [];

    /// <summary>Explicit version list from a config load, consumed by the next scanned reconcile.</summary>
    private List<int>? _pendingVersionSelection;

    /// <summary>Latest-wins guard for overlapping async scans.</summary>
    private int _scanToken;

    /// <summary>Suppresses tree→major sync while the VM is programmatically rebuilding the tree.</summary>
    private bool _suppressGroupSync;

    /// <summary>The currently-ticked leaf versions, ascending. Snapshotted at Start and by config Capture.</summary>
    internal IReadOnlyList<int> SelectedLeafVersions =>
        VersionGroups.SelectMany(g => g.Leaves).Where(l => l.IsChecked).Select(l => l.Version).OrderBy(v => v).ToList();

    [RelayCommand]
    private void RescanVersions() => TriggerVersionScan();

    [RelayCommand]
    private void SelectAllVersions() => SetAllLeaves(true);

    [RelayCommand]
    private void SelectNoVersions() => SetAllLeaves(false);

    private void SetAllLeaves(bool value)
    {
        _suppressGroupSync = true;
        foreach (RarVersionGroup group in VersionGroups)
        {
            foreach (RarVersionLeaf leaf in group.Leaves)
            {
                leaf.IsChecked = value;
            }
        }

        _suppressGroupSync = false;
        SyncMajorsFromTree();
    }

    /// <summary>Kicks off a folder scan: synchronous empty result for an invalid folder (keeps tests
    /// deterministic), otherwise off-thread with a latest-wins token.</summary>
    private void TriggerVersionScan()
    {
        string folder = WinRarPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            ApplyScanResult([], folderScanned: false);
            return;
        }

        _ = RunVersionScanAsync(folder);
    }

    private async Task RunVersionScanAsync(string folder)
    {
        int token = ++_scanToken;
        IReadOnlyList<InstalledRarVersion> installed = await Task.Run(() => WinRarVersionScanner.Scan(folder));
        if (token != _scanToken)
        {
            return;  // superseded by a newer scan
        }

        ApplyScanResult(installed, folderScanned: true);
    }

    /// <summary>Stores a scan result and reconciles the tree. Also the test seam for the async scan.</summary>
    internal void ApplyScanResult(IReadOnlyList<InstalledRarVersion> installed, bool folderScanned)
    {
        _lastScan = installed;
        HasScannedVersions = folderScanned;
        ApplyReconcile();
    }

    /// <summary>Sets the pending explicit selection (config load) and reconciles against the last scan.</summary>
    internal void LoadPendingVersionSelection(IReadOnlyList<int>? explicitVersions)
    {
        _pendingVersionSelection = explicitVersions?.ToList();
        ApplyReconcile();
    }

    private void ApplyReconcile()
    {
        HashSet<int> enabledMajors = EnabledMajors();
        HashSet<int> ticked = VersionSelectionReconciler.ComputeTicked(_lastScan, _pendingVersionSelection, enabledMajors);

        // The pending explicit selection is consumed only once a real scan has materialised the tree.
        if (_pendingVersionSelection is not null && HasScannedVersions)
        {
            _pendingVersionSelection = null;
        }

        RebuildVersionGroups(_lastScan, ticked);
        SyncMajorsFromTree();
        ShowNoVersionsHint = VersionGroups.Count == 0;
    }

    private void RebuildVersionGroups(IReadOnlyList<InstalledRarVersion> installed, HashSet<int> ticked)
    {
        _suppressGroupSync = true;
        foreach (RarVersionGroup group in VersionGroups)
        {
            group.SelectionChanged -= OnGroupSelectionChanged;
            group.Detach();
        }

        VersionGroups.Clear();
        foreach (IGrouping<int, InstalledRarVersion> majorGroup in installed.GroupBy(v => v.Version / 100).OrderBy(g => g.Key))
        {
            List<RarVersionLeaf> leaves = majorGroup
                .OrderBy(v => v.Version)
                .Select(v => new RarVersionLeaf(v.Version, v.FolderName) { IsChecked = ticked.Contains(v.Version) })
                .ToList();
            RarVersionGroup group = new(majorGroup.Key, leaves);
            group.SelectionChanged += OnGroupSelectionChanged;
            VersionGroups.Add(group);
        }

        _suppressGroupSync = false;
    }

    private void OnGroupSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressGroupSync)
        {
            return;
        }

        SyncMajorsFromTree();
    }

    /// <summary>Mirrors "any leaf in this major ticked" onto the coarse major bools — but only when a
    /// tree exists; with no scan the bools remain the fallback/coarse intent.</summary>
    private void SyncMajorsFromTree()
    {
        if (!HasScannedVersions)
        {
            return;
        }

        Version2 = MajorHasTick(2);
        Version3 = MajorHasTick(3);
        Version4 = MajorHasTick(4);
        Version5 = MajorHasTick(5);
        Version6 = MajorHasTick(6);
        Version7 = MajorHasTick(7);
    }

    private bool MajorHasTick(int major) =>
        VersionGroups.FirstOrDefault(g => g.Major == major)?.Leaves.Any(l => l.IsChecked) ?? false;

    private HashSet<int> EnabledMajors()
    {
        HashSet<int> majors = [];
        if (Version2) { majors.Add(2); }
        if (Version3) { majors.Add(3); }
        if (Version4) { majors.Add(4); }
        if (Version5) { majors.Add(5); }
        if (Version6) { majors.Add(6); }
        if (Version7) { majors.Add(7); }
        return majors;
    }
```

- [ ] **Step 4: Trigger a scan on folder change**

Modify `OnWinRarPathChanged` (line 184-185) from:

```csharp
    partial void OnWinRarPathChanged(string value) =>
        WinRarStatus = ReconstructorFieldGuidance.EvaluateWinRarPath(value);
```

to:

```csharp
    partial void OnWinRarPathChanged(string value)
    {
        WinRarStatus = ReconstructorFieldGuidance.EvaluateWinRarPath(value);
        TriggerVersionScan();
    }
```

- [ ] **Step 5: Reconcile after SRR import**

In the SRR import path, after `SetRARVersionsFromSRR(srr);` (line 837), add — import is a fresh coarse intent, so clear any pending config selection, then reconcile against the last scan:

```csharp
            // RAR version selection
            SetRARVersionsFromSRR(srr);
            _pendingVersionSelection = null;
            ApplyReconcile();
```

- [ ] **Step 6: Feed the new fields into `BuildSwitchSettings`**

In `BuildSwitchSettings()` (starting line 1778), add these two lines to the object initializer, right after the `Version7 = Version7,` line:

```csharp
        SelectedRarVersions = SelectedLeafVersions,
        HasScannedVersions = HasScannedVersions,
```

- [ ] **Step 7: Add the empty-selection guard in `StartAsync`**

In `StartAsync`, immediately after the WinRAR directory existence check (after line 987, the `if (!Directory.Exists(WinRarPath))` block), add:

```csharp
        // A materialised tree with nothing ticked would brute-force zero versions — block it with a
        // clear message. The no-scan case (empty tree) is unaffected and uses the broad fallback.
        if (VersionGroups.Count > 0 && VersionGroups.SelectMany(g => g.Leaves).All(l => !l.IsChecked))
        {
            Log(LogTarget.System, "No WinRAR versions selected.");
            _fileDialog.ShowError("Validation Error", "Select at least one WinRAR version.");
            return;
        }
```

- [ ] **Step 8: Run tests to verify they pass**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~ReconstructorViewModelVersionsTests"`
Expected: PASS (6 tests). Then run the existing mapper tests to confirm no regression (they set `WinRarPath` to a non-existent path, so `TriggerVersionScan` applies an empty result synchronously and `SyncMajorsFromTree` no-ops):
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~ReconstructorConfigMapperTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
cd /e/Projects/ReScene.NET
git add ReScene.NET/ViewModels/ReconstructorViewModel.cs ReScene.NET.Tests/ReconstructorViewModelVersionsTests.cs
git commit -m "feat: version tree, scan, reconcile, and empty-selection guard in Reconstructor VM

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: App — Config round-trip for `SelectedRarVersions`

**Files:**
- Modify: `E:/Projects/ReScene.NET/ReScene.NET/Models/ReconstructorConfig.cs` (add field near the RAR-versions block ~22)
- Modify: `E:/Projects/ReScene.NET/ReScene.NET/ViewModels/Reconstruction/ReconstructorConfigMapper.cs` (`Capture` ~26; `Apply` end ~178)
- Test: `E:/Projects/ReScene.NET/ReScene.NET.Tests/ReconstructorConfigMapperTests.cs` (extend)

**Interfaces:**
- Consumes: `vm.SelectedLeafVersions`, `vm.LoadPendingVersionSelection`, `vm.ApplyScanResult` (Task 6).
- Produces: `ReconstructorConfig.SelectedRarVersions : List<int>?` round-trips the ticked leaves; a config lacking the field falls back to enabled-major ticking.

- [ ] **Step 1: Write the failing tests**

Append to `E:/Projects/ReScene.NET/ReScene.NET.Tests/ReconstructorConfigMapperTests.cs` (inside the class; uses the file's existing `CreateVm` helper and the `InstalledRarVersion` type):

```csharp
    private static readonly IReadOnlyList<InstalledRarVersion> InstalledVersions =
    [
        new(500, "winrar-500", "p500"),
        new(560, "winrar-560", "p560"),
        new(624, "winrar-624", "p624"),
    ];

    [Fact]
    public void Capture_WritesTickedLeafVersions()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.LoadPendingVersionSelection([560, 624]);
        vm.ApplyScanResult(InstalledVersions, folderScanned: true);

        ReconstructorConfig config = ReconstructorConfigMapper.Capture(vm);

        Assert.Equal(new[] { 560, 624 }, config.SelectedRarVersions!.OrderBy(v => v).ToArray());
    }

    [Fact]
    public void Apply_WithSelectedVersions_TicksThoseAfterScan()
    {
        ReconstructorViewModel vm = CreateVm();
        var config = new ReconstructorConfig { SelectedRarVersions = [500, 624] };

        ReconstructorConfigMapper.Apply(vm, config);          // sets pending
        vm.ApplyScanResult(InstalledVersions, folderScanned: true);

        int[] ticked = vm.VersionGroups.SelectMany(g => g.Leaves)
            .Where(l => l.IsChecked).Select(l => l.Version).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { 500, 624 }, ticked);
    }

    [Fact]
    public void Apply_OldConfigWithoutSelectedVersions_FallsBackToEnabledMajors()
    {
        ReconstructorViewModel vm = CreateVm();
        var config = new ReconstructorConfig  // SelectedRarVersions == null (old config)
        {
            Version2 = false, Version3 = false, Version4 = false,
            Version5 = true, Version6 = true, Version7 = false,
        };

        ReconstructorConfigMapper.Apply(vm, config);          // pending stays null
        vm.ApplyScanResult(InstalledVersions, folderScanned: true);

        int[] ticked = vm.VersionGroups.SelectMany(g => g.Leaves)
            .Where(l => l.IsChecked).Select(l => l.Version).OrderBy(v => v).ToArray();
        Assert.Equal(new[] { 500, 560, 624 }, ticked);        // all installed in enabled majors
    }
```

(If the test file lacks `using ReScene.NET.ViewModels.Reconstruction;`, add it — it is needed for `InstalledRarVersion`.)

- [ ] **Step 2: Run tests to verify they fail**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~ReconstructorConfigMapperTests"`
Expected: FAIL — `ReconstructorConfig.SelectedRarVersions` does not exist.

- [ ] **Step 3: Add the config field**

In `ReconstructorConfig.cs`, after the `public bool Version7 { get; set; }` line (line 22), add:

```csharp

    /// <summary>Explicit per-sub-version selection. Null in configs written before this feature —
    /// such configs fall back to ticking all installed versions in the enabled majors.</summary>
    public List<int>? SelectedRarVersions { get; set; }
```

- [ ] **Step 4: Capture the selection**

In `ReconstructorConfigMapper.Capture` (the object initializer), after the `Version7 = vm.Version7,` line (line 26), add:

```csharp

        // Only persist an explicit list when a real folder scan produced the tree; otherwise write
        // null so re-import falls back to the enabled-major ticking (an empty [] would wrongly
        // suppress all versions, because an explicit empty selection wins over the majors).
        SelectedRarVersions = vm.HasScannedVersions ? vm.SelectedLeafVersions.ToList() : null,
```

- [ ] **Step 5: Apply the selection**

In `ReconstructorConfigMapper.Apply`, add as the **last** statement of the method (after `vm.EnableHostOSPatching = c.EnableHostOSPatching;`, line 178):

```csharp

        // Set the pending explicit selection last; the next folder scan (triggered by WinRarPath
        // above, or the tab's initial scan) consumes it. A null list keeps the enabled-major fallback.
        vm.LoadPendingVersionSelection(c.SelectedRarVersions);
```

- [ ] **Step 6: Run tests to verify they pass**

Run from `E:/Projects/ReScene.NET`:
`dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/ --filter "FullyQualifiedName~ReconstructorConfigMapperTests"`
Expected: PASS — the three new tests plus all existing mapper tests (the existing `StampDistinctiveValues` round-trip is unaffected: with no real folder scanned, `SyncMajorsFromTree` no-ops and the applied `Version2..7` survive).

- [ ] **Step 7: Commit**

```bash
cd /e/Projects/ReScene.NET
git add ReScene.NET/Models/ReconstructorConfig.cs ReScene.NET/ViewModels/Reconstruction/ReconstructorConfigMapper.cs ReScene.NET.Tests/ReconstructorConfigMapperTests.cs
git commit -m "feat: round-trip SelectedRarVersions in reconstructor config

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: App — `ReconstructorView.xaml` grouped tri-state tree UI

**Files:**
- Modify: `E:/Projects/ReScene.NET/ReScene.NET/Views/ReconstructorView.xaml` (the "Versions" `TabItem`, lines 154-168)

**Interfaces:**
- Consumes: `VersionGroups`, `RescanVersionsCommand`, `SelectAllVersionsCommand`, `SelectNoVersionsCommand`, `ShowNoVersionsHint` (Task 6); `RarVersionGroup.Header/CountText/IsChecked/ToggleAllCommand/Leaves`, `RarVersionLeaf.Label/FolderName/IsChecked` (Task 3).

- [ ] **Step 1: Confirm the BoolToVisibility converter key**

The Compare busy overlay already uses a bool→Visibility converter. Find its resource key:
Run: `grep -n "BoolToVisibility" E:/Projects/ReScene.NET/ReScene.NET/Views/FileCompareView.xaml E:/Projects/ReScene.NET/ReScene.NET/App.xaml`
Use the same `StaticResource` key in Step 2 (referred to below as `BoolToVis`). If the key differs, substitute it.

- [ ] **Step 2: Replace the flat checkbox list with the tree**

In `ReconstructorView.xaml`, replace the inner `StackPanel` of the "Versions" `TabItem` (lines 157-166, the `<StackPanel>` containing the caption `TextBlock` and the six `Version2..7` `CheckBox`es) with:

```xml
                    <StackPanel>
                        <TextBlock Text="The WinRAR versions to try. Only versions found in your WinRAR versions folder are listed — tick the ones you think produced the release."
                                   TextWrapping="Wrap" Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}" Margin="0,0,0,4" />
                        <DockPanel Margin="0,0,0,4">
                            <Button DockPanel.Dock="Right" Content="Rescan" Command="{Binding RescanVersionsCommand}"
                                    Style="{StaticResource GhostButton}" Margin="4,0,0,0" MinWidth="70" />
                            <Button DockPanel.Dock="Right" Content="None" Command="{Binding SelectNoVersionsCommand}"
                                    Style="{StaticResource GhostButton}" Margin="4,0,0,0" MinWidth="55" />
                            <Button DockPanel.Dock="Right" Content="All" Command="{Binding SelectAllVersionsCommand}"
                                    Style="{StaticResource GhostButton}" Margin="4,0,0,0" MinWidth="55" />
                            <TextBlock Text="" />
                        </DockPanel>

                        <TextBlock Text="No WinRAR versions found. Set the WinRAR versions folder on the Files tab, then Rescan."
                                   TextWrapping="Wrap" Foreground="{DynamicResource ForegroundSecondary}"
                                   FontSize="{DynamicResource FontSizeCaption}" Margin="0,4"
                                   Visibility="{Binding ShowNoVersionsHint, Converter={StaticResource BoolToVis}}" />

                        <ItemsControl ItemsSource="{Binding VersionGroups}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <StackPanel Margin="0,2,0,0">
                                        <CheckBox IsThreeState="True" IsChecked="{Binding IsChecked, Mode=OneWay}"
                                                  Command="{Binding ToggleAllCommand}">
                                            <TextBlock>
                                                <Run Text="RAR " /><Run Text="{Binding Header}" /><Run Text="  " /><Run Text="{Binding CountText}" />
                                            </TextBlock>
                                        </CheckBox>
                                        <ItemsControl ItemsSource="{Binding Leaves}" Margin="18,0,0,0">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <CheckBox IsChecked="{Binding IsChecked}" Content="{Binding Label}"
                                                              ToolTip="{Binding FolderName}" Margin="0,1" />
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
```

- [ ] **Step 3: Build and manually verify**

Build (release-quality, no warnings) from `E:/Projects/ReScene.NET`:
`dotnet build ReScene.NET/ReScene.NET.csproj -p:BaseOutputPath=bin2/ --no-incremental`
Expected: 0 warnings, 0 errors.

Manual (the running app is not killed; the reviewer/user launches a fresh build separately if desired):
1. Set the WinRAR versions folder → the tree lists installed sub-versions grouped by major with "(n of m)" counts.
2. Import a solid multi-version release SRR → matched majors' leaves are all ticked.
3. Untick some leaves → the group header goes indeterminate; Start's brute-force log/args show only the chosen versions.
4. Untick everything with at least one version present → Start shows "Select at least one WinRAR version."
5. Drop a new WinRAR version folder in while the app is open → Rescan surfaces it.
6. Point the folder at an empty directory → the "No WinRAR versions found" hint shows.

- [ ] **Step 4: Commit**

```bash
cd /e/Projects/ReScene.NET
git add ReScene.NET/Views/ReconstructorView.xaml
git commit -m "feat: grouped tri-state WinRAR version tree in Reconstructor view

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Final Verification (after all tasks)

- [ ] Full lib suite: from `E:/Projects/ReScene.Lib`, `dotnet test ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/` → all green.
- [ ] Full app suite: from `E:/Projects/ReScene.NET`, `dotnet test ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/` → all green.
- [ ] No-warning builds of both projects with `--no-incremental` → 0 warnings / 0 errors.
- [ ] Delete `bin2` dirs under both repos.
- [ ] Confirm the app submodule pointer bump (Task 1, Step 7) is committed so the app references the new `Manager.TryParseRARVersion`.
