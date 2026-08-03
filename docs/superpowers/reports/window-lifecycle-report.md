# Window lifecycle — the orphaned progress dialog, and a warning that turned out to be the framework

Diagnosis-first round on a user-reported log line:

```
[Control] PlatformImpl is null, couldn't handle input. (BruteForceProgressWindow #38491183)
```

Two findings came out of it, and they are not the same kind of thing. One is a real, reproduced
application defect that the report's own symptom did not name. The other is the reported line itself,
which is framework behaviour and is deliberately **not** fixed.

## A1. The warning: mechanism established, app route NOT reproduced

Read at its source rather than recalled. `ilspycmd` over `Avalonia.Controls` 11.3.18, `TopLevel`:

- ctor, line 554: `impl.Input = HandleInput;`
- `HandleClosed()`, line 850: `PlatformImpl = null;` — and it never unhooks `impl.Input`
- `HandleInput()`, line 988:

```csharp
private void HandleInput(RawInputEventArgs e)
{
    if (PlatformImpl != null) { Dispatcher.UIThread.Send(…); }
    else { Logger.TryGet(LogEventLevel.Warning, "Control")?.Log(this, "PlatformImpl is null, couldn't handle input."); }
}
```

Measured, not inferred: after `Close()`, `window.PlatformImpl` is `null` while the impl's `Input`
delegate is **still hooked**. So the warning fires whenever a platform impl delivers an input event
after its `TopLevel` has closed — Avalonia correctly dropping late input and logging that it did.

**Four probes, none reproduced it**, and the negative results are the useful part:

| Probe | Result |
|---|---|
| Completion cascade in the VM's real order (`IsRunning=false` → `IsCopying=false`, nested modal open, owner closed before the posted child close ran) | no warning |
| Close, then input via the headless extensions | impossible by construction — they resolve `PlatformImpl as IHeadlessWindow`, null after close, and throw `TopLevel must be a headless window` |
| Orphaned modal outliving its owner | Avalonia tears the child down with the owner; no warning |
| Direct delivery through the retained delegate | blocked — `ITopLevelImpl.Input` is internal, and `MouseMove` is an explicit interface implementation not reachable by name-based reflection |

The reason is structural: **headless has no native message queue**, and this mechanism needs a message
already in flight when teardown runs.

**The hypothesis, labelled as one.** `OnStopCloseClick` calls `Close()` directly, so the window is torn
down in the middle of dispatching a click; on Windows the rest of that click's native messages
(button-up, mouse-move, mouse-leave) then arrive at a dead window. That fits the intermittency and fits
this window being the one named. It is **not confirmed**, and confirming it needs a real Windows
session with the AvaDevBridge attached — attaching to the live process during this round returned no
bridge, so no live logs were available.

**Deliberately not fixed.** The input is correctly discarded; the line is a warning, not an error. With
no reproduced app route, any guard would be a fix aimed at a symptom, which is the thing the debugging
discipline exists to prevent. Offered to the user as an optional live-bridge chase rather than queued
as work.

## A2. What the diagnosis actually found: a dialog nobody can close

Both progress-window controllers post their open and their close onto the dispatcher, and each post
acted on a single tracked reference. Two interleavings orphan a window:

- **A late `Closed`.** The handler cleared the tracked reference unconditionally, so a window closing
  after a newer one had been opened nulled the reference to the **newer** window. The not-busy branch
  then had nothing to close.
- **Two queued opens.** Each post constructed its own window while only the last was tracked, so the
  first was unreachable the moment it was shown.

Either way a modal progress dialog stays on screen forever and the application looks hung behind a
dialog with no way out — a worse symptom than the log line that prompted the investigation.

Measured, with a window-liveness counter:

```
after true:                     1
after clean false:              0      <- the ordinary path was always fine
after true/false/true flicker:  2
after final false:              1      <- orphan
after double-true:              2
after clearing double-true:     1      <- orphan
```

**A rig that measured nothing first.** The initial counter read
`IClassicDesktopStyleApplicationLifetime.Windows`, which is null under headless: it reported `-1` at
every step and would have passed while examining nothing. Windows are now tracked through
`Window.WindowOpenedEvent` and counted live only while their `PlatformImpl` survives.

## A3. The census: the same defect, twice

Population taken from the assembly, not from a list: every type in `ReScene.Manager.Helpers` that
holds a `Window` field. **2** of them, and **both carried the identical defect** —
`IsoProgressWindowController` was line-for-line the same shape as
`ModalProgressWindowController<TWindow>`. The second was only found because it was read after the
first was diagnosed, which is exactly the accident a census is supposed to remove; it is now guarded,
so a third cannot be missed.

`ProgressWindowLifecycle` is excluded with a reason rather than by omission: it holds no window, only
wiring a Cancel button and a `Closing` guard onto a window somebody else owns, so it has nothing to
orphan.

Reach: **3** window types (`FileCopyProgressWindow`, `CRCValidationProgressWindow`,
`ISOProgressWindow`) across **5** surfaces — the RAR Reconstructor, SRS Creator, SRS Reconstructor,
the Create-SRS wizard and the beginner Restore wizard.

## A4. The fix, and the design that a failing test corrected

Both controllers now record the **desired** state and reconcile **once**:

- The latest `busy` / `processing` wins, held in a field.
- A single reconcile is posted; further notifications before it runs update the field instead of
  queueing another post. That kills the two-queued-opens leg.
- `Closed` clears the tracked reference only when `ReferenceEquals(sender, tracked)`. That kills the
  late-`Closed` leg.

**The first attempt was wrong and a shipped test caught it.** It ignored the parameter and re-read the
live flag inside the reconcile. That looked more correct — reconcile to reality — but it broke
`ISOProgressWindowTests.OnProcessingChanged_True_AgainstShownWindowOwner_OpensDialog_ClosesOnFalse`,
whose double is a constant `() => true`: with the live flag permanently true, nothing could ever close
the dialog. The temptation was to call the double unrealistic and change the test. Recording the
caller's latest intent instead fixes the same race without touching the contract the existing tests
encode, and does not depend on the supplied predicate being truthful. The parameter was never the
problem; acting on a *stale* parameter was.

## A5. Evidence

Forced `-t:Rebuild` on the solution: 0 Warning(s), 0 Error(s). **Manager 523/523** (520 + 3),
**App.Core 728/728**.

RED first, against the shipped shape of both controllers:

```
after two queued opens and a close, 1 FileCopyProgressWindow(s) are still open …
after two queued opens and a close, 1 ISOProgressWindow(s) are still open …
```

Break-verified after the fix was in: both controllers reverted via `git stash` to the shape that
shipped, both tests failed again with the same messages, restored and green after.

**Which leg is RED where, stated because the two rigs differ.** The double-open leg reproduces in the
plain-owner rig this file uses and is what the committed test fails on. The flicker leg reproduced in
the diagnosis probe under *nested* modality (owner = `BruteForceProgressWindow` shown over another
window, where `Closed` is raised later) but not in the simpler rig. Both legs are kept — they are both
real interleavings — but only the second is claimed as the committed RED.

**VERIFICATION RAN FROM A NON-DEFAULT OUTPUT PATH, and why.** The user's own
`ReScene.Manager.exe` and a Visual Studio instance held `bin/Debug/net10.0/ReScene.Manager.dll` for
the whole round. That process was left alone deliberately: it may hold the live repro of the very
warning under investigation, and ending a user's session is not this round's call. The build was
directed elsewhere with `-p:BaseOutputPath -p:UseAppHost=false`.

**Since confirmed on the default path.** The user's app exited shortly after the commit landed, and a
forced `-t:Rebuild` plus both suites were re-run normally: 0 Warning(s), 0 Error(s), **523/523** and
**728/728**, identical to the workaround's numbers. The disclosure below stays because it is the
condition the work was actually done under, and because the trap it records outlives this round.

One trap inside that workaround is worth recording, **and its first two write-ups were both wrong in
opposite directions** — which is the more useful half of the story.

The fact, re-measured deliberately rather than recalled. A scratch path under the system temp
directory made **6** tests fail — `HighContrastTokenTests` (×4), `TextContrastAuditTests`,
`IsVisibleCensusTests` — every one with
`DirectoryNotFoundException: could not find ReScene.Manager/Resources above <temp>`. Those censuses
locate the repo by walking up from `AppContext.BaseDirectory`, which does not resolve from outside
the tree. Moving the scratch output *inside* the repo (`.tmp-verify/`, deleted afterwards) restored
all 523.

```
Failed: 6, Passed: 517, Skipped: 0, Total: 523
```

**Nothing was silent, skipped, or undiscovered.** All 523 tests were discovered and the six failed
loudly, with a named exception, in a red run. My own first write-up said the path "silently removes
them from the run" — it does not. A later review corrected that to "never discovered" — it is not
that either; the discovered total was **523** in the bad run, identical to the good one. Both
descriptions were reached by reasoning about what such a path *would* do, and neither survived
running it.

This matters beyond bookkeeping, because a guard was nearly built on the wrong premise: a
discovery-floor meta-test cannot catch this, since the count it would compare against never moved.
The floor shipped anyway (§A7) for the failure mode it *does* catch, with that limitation stated in
the test itself rather than in a report nobody re-reads.

The real lesson is narrower and duller than either wrong version: **an output path outside the
repository breaks tests that read the source tree, and they tell you so.**

## A7. The discovery floor, and what it is honestly for

Each test assembly now asserts a floor under the number of test METHODS it exposes —
`ReScene.Manager.Tests` **503**, `ReScene.App.Core.Tests` **679**. Counting rule, stated in both
tests: methods carrying `Fact`, `Theory`, `AvaloniaFact` or `AvaloniaTheory`, matched by attribute
type NAME so framework-derived attributes count, across every non-abstract class; a theory counts
ONCE regardless of data rows, which is why 503 sits below the run's reported 523.

It does **not** guard the incident above, and both tests say so in their own doc comments. It guards
the failure mode that incident made everyone imagine but that did not actually occur: methods that
stop being discovered at all — a file dropped from compilation, a stray `--filter` left in a CI
invocation, an attribute renamed by a framework upgrade. Those produce a smaller **green** run, and
a smaller green number is the one nobody questions.

Two corrections came out of building it, both from trying things rather than reasoning about them:

- The first counting rule used every type `Assembly.GetTypes()` returns. That would have counted
  internal classes xUnit never runs, so the floor could have held steady while a class stopped being
  discovered — the guard would not have guarded its own headline case. It now counts **public**
  non-abstract classes, which is xUnit's own rule.
- "A test class made non-public" was then dropped from the claims entirely, because attempting that
  sabotage produced a **build error**: xUnit's analyzer rejects it (`xUnit1000`, "Test classes must
  be public"). It can never reach a run, so no guard is needed and claiming one would be padding.

Break-verified against a case that can actually happen: removing one test file from compilation drops
the count to **499** against the floor of 503 and fails by name. Reverted byte-identically.

## A6. Still open

- **The reported warning**, per §A1: diagnosed to mechanism, benign, no app route reproduced. Next
  step if anyone wants it settled is a bridge-enabled Debug session reproduced live, not more headless
  work.
- ~~Re-running verification on the default output path.~~ **Done** — see §A5. Same forced rebuild,
  same 523/728, no difference from the workaround's result.
