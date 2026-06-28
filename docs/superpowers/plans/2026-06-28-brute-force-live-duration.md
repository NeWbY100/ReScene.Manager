# Brute-Force Progress Live Duration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Brute Force Progress window's Duration column count up live for the in-progress row, freezing each row's value when it finishes.

**Architecture:** `VersionEntry.DurationText` measures against `DateTime.Now` while testing (`EndedAt` null) and against `EndedAt` once finished; the view-model's existing 1-second elapsed timer refreshes the active row (`VersionEntries[^1]`) each tick. No new timer, no tracker change.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-28-brute-force-live-duration-design.md`

## Global Constraints

- **Target:** `net10.0-windows` (WPF). Do NOT touch the `ReScene.Lib` submodule.
- **The running app locks `ReScene.NET/bin/`.** ALWAYS build/test with `-p:BaseOutputPath=bin2/`. NEVER kill the app.
- **Verify non-incrementally:** `dotnet build ... --no-incremental` → **0 warnings, 0 errors**.
- After verification, delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- **Work on the branch chosen at execution start** — do not switch/rebase/amend. One commit.
- Only **Duration** goes live; **Start**/**End** unchanged. 1-second granularity.
- `InternalsVisibleTo ReScene.NET.Tests` is set; `VersionEntry` is `ReconstructorViewModel.VersionEntry` (public nested).
- **End the commit message** with: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## Task 1: Live Duration on the active row

**Files:**
- Modify: `ReScene.NET/ViewModels/ReconstructorViewModel.cs` (`VersionEntry.DurationText` + `RefreshLiveDuration`; `OnElapsedTimerTick`)
- Test: `ReScene.NET.Tests/VersionEntryTests.cs`

**Interfaces:**
- Produces: `VersionEntry.DurationText` now live while testing; `VersionEntry.RefreshLiveDuration()` raises `PropertyChanged(DurationText)`.

- [ ] **Step 1: Update the tests (RED)**

In `ReScene.NET.Tests/VersionEntryTests.cs`, replace `NewRow_HasStartText_AndBlankEndAndDuration` and `WhileTesting_EndAndDuration_AreBlank` and add the refresh test, so the file's tests become:

```csharp
    [Fact]
    public void NewRow_HasStartText_LiveDuration_BlankEnd()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        Assert.Equal(8, row.StartText.Length);              // HH:mm:ss
        Assert.Equal(string.Empty, row.EndText);            // no end yet
        Assert.False(string.IsNullOrEmpty(row.DurationText)); // live (e.g. "00:00")
    }

    [Fact]
    public void Complete_StampsEnd_AndDuration()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        row.Status = "Complete";
        Assert.NotNull(row.EndedAt);
        Assert.False(string.IsNullOrEmpty(row.EndText));
        Assert.False(string.IsNullOrEmpty(row.DurationText));
    }

    [Fact]
    public void TerminalStatus_IsIdempotent_DoesNotMoveEnd()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        row.Status = "Complete";
        DateTime? first = row.EndedAt;
        row.Status = "Error";
        Assert.Equal(first, row.EndedAt);
    }

    [Fact]
    public void WhileTesting_EndBlank_DurationLive()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        row.Status = "Testing"; // no-op vs the default; must not stamp an end
        Assert.Null(row.EndedAt);
        Assert.Equal(string.Empty, row.EndText);
        Assert.False(string.IsNullOrEmpty(row.DurationText)); // live, not blank
    }

    [Fact]
    public void RefreshLiveDuration_RaisesDurationTextChanged()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.RefreshLiveDuration();

        Assert.Contains(nameof(ReconstructorViewModel.VersionEntry.DurationText), raised);
    }
```

(The file needs `using System.ComponentModel;` for the `PropertyChanged` handler — add it if not present.)

- [ ] **Step 2: Run the tests to confirm RED**

Run:
```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~VersionEntryTests" -p:BaseOutputPath=bin2/
```
Expected: **build error** — `RefreshLiveDuration` doesn't exist (CS1061); and once that compiles, `NewRow_HasStartText_LiveDuration_BlankEnd` / `WhileTesting_EndBlank_DurationLive` would FAIL (current `DurationText` is blank while testing).

- [ ] **Step 3: Make `DurationText` live + add `RefreshLiveDuration`**

In `ReScene.NET/ViewModels/ReconstructorViewModel.cs`, in the nested `VersionEntry` class, replace:

```csharp
        /// <summary>Elapsed test time once finished, or empty while running.</summary>
        public string DurationText => EndedAt is { } end
            ? ReconstructorFormatting.FormatTimeSpan(end - StartedAt)
            : string.Empty;
```

with:

```csharp
        /// <summary>
        /// Elapsed test time: counts up live while the test runs, then freezes at the final duration
        /// once the row finishes. Driven once per second by <see cref="RefreshLiveDuration"/>.
        /// </summary>
        public string DurationText =>
            ReconstructorFormatting.FormatTimeSpan((EndedAt ?? DateTime.Now) - StartedAt);

        /// <summary>Raises a change for <see cref="DurationText"/> so the live value re-renders.</summary>
        public void RefreshLiveDuration() => OnPropertyChanged(nameof(DurationText));
```

(`OnPropertyChanged` is `ObservableObject`'s protected method. The `[NotifyPropertyChangedFor(nameof(DurationText))]` on `EndedAt` stays — it freezes the value when the row finishes.)

- [ ] **Step 4: Refresh the active row each timer tick**

In `OnElapsedTimerTick`, add the refresh after the existing timing updates:

```csharp
    private void OnElapsedTimerTick()
    {
        ElapsedTick tick = _progress.Tick();
        ElapsedText = tick.ElapsedText;

        if (tick.HasTiming)
        {
            RemainingText = tick.RemainingText;
            EtaText = tick.EtaText;
        }

        if (VersionEntries.Count > 0)
        {
            VersionEntries[^1].RefreshLiveDuration();
        }
    }
```

- [ ] **Step 5: Run the tests (GREEN) + clean build + full suite**

Run:
```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~VersionEntryTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.NET/ReScene.NET.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: focused tests pass; **0 Warning(s) 0 Error(s)**; full suite **0 failures**.

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/specs/2026-06-28-brute-force-live-duration-design.md \
        docs/superpowers/plans/2026-06-28-brute-force-live-duration.md \
        ReScene.NET/ViewModels/ReconstructorViewModel.cs \
        ReScene.NET.Tests/VersionEntryTests.cs
git commit -m "$(cat <<'EOF'
feat(ui): live Duration for the in-progress brute-force row

DurationText now counts up against DateTime.Now while a row is testing and
freezes at EndedAt - StartedAt once it finishes; the existing 1-second elapsed
timer refreshes the active row (VersionEntries[^1]) each tick. Completed rows
stay frozen.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification (after the task)

- [ ] Clean non-incremental build of `ReScene.NET` with `-p:BaseOutputPath=bin2/`: **0 warnings, 0 errors**.
- [ ] Full `ReScene.NET.Tests` run with `-p:BaseOutputPath=bin2/`: **0 failures**.
- [ ] Delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- [ ] Hand back for a manual check: during a run the active row's Duration counts up each second; on completion it freezes and the next row starts from `00:00`; completed rows never tick.

## Notes on cross-cutting concerns

- **No new timer / no tracker change:** reuses the elapsed timer (only runs during a run) and `VersionEntries[^1]` (always the active row); completed rows are frozen because their `EndedAt` is set, so `DurationText` never evaluates `DateTime.Now` for them.
- **YAGNI:** Start/End untouched; only Duration goes live.
