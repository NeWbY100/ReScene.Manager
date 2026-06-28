# Brute-Force Progress — Live Duration for the Active Row (Design)

**Date:** 2026-06-28
**Status:** Approved (pending implementation plan)
**Scope:** `ReconstructorViewModel` (the nested `VersionEntry` row model + the elapsed-timer tick).

## Background

The Brute Force Progress window shows a `Duration` column per tested RAR version. Today it stays
blank while a test runs and only shows a value once the row finishes (`v1.6.0` design choice). The
request: show the Duration **counting up live** for the in-progress row.

## Goal

The currently-testing row's `Duration` counts up once per second; completed rows keep their final,
frozen duration. Reuses the window's existing 1-second elapsed timer — no new timer.

## Architecture

### `VersionEntry.DurationText`

Change from "blank until finished" to "live while testing, frozen when done":

```csharp
/// <summary>
/// Elapsed test time: counts up live while the test runs, then freezes at the final duration once
/// the row finishes. Driven once per second by <see cref="RefreshLiveDuration"/> while testing.
/// </summary>
public string DurationText =>
    ReconstructorFormatting.FormatTimeSpan((EndedAt ?? DateTime.Now) - StartedAt);
```

- While testing (`EndedAt` is null) it measures against `DateTime.Now` (live).
- Once finished (`EndedAt` set) it is `EndedAt − StartedAt`, frozen — the `??` short-circuits so a
  completed row never evaluates `DateTime.Now` and never ticks.

Add a method so the view-model can force a per-second UI refresh of the active row:

```csharp
/// <summary>Raises a change for <see cref="DurationText"/> so the live value re-renders.</summary>
public void RefreshLiveDuration() => OnPropertyChanged(nameof(DurationText));
```

(`OnPropertyChanged` is the protected `ObservableObject` method, callable from within the class.)

The `[NotifyPropertyChangedFor(nameof(DurationText))]` on `EndedAt` stays — it freezes the value the
moment the row finishes.

### `ReconstructorViewModel.OnElapsedTimerTick`

The existing 1-second `DispatcherTimer` (started in `StartAsync`, stopped in the `finally`) already
updates Elapsed/Remaining/ETA. Append a refresh of the active row's duration:

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

The last entry is always the row currently being tested (the tracker appends a new row and makes it
active when the version+args key changes), so its Duration updates every second. Completed rows
above it are frozen (their `EndedAt` is set), so refreshing only the last row is sufficient; even a
final tick on a just-completed last row is harmless (it recomputes the same frozen value). The timer
only runs during a run, so there is no idle ticking.

## Data Flow

Timer tick (UI thread) → `VersionEntries[^1].RefreshLiveDuration()` → `PropertyChanged(DurationText)`
→ the bound Duration cell re-reads `DurationText`, which measures against `DateTime.Now` while the
row is testing. When the row finishes, `EndedAt` is set (via the existing `OnStatusChanged` stamp),
its `NotifyPropertyChangedFor` raises `DurationText` once more, and it freezes.

## Error Handling

None needed — display-only; the timer lifecycle is unchanged.

## Scope / Non-Goals

- Only **Duration** goes live. **Start** stays fixed; **End** stays blank until the row finishes (a
  live "End" would be meaningless).
- 1-second granularity (matches the Elapsed field and the `mm:ss` duration format).
- No new timer; no `ReconstructionProgressTracker` change (the active row is `VersionEntries[^1]`).

## Testing & Verification

- `VersionEntry` tests:
  - A new (testing) row: `DurationText` is **non-empty** (live, e.g. `00:00`) and `EndText` is empty
    (updates the old "blank duration while testing" assertions).
  - A finished row (`Status = "Complete"`): `DurationText` is non-empty (frozen) and `EndText` set.
  - `RefreshLiveDuration()` raises `PropertyChanged(nameof(DurationText))`.
- Build: clean non-incremental, 0 warnings; full suite green.
- Manual check: during a run the active row's Duration counts up each second; when it completes the
  value freezes and the next row starts from `00:00`.

## File Structure

- `ReScene.NET/ViewModels/ReconstructorViewModel.cs` — `VersionEntry.DurationText` (live) +
  `RefreshLiveDuration()`; `OnElapsedTimerTick` refreshes the active row.
- `ReScene.NET.Tests/VersionEntryTests.cs` — updated assertions + the `RefreshLiveDuration` test.
