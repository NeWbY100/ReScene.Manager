# Multi-Archive-Set RAR Reconstruction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconstruct multi-disc releases (e.g. RE4 `DVD1`/`DVD2`) by brute-forcing and CRC-verifying each RAR archive set independently, instead of merging all sets into one input and verifying only the first produced volume.

**Architecture:** The library (`ReScene.Lib`, a git submodule) gains a per-set model on `SRRFile` (`ArchiveSets`), a pure volume-match evaluator, and a `Manager` that verifies *every* produced volume (positional assignment + per-volume CRC check, continue-on-near-miss) and returns the winning combo. The app (`ReScene.NET`) loops over the sets, builds per-set options, seeds each later set with the previous winner (narrowed-range run, full-range fallback), relocates verified output into `output\<set-dir>\`, and reports per-set pass/fail.

**Tech Stack:** .NET 10 (`ReScene.Lib` = `net10.0`; `ReScene.NET` = `net10.0-windows`, WPF, CommunityToolkit.Mvvm), xUnit.

**Spec:** `docs/superpowers/specs/2026-06-28-multi-archive-set-reconstruction-design.md`

## Global Constraints

- **Two repos.** Tasks 1–5 modify the **submodule** `ReScene.Lib` (at `E:\Projects\ReScene.NET\ReScene.Lib`, working dir `ReScene.Lib\ReScene\`). Tasks 6–9 modify the **app** `ReScene.NET`. The submodule's lib tasks must be committed in the submodule on a branch, and the **submodule pointer bumped** in the app's feature branch before the app compiles against the new API (see Task 6 step 0).
- **Build/test only with `-p:BaseOutputPath=bin2/`** — the running app locks `ReScene.NET/bin/`. **NEVER kill the app.**
- **Verify non-incrementally:** `dotnet build … --no-incremental` → **0 warnings, 0 errors** (`AnalysisLevel=latest-All`, `EnforceCodeStyleInBuild`).
- After verifying, delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- **Work on the branch chosen at execution start** (`feature/multi-archive-set-reconstruction` in the app; a matching branch in the submodule). Do not switch/rebase/amend.
- **Codebase patterns:** keep `public static` helpers (not `internal`) where the surrounding code does; CommunityToolkit.Mvvm partial-property `[ObservableProperty]`; explicit `<Using Include="System.IO" />` already present in the test csproj SDK config — do not remove.
- **End every commit message** with: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **Per-volume CRCs come only from an SFV** (embedded in the SRR or user-supplied); the `0x71` RARFile blocks carry no CRC. A set with no SFV coverage **fails honestly** — never silently degrade to first-volume-only.
- **Single-set behavior must stay byte-identical** (output bytes and `OutputPath\output\<name>` location); the per-set path is a parameterization, and a 1-set resolve takes the unchanged single-run path (no `.rescene-work` subdir, no relocate).

---

## Task 1: Per-set SRR model + parser grouping (lib)

> **Submodule branch (one-time, before this task):** in the submodule, create the working branch:
> ```bash
> cd E:/Projects/ReScene.NET/ReScene.Lib && git checkout -b feature/multi-archive-set-reconstruction
> ```
> All lib commits (Tasks 1–5, and any `BruteForceProgressEventArgs` change in Task 9) land here; Task 6 step 0 bumps the app's submodule pointer to this branch's HEAD.

**Files:**
- Create: `ReScene.Lib/ReScene/SRR/SrrArchiveSet.cs`
- Modify: `ReScene.Lib/ReScene/SRR/RARVolumeIdentifier.cs` (add `GetArchiveSetKey`)
- Modify: `ReScene.Lib/ReScene/SRR/SRRFile.cs` (`ArchiveSets` property + accumulation; `Load` starts a set per RARFile block)
- Modify: `ReScene.Lib/ReScene/SRR/SRRFileParser.cs` (route embedded-header entries/metadata to the current set)
- Test: `ReScene.Lib/ReScene.Tests/SrrArchiveSetTests.cs`

**Interfaces:**
- Produces: `RARVolumeIdentifier.GetArchiveSetKey(string volumePath) -> string`; `SrrArchiveSet` (public); `SRRFile.ArchiveSets` (`IReadOnlyList<SrrArchiveSet>`).
- Consumes: existing `RARVolumeNaming.GetBaseName(string fileName)` (internal, same assembly).

- [ ] **Step 1: Write the failing tests**

In `ReScene.Lib/ReScene.Tests/SrrArchiveSetTests.cs`:

```csharp
using ReScene.SRR;

namespace ReScene.Tests;

public class SrrArchiveSetTests
{
    [Theory]
    [InlineData("DVD1\\aln-re4a.rar", "DVD1/aln-re4a")]
    [InlineData("DVD1\\aln-re4a.r28", "DVD1/aln-re4a")]
    [InlineData("DVD2/aln-re4b.r00", "DVD2/aln-re4b")]
    [InlineData("aln-re4a.rar", "aln-re4a")]            // root-level, old style
    [InlineData("incite-avtak.ue.xvid.cd1.r05", "incite-avtak.ue.xvid.cd1")]
    [InlineData("rls.part01.rar", "rls")]               // new style
    [InlineData("rls.part002.rar", "rls")]
    public void GetArchiveSetKey_StripsVolumeExtension_KeepsDirectory(string path, string expected)
    {
        Assert.Equal(expected, RARVolumeIdentifier.GetArchiveSetKey(path));
    }

    [Fact]
    public void Load_DirectoryLessTwoSetRelease_GroupsByBaseName()
    {
        // The in-repo fixture: two sets at root, distinguished only by base name.
        var srr = SRRFile.Load("TestData/cleanup_script/007.A.View.To.A.Kill.1985.UE.iNTERNAL.DVDRip.XviD-iNCiTE.fine_2cd.srr");

        Assert.Equal(2, srr.ArchiveSets.Count);
        SrrArchiveSet cd1 = srr.ArchiveSets.Single(s => s.Key.EndsWith("cd1", StringComparison.OrdinalIgnoreCase));
        SrrArchiveSet cd2 = srr.ArchiveSets.Single(s => s.Key.EndsWith("cd2", StringComparison.OrdinalIgnoreCase));

        // Each set's volumes all share its base name; the two sets are disjoint.
        Assert.NotEmpty(cd1.VolumeNames);
        Assert.NotEmpty(cd2.VolumeNames);
        Assert.All(cd1.VolumeNames, v => Assert.Contains("cd1", v, StringComparison.OrdinalIgnoreCase));
        Assert.All(cd2.VolumeNames, v => Assert.Contains("cd2", v, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(cd1.ArchivedFiles.Intersect(cd2.ArchivedFiles, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Load_SingleSetRelease_YieldsOneSetEqualToFlatUnion()
    {
        var srr = SRRFile.Load("TestData/store_little/store_little.srr");

        Assert.Single(srr.ArchiveSets);
        SrrArchiveSet only = srr.ArchiveSets[0];
        Assert.Equal(srr.ArchivedFiles.OrderBy(x => x), only.ArchivedFiles.OrderBy(x => x));
        Assert.Equal(
            srr.ArchivedFileCrcs.OrderBy(kv => kv.Key),
            only.ArchivedFileCrcs.OrderBy(kv => kv.Key));
        Assert.Equal(srr.RARFiles.Select(r => r.FileName), only.VolumeNames);
        Assert.Equal(srr.CompressionMethod, only.CompressionMethod);
    }
}
```

- [ ] **Step 2: Run the tests to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~SrrArchiveSetTests" -p:BaseOutputPath=bin2/
```
Expected: **build error** — `RARVolumeIdentifier.GetArchiveSetKey` and `SRRFile.ArchiveSets` / `SrrArchiveSet` do not exist (CS0117/CS0246).

> Note: if `store_little.srr` is itself multi-set, switch the single-set assertion to another single-set fixture under `TestData/` (e.g. `TestData/best_little/added_empty_file.srr`); pick one whose `RARFiles` all share one base name. Verify by loading and checking `ArchiveSets.Count == 1` once the code exists.

- [ ] **Step 3: Add `GetArchiveSetKey`**

In `ReScene.Lib/ReScene/SRR/RARVolumeIdentifier.cs`, add `using ReScene.RAR;` at the top and this method inside the class:

```csharp
    /// <summary>
    /// Computes the archive-set key for a RAR volume path: its directory (normalized to forward
    /// slashes, trimmed) plus the volume base name (extension stripped via
    /// <see cref="RARVolumeNaming.GetBaseName(string)"/>). Volumes in the same set share this key,
    /// distinguishing sets by directory and/or base name (e.g. "DVD1/aln-re4a", or "…cd1" at root).
    /// </summary>
    public static string GetArchiveSetKey(string volumePath)
    {
        string baseName = RARVolumeNaming.GetBaseName(Path.GetFileName(volumePath));
        string? dir = Path.GetDirectoryName(volumePath);
        if (string.IsNullOrEmpty(dir))
        {
            return baseName;
        }

        string normalizedDir = dir.Replace('\\', '/').Trim('/');
        return normalizedDir.Length == 0 ? baseName : $"{normalizedDir}/{baseName}";
    }
```

- [ ] **Step 4: Add the `SrrArchiveSet` model**

Create `ReScene.Lib/ReScene/SRR/SrrArchiveSet.cs`:

```csharp
namespace ReScene.SRR;

/// <summary>
/// One RAR archive set within an SRR: a single multi-volume series (e.g. a disc's
/// <c>.rar</c>+<c>.r00</c>…) and the files it archives, with the header-derived metadata captured
/// from this set's own first headers. Distinct from the flat <see cref="SRRFile"/> properties,
/// which remain the union across all sets.
/// </summary>
public sealed class SrrArchiveSet
{
    /// <summary>The set key (directory + volume base name), e.g. "DVD1/aln-re4a".</summary>
    public required string Key { get; init; }

    /// <summary>The set's directory relative to the release root ("" for root-level volumes).</summary>
    public required string Directory { get; init; }

    /// <summary>Volume file names in SRR order, with directory prefix (e.g. "DVD1\aln-re4a.rar").</summary>
    public List<string> VolumeNames { get; } = [];

    /// <summary>Content files this set archives (normalized relative paths).</summary>
    public HashSet<string> ArchivedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ArchivedFileCrcs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime> ArchivedFileTimestamps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime> ArchivedFileCreationTimes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DateTime> ArchivedFileAccessTimes { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Header-derived metadata, from this set's first headers.
    public int? CompressionMethod { get; set; }
    public int? DictionarySize { get; set; }
    public int? RARVersion { get; set; }
    public bool? IsSolid { get; set; }
    public bool? HasRecoveryRecord { get; set; }
    public byte? DetectedHostOS { get; set; }
    public uint? DetectedFileAttributes { get; set; }
    public bool? HasLargeFiles { get; set; }
    public uint? DetectedHighPackSize { get; set; }
    public uint? DetectedHighUnpSize { get; set; }
}
```

- [ ] **Step 5: Add `ArchiveSets` + a "current set" accumulator to `SRRFile`**

In `ReScene.Lib/ReScene/SRR/SRRFile.cs`, add alongside the other collection properties (near `_rarFiles`, around line 35–44):

```csharp
    /// <summary>
    /// Gets the archive sets (grouped multi-volume series) parsed from the SRR. A single-set SRR
    /// yields one entry whose data equals the flat union properties.
    /// </summary>
    public IReadOnlyList<SrrArchiveSet> ArchiveSets => _archiveSets;

    internal List<SrrArchiveSet> _archiveSets { get; } = [];

    // Keyed lookup used during parsing to attribute embedded-header entries to the current set.
    internal Dictionary<string, SrrArchiveSet> _archiveSetsByKey { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The set whose embedded headers are currently being parsed (most recent RARFile block).</summary>
    internal SrrArchiveSet? CurrentArchiveSet { get; set; }

    /// <summary>
    /// Begins (or resumes) the archive set for a RARFile block and records the volume name. Called by
    /// <see cref="Load"/> as each type-0x71 block is read, so subsequent embedded-header entries are
    /// attributed to this set.
    /// </summary>
    internal void BeginArchiveSetForVolume(string volumeName)
    {
        string key = RARVolumeIdentifier.GetArchiveSetKey(volumeName);
        if (!_archiveSetsByKey.TryGetValue(key, out SrrArchiveSet? set))
        {
            string? dir = Path.GetDirectoryName(volumeName);
            set = new SrrArchiveSet
            {
                Key = key,
                Directory = string.IsNullOrEmpty(dir) ? string.Empty : dir,
            };
            _archiveSetsByKey[key] = set;
            _archiveSets.Add(set);
        }

        set.VolumeNames.Add(volumeName);
        CurrentArchiveSet = set;
    }
```

Then in `Load`, immediately after `srr._rarFiles.Add(rarBlock);` (SRRFile.cs:511), before `ParseEmbeddedRarHeaders`:

```csharp
                    srr._rarFiles.Add(rarBlock);
                    srr.BeginArchiveSetForVolume(rarBlock.FileName);
```

- [ ] **Step 6: Route embedded-header entries + metadata to the current set**

In `ReScene.Lib/ReScene/SRR/SRRFileParser.cs`:

In `ProcessArchiveHeader` (around line 357), after the existing `??=` assignments, add per-set capture:

```csharp
        SrrArchiveSet? set = srr.CurrentArchiveSet;
        if (set != null)
        {
            set.IsSolid ??= header.IsSolid;
            set.HasRecoveryRecord ??= header.HasRecoveryRecord;
        }
```

In `ProcessFileHeader` (around line 367), after the existing global `if (srr.CompressionMethod == null)` block and the `??=` host-OS/attribute captures, add:

```csharp
        SrrArchiveSet? set = srr.CurrentArchiveSet;
        if (set != null)
        {
            set.CompressionMethod ??= header.CompressionMethod;
            set.DictionarySize ??= header.DictionarySizeKB;
            set.RARVersion ??= header.UnpackVersion;
            set.HasLargeFiles ??= header.HasLargeSize;
            set.DetectedHostOS ??= header.HostOS;
            set.DetectedFileAttributes ??= header.FileAttributes;
            if (header.HasLargeSize)
            {
                set.DetectedHighPackSize ??= header.HighPackSize;
                set.DetectedHighUnpSize ??= header.HighUnpSize;
            }
        }
```

In `AddArchiveEntry` (around line 663), after the entry is added to the flat `srr.ArchivedFiles` / `srr.ArchivedFileCrcs` and times are set, route the same data to the current set. Add a `SrrArchiveSet? set = srr.CurrentArchiveSet;` near the top of the method (after the `normalized` guard), then at the end (after `SetFileTimes`):

```csharp
        if (set != null)
        {
            if (isDirectory)
            {
                // directories are not tracked per-set for input (files drive the input list)
            }
            else
            {
                set.ArchivedFiles.Add(normalized);
                if (fileCRC.HasValue && !set.ArchivedFileCrcs.ContainsKey(normalized))
                {
                    set.ArchivedFileCrcs[normalized] = fileCRC.Value.ToString("x8");
                }

                if (modifiedTime.HasValue && !set.ArchivedFileTimestamps.ContainsKey(normalized))
                {
                    set.ArchivedFileTimestamps[normalized] = modifiedTime.Value;
                }

                if (creationTime.HasValue && !set.ArchivedFileCreationTimes.ContainsKey(normalized))
                {
                    set.ArchivedFileCreationTimes[normalized] = creationTime.Value;
                }

                if (accessTime.HasValue && !set.ArchivedFileAccessTimes.ContainsKey(normalized))
                {
                    set.ArchivedFileAccessTimes[normalized] = accessTime.Value;
                }
            }
        }
```

(The directory `return;` path already returns early for directories; place the file-branch code on the non-directory path so it is reached. Mirror the existing control flow — the per-set block runs only for files.)

Do the equivalent in the RAR5 file-header path `ProcessRar5FileHeader` (line 532): add the same `set.CompressionMethod ??= …`/`RARVersion ??= 50`/etc. capture using `srr.CurrentArchiveSet`. The content entries already flow through `AddArchiveEntry`, so they are covered.

- [ ] **Step 7: Run the tests (GREEN) + clean build**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~SrrArchiveSetTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: focused tests pass; **0 Warning(s) 0 Error(s)**; full lib suite **0 failures** (the additive `ArchiveSets` must not perturb existing parser tests).

- [ ] **Step 8: Commit (in the submodule)**

```bash
cd E:/Projects/ReScene.NET/ReScene.Lib
git add ReScene/SRR/SrrArchiveSet.cs ReScene/SRR/RARVolumeIdentifier.cs ReScene/SRR/SRRFile.cs ReScene/SRR/SRRFileParser.cs ReScene.Tests/SrrArchiveSetTests.cs
git commit -m "$(cat <<'EOF'
feat(srr): group SRR volumes into per-archive-set model

SRRFile.ArchiveSets groups volumes by directory + base name and attributes each
set's archived files, CRCs, timestamps, and header metadata during parsing. Flat
union properties are unchanged. Adds RARVolumeIdentifier.GetArchiveSetKey.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Tolerant SFV parser from bytes/lines (lib)

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/IO/SFVFile.cs` (add `ParseLines` + `ParseBytes`; `ReadFile` delegates)
- Test: `ReScene.Lib/ReScene.Tests/SFVFileTests.cs` (add cases; create if absent)

**Interfaces:**
- Produces: `SFVFile.ParseLines(IEnumerable<string> lines, bool tolerant) -> SFVFile`; `SFVFile.ParseBytes(byte[] data, bool tolerant) -> SFVFile`.

- [ ] **Step 1: Write the failing tests**

Add to `ReScene.Lib/ReScene.Tests/SFVFileTests.cs` (create the file with this content if it does not exist):

```csharp
using System.Text;
using ReScene.Core.IO;

namespace ReScene.Tests;

public class SFVFileTests
{
    [Fact]
    public void ParseBytes_ParsesNameCrcPairs()
    {
        byte[] data = Encoding.ASCII.GetBytes("; comment\r\naln-re4a.r00 88b361c9\r\naln-re4a.rar f1a3ec0d\r\n");
        SFVFile sfv = SFVFile.ParseBytes(data, tolerant: true);

        Assert.Equal(2, sfv.Entries.Count);
        Assert.Equal("aln-re4a.r00", sfv.Entries[0].FileName);
        Assert.Equal("88b361c9", sfv.Entries[0].CRC);
    }

    [Fact]
    public void ParseLines_Tolerant_SkipsMalformedInsteadOfThrowing()
    {
        string[] lines = ["good.r00 deadbeef", "this line is broken", "good.r01 cafebabe"];
        SFVFile sfv = SFVFile.ParseLines(lines, tolerant: true);

        Assert.Equal(2, sfv.Entries.Count);
        Assert.Equal("good.r01", sfv.Entries[1].FileName);
    }

    [Fact]
    public void ParseLines_Strict_ThrowsOnMalformed()
    {
        string[] lines = ["good.r00 deadbeef", "broken"];
        Assert.Throws<InvalidDataException>(() => SFVFile.ParseLines(lines, tolerant: false));
    }
}
```

- [ ] **Step 2: Run to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~SFVFileTests" -p:BaseOutputPath=bin2/
```
Expected: build error — `SFVFile.ParseBytes` / `ParseLines` do not exist.

- [ ] **Step 3: Implement `ParseLines` / `ParseBytes`; delegate `ReadFile`**

In `ReScene.Lib/ReScene/Core/IO/SFVFile.cs`, add `using System.Text;` and replace the body of `ReadFile` plus add the two parsers:

```csharp
    public static SFVFile ReadFile(string filePath)
    {
        var sfvFile = ParseLines(File.ReadAllLines(filePath), tolerant: false);
        sfvFile.FileInfo = new FileInfo(filePath);
        return sfvFile;
    }

    /// <summary>Parses SFV text from raw bytes (decoded as Latin-1, the SFV norm), tolerant or strict.</summary>
    public static SFVFile ParseBytes(byte[] data, bool tolerant)
    {
        string text = Encoding.Latin1.GetString(data);
        return ParseLines(text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries), tolerant);
    }

    /// <summary>
    /// Parses SFV lines into filename-CRC entries. When <paramref name="tolerant"/> is true,
    /// malformed lines are skipped; otherwise an <see cref="InvalidDataException"/> is thrown
    /// (the legacy <see cref="ReadFile"/> contract).
    /// </summary>
    public static SFVFile ParseLines(IEnumerable<string> lines, bool tolerant)
    {
        SFVFile sfvFile = new();
        foreach (string fileLine in lines)
        {
            if (string.IsNullOrEmpty(fileLine) || fileLine.StartsWith(':') || fileLine.StartsWith('#') || fileLine.StartsWith(';'))
            {
                continue;
            }

            string[] items = fileLine.Split(" ", StringSplitOptions.RemoveEmptyEntries);
            if (items.Length < 2 || items[1].Length != 8)
            {
                if (tolerant)
                {
                    continue;
                }

                throw new InvalidDataException("Invalid SFV file format.");
            }

            sfvFile.Entries.Add(new SFVFileEntry(items[0], items[1].ToLowerInvariant()));
        }

        return sfvFile;
    }
```

(Note: the original `ReadFile` split filename on a single space and required exactly 2+ items with an 8-char CRC; this preserves that. SFV filenames containing spaces were already unsupported and remain so — no behavior change.)

- [ ] **Step 4: Run (GREEN) + clean build + full suite**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~SFVFileTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: focused pass; **0 Warning(s) 0 Error(s)**; full suite green (existing `SFVFile.ReadFile` callers/tests unaffected).

- [ ] **Step 5: Commit (submodule)**

```bash
cd E:/Projects/ReScene.NET/ReScene.Lib
git add ReScene/Core/IO/SFVFile.cs ReScene.Tests/SFVFileTests.cs
git commit -m "$(cat <<'EOF'
feat(sfv): add tolerant SFVFile.ParseLines/ParseBytes; ReadFile delegates

Lets the embedded SFV stored block be parsed from raw bytes (skipping junk
lines) while ReadFile keeps its strict path. No behavior change for existing
callers.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Pure volume-match evaluator (lib)

**Files:**
- Create: `ReScene.Lib/ReScene/Core/VolumeMatchEvaluator.cs`
- Test: `ReScene.Lib/ReScene.Tests/VolumeMatchEvaluatorTests.cs`

**Interfaces:**
- Produces: `VolumeMatchEvaluator.Evaluate(IReadOnlyList<string> producedCrcs, IReadOnlyList<(string Name, string Crc)> expectedInOrder) -> VolumeMatchResult` with `VolumeMatchResult(bool AllMatch, IReadOnlyList<VolumeMatch> Volumes, VolumeMatch? FirstMismatch, bool CountMismatch)` and `VolumeMatch(int Index, string ExpectedName, string ExpectedCrc, string ActualCrc, bool Match)`.

- [ ] **Step 1: Write the failing tests**

Create `ReScene.Lib/ReScene.Tests/VolumeMatchEvaluatorTests.cs`:

```csharp
using ReScene.Core;

namespace ReScene.Tests;

public class VolumeMatchEvaluatorTests
{
    [Fact]
    public void Evaluate_AllMatch_AssignsNamesPositionally()
    {
        var produced = new[] { "f1a3ec0d", "88b361c9" };
        var expected = new[] { ("aln-re4a.rar", "f1a3ec0d"), ("aln-re4a.r00", "88b361c9") };

        VolumeMatchResult r = VolumeMatchEvaluator.Evaluate(produced, expected);

        Assert.True(r.AllMatch);
        Assert.False(r.CountMismatch);
        Assert.Null(r.FirstMismatch);
        Assert.Equal("aln-re4a.r00", r.Volumes[1].ExpectedName);
    }

    [Fact]
    public void Evaluate_NearMiss_ReportsFirstMismatch_NotAllMatch()
    {
        var produced = new[] { "f1a3ec0d", "ffffffff" };               // vol 1 ok, vol 2 wrong
        var expected = new[] { ("x.rar", "f1a3ec0d"), ("x.r00", "88b361c9") };

        VolumeMatchResult r = VolumeMatchEvaluator.Evaluate(produced, expected);

        Assert.False(r.AllMatch);
        Assert.NotNull(r.FirstMismatch);
        Assert.Equal(1, r.FirstMismatch!.Index);
        Assert.Equal("x.r00", r.FirstMismatch.ExpectedName);
    }

    [Fact]
    public void Evaluate_CrcCompareIsCaseInsensitive()
    {
        VolumeMatchResult r = VolumeMatchEvaluator.Evaluate(["AABBCCDD"], [("x.rar", "aabbccdd")]);
        Assert.True(r.AllMatch);
    }

    [Fact]
    public void Evaluate_CountMismatch_IsNotAMatch()
    {
        VolumeMatchResult r = VolumeMatchEvaluator.Evaluate(["aabbccdd"], [("x.rar", "aabbccdd"), ("x.r00", "11223344")]);
        Assert.True(r.CountMismatch);
        Assert.False(r.AllMatch);
    }
}
```

- [ ] **Step 2: Run to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~VolumeMatchEvaluatorTests" -p:BaseOutputPath=bin2/
```
Expected: build error — type does not exist.

- [ ] **Step 3: Implement the evaluator**

Create `ReScene.Lib/ReScene/Core/VolumeMatchEvaluator.cs`:

```csharp
namespace ReScene.Core;

/// <summary>One produced volume's positional comparison against its expected name + CRC.</summary>
public sealed record VolumeMatch(int Index, string ExpectedName, string ExpectedCrc, string ActualCrc, bool Match);

/// <summary>The result of comparing a produced volume set against the expected per-volume CRCs.</summary>
public sealed record VolumeMatchResult(
    bool AllMatch,
    IReadOnlyList<VolumeMatch> Volumes,
    VolumeMatch? FirstMismatch,
    bool CountMismatch);

/// <summary>
/// Pure comparison of a produced multi-volume RAR set against the expected per-volume CRCs.
/// Volumes are assigned to expected names positionally (RAR emits volumes in deterministic order);
/// CRC is the verification, not the assignment key. A full match requires equal counts and every
/// position's CRC to match (case-insensitive).
/// </summary>
public static class VolumeMatchEvaluator
{
    public static VolumeMatchResult Evaluate(
        IReadOnlyList<string> producedCrcs,
        IReadOnlyList<(string Name, string Crc)> expectedInOrder)
    {
        bool countMismatch = producedCrcs.Count != expectedInOrder.Count;
        int n = Math.Min(producedCrcs.Count, expectedInOrder.Count);
        var volumes = new List<VolumeMatch>(n);
        VolumeMatch? firstMismatch = null;

        for (int i = 0; i < n; i++)
        {
            (string name, string expectedCrc) = expectedInOrder[i];
            string actual = producedCrcs[i];
            bool match = string.Equals(actual, expectedCrc, StringComparison.OrdinalIgnoreCase);
            var vm = new VolumeMatch(i, name, expectedCrc, actual, match);
            volumes.Add(vm);
            if (!match && firstMismatch == null)
            {
                firstMismatch = vm;
            }
        }

        bool allMatch = !countMismatch && firstMismatch == null;
        return new VolumeMatchResult(allMatch, volumes, firstMismatch, countMismatch);
    }
}
```

- [ ] **Step 4: Run (GREEN) + clean build**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~VolumeMatchEvaluatorTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental
```
Expected: pass; **0 Warning(s) 0 Error(s)**.

- [ ] **Step 5: Commit (submodule)**

```bash
cd E:/Projects/ReScene.NET/ReScene.Lib
git add ReScene/Core/VolumeMatchEvaluator.cs ReScene.Tests/VolumeMatchEvaluatorTests.cs
git commit -m "$(cat <<'EOF'
feat(core): pure VolumeMatchEvaluator for full per-volume CRC verification

Positional assignment with per-position CRC check; reports first mismatch and
count mismatch. No rar.exe dependency.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Options fields + run-result types (lib, additive)

**Files:**
- Create: `ReScene.Lib/ReScene/Core/BruteForceRunResult.cs`
- Modify: `ReScene.Lib/ReScene/Core/BruteForceOptions.cs` (add `ExpectedVolumeCrcs`)
- Test: `ReScene.Lib/ReScene.Tests/BruteForceRunResultTests.cs`

**Interfaces:**
- Produces: `WinningCombo(int Version, RARCommandLineArgument[] Args)`; `BruteForceRunResult(bool Success, WinningCombo? Combo)`; `BruteForceOptions.ExpectedVolumeCrcs` (`Dictionary<string,string>`, volume **base filename** → CRC).

- [ ] **Step 1: Write the failing test**

Create `ReScene.Lib/ReScene.Tests/BruteForceRunResultTests.cs`:

```csharp
using ReScene.Core;
using ReScene.RAR;

namespace ReScene.Tests;

public class BruteForceRunResultTests
{
    [Fact]
    public void WinningCombo_CarriesVersionAndArgs()
    {
        var args = new[] { new RARCommandLineArgument("-m0") };
        var combo = new WinningCombo(351, args);
        var result = new BruteForceRunResult(true, combo);

        Assert.True(result.Success);
        Assert.Equal(351, result.Combo!.Version);
        Assert.Single(result.Combo.Args);
    }

    [Fact]
    public void Options_ExpectedVolumeCrcs_IsCaseInsensitive()
    {
        var opts = new BruteForceOptions("a", "b", "c");
        opts.ExpectedVolumeCrcs["aln-re4a.rar"] = "f1a3ec0d";
        Assert.True(opts.ExpectedVolumeCrcs.ContainsKey("ALN-RE4A.RAR"));
    }
}
```

(If `RARCommandLineArgument` has no single-string constructor, the implementer adjusts the test to the real constructor; confirm by reading `ReScene.Lib/ReScene/RAR/RARCommandLineArgument.cs`.)

- [ ] **Step 2: Run to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~BruteForceRunResultTests" -p:BaseOutputPath=bin2/
```
Expected: build error — `WinningCombo`/`BruteForceRunResult`/`ExpectedVolumeCrcs` do not exist.

- [ ] **Step 3: Add the types and the options field**

Create `ReScene.Lib/ReScene/Core/BruteForceRunResult.cs`:

```csharp
using ReScene.RAR;

namespace ReScene.Core;

/// <summary>The version + command-line argument combination that reproduced a set, byte-exact.</summary>
public sealed record WinningCombo(int Version, RARCommandLineArgument[] Args);

/// <summary>The outcome of a brute-force run: success plus the winning combo (for seeding the next set).</summary>
public sealed record BruteForceRunResult(bool Success, WinningCombo? Combo);
```

In `ReScene.Lib/ReScene/Core/BruteForceOptions.cs`, add after the `Hashes` property (line 38):

```csharp
    /// <summary>
    /// Expected per-volume CRC32 values keyed by volume base filename (e.g. "aln-re4a.r00"), used to
    /// verify EVERY produced volume. When populated (and CompleteAllVolumes is set), the engine
    /// verifies the whole set; when empty, it falls back to the first-volume-only check.
    /// </summary>
    public Dictionary<string, string> ExpectedVolumeCrcs { get; } = new(StringComparer.OrdinalIgnoreCase);
```

- [ ] **Step 4: Run (GREEN) + clean build**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~BruteForceRunResultTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental
```
Expected: pass; **0 Warning(s) 0 Error(s)**.

- [ ] **Step 5: Commit (submodule)**

```bash
cd E:/Projects/ReScene.NET/ReScene.Lib
git add ReScene/Core/BruteForceRunResult.cs ReScene/Core/BruteForceOptions.cs ReScene.Tests/BruteForceRunResultTests.cs
git commit -m "$(cat <<'EOF'
feat(core): add WinningCombo/BruteForceRunResult + ExpectedVolumeCrcs option

Additive types for full-volume verification and cross-set seeding.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Manager full-volume verification + winning combo (lib)

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (`BruteForceRARVersionAsync` return type; `TryProcessCommandLinesAsync` verification/near-miss; `RenameMatchedOutput` dir-create + StopOnFirstMatch decoupling)
- Modify: `ReScene.Lib/ReScene/Core/MatchedRarWriter.cs` (`GetVolumeCrcsInOrder` helper) — or compute in Manager
- Test: `ReScene.Lib/ReScene.Tests/ManagerVerificationTests.cs`

**Interfaces:**
- Consumes: `VolumeMatchEvaluator.Evaluate`, `WinningCombo`, `BruteForceRunResult`, `BruteForceOptions.ExpectedVolumeCrcs`, `MatchedRarWriter.GetAllVolumeFiles`, `HashCalculator.Calculate`.
- Produces: `Manager.BruteForceRARVersionAsync(...) -> Task<BruteForceRunResult>` (was `Task<bool>`).

> **Risk note:** `Manager` shells out to `rar.exe`, so the end-to-end behavior is integration/manual-tested, not unit-tested. Unit tests here target the *extractable* pure pieces: building the ordered expected list from options, and the rename/dir-create. The full path is covered by Task 3's evaluator tests + the manual checklist.

- [ ] **Step 1: Write the failing tests (extractable pieces)**

Create `ReScene.Lib/ReScene.Tests/ManagerVerificationTests.cs`:

```csharp
using ReScene.Core;
using ReScene.RAR;

namespace ReScene.Tests;

public class ManagerVerificationTests
{
    [Fact]
    public void BuildExpectedInOrder_MapsVolumeNamesToCrcsByBaseFilename()
    {
        var opts = new BruteForceOptions("w", "r", "o")
        {
            RAROptions = new RAROptions { OriginalRarFileNames = ["DVD1\\aln-re4a.rar", "DVD1\\aln-re4a.r00"] }
        };
        opts.ExpectedVolumeCrcs["aln-re4a.rar"] = "f1a3ec0d";
        opts.ExpectedVolumeCrcs["aln-re4a.r00"] = "88b361c9";

        var expected = Manager.BuildExpectedInOrder(opts);

        Assert.Equal(2, expected.Count);
        Assert.Equal(("aln-re4a.rar", "f1a3ec0d"), expected[0]);
        Assert.Equal(("aln-re4a.r00", "88b361c9"), expected[1]);
    }

    [Fact]
    public void BuildExpectedInOrder_MissingCrc_OmitsTheVolume()
    {
        var opts = new BruteForceOptions("w", "r", "o")
        {
            RAROptions = new RAROptions { OriginalRarFileNames = ["x.rar", "x.r00"] }
        };
        opts.ExpectedVolumeCrcs["x.rar"] = "aabbccdd"; // x.r00 missing

        var expected = Manager.BuildExpectedInOrder(opts);
        Assert.Single(expected); // only the covered volume; caller treats partial coverage as not-verifiable
    }
}
```

- [ ] **Step 2: Run to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~ManagerVerificationTests" -p:BaseOutputPath=bin2/
```
Expected: build error — `Manager.BuildExpectedInOrder` does not exist (and the return-type change is not yet made).

- [ ] **Step 3: Add `BuildExpectedInOrder` + change the return type**

In `ReScene.Lib/ReScene/Core/Manager.cs`, add this `public static` helper (so the test can reach it; keep with the other statics):

```csharp
    /// <summary>
    /// Builds the expected (volume base filename, CRC) list in volume order from the options'
    /// original names and <see cref="BruteForceOptions.ExpectedVolumeCrcs"/>. Volumes with no
    /// expected CRC are omitted; callers treat a count below the produced-volume count as
    /// not-fully-verifiable.
    /// </summary>
    public static List<(string Name, string Crc)> BuildExpectedInOrder(BruteForceOptions options)
    {
        var result = new List<(string, string)>();
        foreach (string volume in options.RAROptions.OriginalRarFileNames)
        {
            string name = Path.GetFileName(volume);
            if (options.ExpectedVolumeCrcs.TryGetValue(name, out string? crc))
            {
                result.Add((name, crc));
            }
        }

        return result;
    }
```

Change the signature `public async Task<bool> BruteForceRARVersionAsync(...)` → `public async Task<BruteForceRunResult> BruteForceRARVersionAsync(...)`. Update its returns:
- Custom-packer early return (around line 177-180): `return new BruteForceRunResult(result, null);`
- Final return (line 330): track a `WinningCombo? winningCombo = null;` declared with `found`; set it when a combo fully matches (threaded up from `TryProcessCommandLinesAsync`); `return new BruteForceRunResult(found, winningCombo);`
- The early `return false;` guards (no RAR dirs, validation fail, lines 189/206) → `return new BruteForceRunResult(false, null);`

Change `TryProcessCommandLinesAsync` to return `(bool Found, int NewProgress, WinningCombo? Combo)` and the caller (line 283) to capture the combo:

```csharp
                    (bool foundCombination, int newProgress, WinningCombo? combo) =
                        await TryProcessCommandLinesAsync(options, version, rarVersionDirectoryPath, inputFilesDir, totalProgressSize, currentProgress, bruteForceStartDateTime, fileHashes, a, b).ConfigureAwait(false);
                    currentProgress = newProgress;
                    if (foundCombination)
                    {
                        found = true;
                        winningCombo = combo;
                        if (stopOnFirstMatch)
                        {
                            // ... existing log + break
                        }
                        // ... existing else log
                    }
```

- [ ] **Step 4: Full-volume verification + continue-on-near-miss in `TryProcessCommandLinesAsync`**

In the `// ---- MATCH FOUND ----` region (after `await runningProcessTask` at line ~699, before `LogMatchDetails`/`RenameMatchedOutput`), insert full verification **only when** we have all volumes and expected CRCs:

```csharp
                // ---- MATCH FOUND (first volume) ----

                if (runningProcessTask != null && !runningProcessTask.IsCompleted)
                {
                    _logger.Information(this, "First volume matched, completing all volumes...", LogTarget.System);
                    await runningProcessTask.ConfigureAwait(false);
                }

                // Full per-volume verification (recreate-whole-release mode with known CRCs).
                List<(string Name, string Crc)> expectedInOrder = BuildExpectedInOrder(options);
                if (options.RAROptions.CompleteAllVolumes && expectedInOrder.Count > 0)
                {
                    string? completed = MatchedRarWriter.FindCreatedRARFile(rarFilePath);
                    List<string> producedVolumes = completed != null ? MatchedRarWriter.GetAllVolumeFiles(completed) : [];

                    // Patch all volumes before hashing if patching is needed (CRCs are of the final bytes).
                    if (completed != null && options.RAROptions.NeedsPatching)
                    {
                        PatchRARFilesHostOS(completed, options.RAROptions);
                    }

                    var producedCrcs = producedVolumes
                        .Select(v => HashCalculator.Calculate(options.HashType, v))
                        .ToList();

                    VolumeMatchResult verify = VolumeMatchEvaluator.Evaluate(producedCrcs, expectedInOrder);
                    if (!verify.AllMatch)
                    {
                        VolumeMatch? m = verify.FirstMismatch;
                        string detail = verify.CountMismatch
                            ? $"produced {producedCrcs.Count} volume(s), expected {expectedInOrder.Count}"
                            : $"{m?.ExpectedName} CRC mismatch (expected {m?.ExpectedCrc}, got {m?.ActualCrc})";
                        _logger.Information(this, $"{rarVersionDirectoryName} / {displayArguments}: first volume matched but {detail} — continuing", LogTarget.Phase2);

                        if (options.RAROptions.DeleteRARFiles && completed != null)
                        {
                            DeleteRARFileAndVolumes(completed);
                        }

                        continue; // near-miss: keep brute-forcing
                    }
                }

                // ---- FULL MATCH ----
                LogMatchDetails(options, rarVersionDirectoryName, displayArguments, hash, actualRarFilePath);
                RenameMatchedOutput(options, rarFilePath, actualRarFilePath, rarOutputDir);
                return (true, currentProgress, new WinningCombo(version, commandLineArguments));
```

Note: this **moves** the existing `await runningProcessTask` block and the `LogMatchDetails`/`RenameMatchedOutput`/`return` to after verification. The existing patching-on-first-volume at line 654-658 stays for the cheap path; the new block re-patches all volumes before hashing (so hashes reflect final bytes). Ensure `NeedsPatching` volumes are not double-patched destructively — `PatchRARFilesHostOS` is idempotent for already-correct headers (it compares before writing), so re-running is safe.

When `CompleteAllVolumes` is **false** or `expectedInOrder` is empty, fall through to the existing first-volume `LogMatchDetails`/`RenameMatchedOutput`/`return` (now returning the 3-tuple with `new WinningCombo(version, commandLineArguments)`).

- [ ] **Step 5: `RenameMatchedOutput` — create dirs + decouple from StopOnFirstMatch**

In `RenameMatchedOutput` (line 810):
- Change the `useOriginalNames` gate (lines 816-818) to drop `StopOnFirstMatch`:

```csharp
        bool useOriginalNames = options.RAROptions.RenameToOriginalNames &&
                                originalNames.Count > 0;
```

- Before each `MatchedRarWriter.MoveMatchedFile(... , outputPath)` call (the `CompleteAllVolumes` loop at line 841 and the single-file branch at line 861), add:

```csharp
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
```

- [ ] **Step 6: Update lib callers of the changed return type**

Search the submodule for callers of `BruteForceRARVersionAsync` and any lib tests asserting `bool`:

```bash
cd E:/Projects/ReScene.NET/ReScene.Lib
grep -rn "BruteForceRARVersionAsync" ReScene ReScene.Tests
```
Update each to use `BruteForceRunResult` (`.Success`). (The app's `BruteForceService` is updated in Task 6.)

- [ ] **Step 7: Run tests + clean build + full suite**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~ManagerVerificationTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: focused pass; **0 Warning(s) 0 Error(s)**; full lib suite green (any lib brute-force integration tests now read `.Success`).

- [ ] **Step 8: Commit (submodule)**

```bash
cd E:/Projects/ReScene.NET/ReScene.Lib
git add ReScene/Core/Manager.cs ReScene/Core/MatchedRarWriter.cs ReScene.Tests/ManagerVerificationTests.cs
git commit -m "$(cat <<'EOF'
feat(core): verify all produced volumes; return winning combo

Manager now completes and CRC-verifies every volume (positional assignment) in
recreate-whole-release mode, continuing the brute-force on a near-miss instead of
committing a wrong rename. Rename creates output subdirectories and no longer
requires StopOnFirstMatch. BruteForceRARVersionAsync returns BruteForceRunResult
carrying the winning version+args.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: App brute-force service returns the run result

**Files:**
- Modify: `ReScene.NET/Services/IBruteForceService.cs`, `ReScene.NET/Services/BruteForceService.cs`
- Modify: `ReScene.NET/ViewModels/ReconstructorViewModel.cs` (the single `RunAsync` call site — minimal compile fix)
- Test: build + existing `ReScene.NET.Tests`

**Interfaces:**
- Produces: `IBruteForceService.RunAsync(...) -> Task<BruteForceRunResult>`.

- [ ] **Step 0: Bump the submodule pointer (one-time, before app tasks compile)**

The app references the submodule's compiled API. After Tasks 1–5 are committed in the submodule, record the new commit in the app's feature branch:

```bash
cd E:/Projects/ReScene.NET
git add ReScene.Lib
git commit -m "$(cat <<'EOF'
chore: bump ReScene.Lib (per-archive-set model + full-volume verification)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 1: Change the service signatures**

In `ReScene.NET/Services/IBruteForceService.cs`:

```csharp
    public Task<BruteForceRunResult> RunAsync(BruteForceOptions options, CancellationToken cancellationToken = default);
```

In `ReScene.NET/Services/BruteForceService.cs` line 17 + 32:

```csharp
    public async Task<BruteForceRunResult> RunAsync(BruteForceOptions options, CancellationToken cancellationToken = default)
    {
        // ... unchanged setup ...
        return await manager.BruteForceRARVersionAsync(options, cancellationToken);
    }
```

- [ ] **Step 2: Fix the VM call site minimally**

In `ReScene.NET/ViewModels/ReconstructorViewModel.cs`, find the `await _bruteForceService.RunAsync(...)` call (near the StartAsync tail). Capture the result and use `.Success` where the old `bool` was used:

```csharp
        BruteForceRunResult runResult = await _bruteForceService.RunAsync(options, token);
        bool found = runResult.Success;
        // ... existing logic that used the bool now uses `found`
```

(Task 8 replaces this single call with the per-set loop; this step only keeps the app compiling.)

- [ ] **Step 3: Build + full app suite**

```bash
dotnet build E:/Projects/ReScene.NET/ReScene.NET/ReScene.NET.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: **0 Warning(s) 0 Error(s)**; existing app tests green. Update any test double implementing `IBruteForceService` to the new return type (search `IBruteForceService` in `ReScene.NET.Tests`).

- [ ] **Step 4: Commit (app)**

```bash
cd E:/Projects/ReScene.NET
git add ReScene.NET/Services/IBruteForceService.cs ReScene.NET/Services/BruteForceService.cs ReScene.NET/ViewModels/ReconstructorViewModel.cs ReScene.NET.Tests
git commit -m "$(cat <<'EOF'
refactor(reconstructor): brute-force service returns BruteForceRunResult

Threads the winning combo to the view-model for cross-set seeding.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Carry archive sets into the import state (app)

**Files:**
- Modify: `ReScene.NET/ViewModels/Reconstruction/ImportedSrrInfo.cs` (+`ArchiveSets`)
- Modify: `ReScene.NET/ViewModels/Reconstruction/SrrImportParser.cs` (populate from `srr.ArchiveSets`)
- Modify: `ReScene.NET/ViewModels/Reconstruction/ReconstructionImportState.cs` (+`ArchiveSets`, reset in `Clear`)
- Modify: `ReScene.NET/ViewModels/ReconstructorViewModel.cs` (copy `info.ArchiveSets` into `_import` on the fresh-import path)
- Test: `ReScene.NET.Tests/SrrImportParserArchiveSetTests.cs`

**Interfaces:**
- Produces: `ImportedSrrInfo.ArchiveSets` and `ReconstructionImportState.ArchiveSets` (both `IReadOnlyList<SrrArchiveSet>`).

- [ ] **Step 1: Write the failing test**

Create `ReScene.NET.Tests/SrrImportParserArchiveSetTests.cs`:

```csharp
using ReScene.NET.ViewModels.Reconstruction;
using ReScene.SRR;

namespace ReScene.NET.Tests;

public class SrrImportParserArchiveSetTests
{
    [Fact]
    public void Parse_MultiSetSrr_ExposesArchiveSets()
    {
        // Reuse the lib fixture copied to the app test output, or point at the submodule path.
        string srrPath = TestPaths.Fixture("cleanup_script/007.A.View.To.A.Kill.1985.UE.iNTERNAL.DVDRip.XviD-iNCiTE.fine_2cd.srr");
        SRRFile srr = SRRFile.Load(srrPath);

        ImportedSrrInfo info = SrrImportParser.Parse(srr, srrPath);

        Assert.Equal(2, info.ArchiveSets.Count);
    }
}
```

(Add a small `TestPaths.Fixture(string rel)` helper resolving to the lib test data, or copy the fixture into `ReScene.NET.Tests/TestData/`. If the app test project already has a fixture-path convention, use it; otherwise resolve relative to the submodule: `Path.Combine(AppContext.BaseDirectory, "..","..","..","..","ReScene.Lib","ReScene.Tests","TestData", rel)` and assert the file exists in the test.)

- [ ] **Step 2: Run to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~SrrImportParserArchiveSetTests" -p:BaseOutputPath=bin2/
```
Expected: build error — `ImportedSrrInfo.ArchiveSets` does not exist.

- [ ] **Step 3: Add `ArchiveSets` to `ImportedSrrInfo` + populate in the parser**

In `ImportedSrrInfo.cs`, add:

```csharp
    public IReadOnlyList<SrrArchiveSet> ArchiveSets { get; init; } = [];
```

In `SrrImportParser.cs` `Parse`, add to the returned object initializer:

```csharp
            ArchiveSets = srr.ArchiveSets,
```

- [ ] **Step 4: Add `ArchiveSets` to `ReconstructionImportState` + copy on import**

In `ReconstructionImportState.cs`, add a property and reset it in `Clear`:

```csharp
    public IReadOnlyList<SrrArchiveSet> ArchiveSets { get; set; } = [];
```
```csharp
        ArchiveSets = [];   // in Clear()
```

In `ReconstructorViewModel.cs`, on the fresh-import path where `info` is applied to `_import` (near line 647+, wherever the other `info.*` fields are copied onto `_import`), add:

```csharp
        _import.ArchiveSets = info.ArchiveSets;
```

(The config-restore path via `ImportedSrrStateMapper.Apply` leaves `ArchiveSets` empty; Task 8's `ResolveArchiveSets()` re-derives it from the SRR path or synthesizes a single flat set.)

- [ ] **Step 5: Run (GREEN) + clean build + full app suite**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~SrrImportParserArchiveSetTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.NET/ReScene.NET.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: pass; **0 Warning(s) 0 Error(s)**; full suite green.

- [ ] **Step 6: Commit (app)**

```bash
cd E:/Projects/ReScene.NET
git add ReScene.NET/ViewModels/Reconstruction/ImportedSrrInfo.cs ReScene.NET/ViewModels/Reconstruction/SrrImportParser.cs ReScene.NET/ViewModels/Reconstruction/ReconstructionImportState.cs ReScene.NET/ViewModels/ReconstructorViewModel.cs ReScene.NET.Tests/SrrImportParserArchiveSetTests.cs
git commit -m "$(cat <<'EOF'
feat(reconstructor): carry per-archive-set list through SRR import

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Per-set option builder + view-model loop (app)

**Files:**
- Create: `ReScene.NET/ViewModels/Reconstruction/ArchiveSetPlanner.cs` (pure: resolve sets; build per-set `BruteForceOptions`; assemble `ExpectedVolumeCrcs`)
- Modify: `ReScene.NET/ViewModels/ReconstructorViewModel.cs` (`StartAsync` loop, seeding, relocation, partial-failure/cancellation reporting)
- Test: `ReScene.NET.Tests/ArchiveSetPlannerTests.cs`

**Interfaces:**
- Consumes: `_import.ArchiveSets`, `SRRFile.Load`, `SRRFile.ReadStoredFile`, `SFVFile.ParseBytes`/`ReadFile`, `RARVolumeIdentifier.GetArchiveSetKey`, `WinningCombo`, `BruteForceRunResult`.
- Produces: `ArchiveSetPlanner.ResolveSets(...)`, `ArchiveSetPlanner.BuildExpectedVolumeCrcs(...)`, `ArchiveSetPlanner.BuildOptionsForSet(...)`, `ArchiveSetPlanner.NarrowToCombo(...)`.

> This task is large but cohesive: the pure planner (unit-tested) plus the loop wiring it into `StartAsync` (driven through the existing `IBruteForceService`). The planner holds all the testable policy; the VM does orchestration + UI marshalling.

- [ ] **Step 1: Write the failing planner tests**

Create `ReScene.NET.Tests/ArchiveSetPlannerTests.cs`:

```csharp
using ReScene.Core;
using ReScene.NET.ViewModels.Reconstruction;
using ReScene.RAR;
using ReScene.SRR;

namespace ReScene.NET.Tests;

public class ArchiveSetPlannerTests
{
    private static SrrArchiveSet MakeSet(string key, string dir, string[] volumes, (string file, string crc)[] content)
    {
        var set = new SrrArchiveSet { Key = key, Directory = dir };
        set.VolumeNames.AddRange(volumes);
        foreach ((string file, string crc) in content)
        {
            set.ArchivedFiles.Add(file);
            set.ArchivedFileCrcs[file] = crc;
        }
        return set;
    }

    [Fact]
    public void BuildExpectedVolumeCrcs_FromUserSfv_FilteredToSetVolumes()
    {
        var set = MakeSet("DVD1/aln-re4a", "DVD1",
            ["DVD1\\aln-re4a.rar", "DVD1\\aln-re4a.r00"], [("aln-re4a.iso", "00000000")]);
        var userSfv = new SFVFile();
        userSfv.Entries.Add(new SFVFileEntry("aln-re4a.rar", "f1a3ec0d"));
        userSfv.Entries.Add(new SFVFileEntry("aln-re4a.r00", "88b361c9"));
        userSfv.Entries.Add(new SFVFileEntry("aln-re4b.rar", "631d681c")); // other set — excluded

        Dictionary<string, string> crcs = ArchiveSetPlanner.BuildExpectedVolumeCrcs(set, embeddedSfvBytes: null, userSfv);

        Assert.Equal(2, crcs.Count);
        Assert.Equal("f1a3ec0d", crcs["aln-re4a.rar"]);
        Assert.False(crcs.ContainsKey("aln-re4b.rar"));
    }

    [Fact]
    public void BuildOptionsForSet_UsesOnlyThisSetsContentAndNames()
    {
        var set = MakeSet("DVD1/aln-re4a", "DVD1",
            ["DVD1\\aln-re4a.rar", "DVD1\\aln-re4a.r00"], [("aln-re4a.iso", "00000000")]);
        var shared = ArchiveSetPlannerTestData.SharedSettings();   // helper building the non-per-set toggles

        BruteForceOptions opts = ArchiveSetPlanner.BuildOptionsForSet(set, shared, expectedVolumeCrcs:
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["aln-re4a.rar"] = "f1a3ec0d" });

        Assert.Contains("aln-re4a.iso", opts.RAROptions.ArchiveFilePaths);
        Assert.DoesNotContain("aln-re4b.iso", opts.RAROptions.ArchiveFilePaths);
        Assert.Equal(["DVD1\\aln-re4a.rar", "DVD1\\aln-re4a.r00"], opts.RAROptions.OriginalRarFileNames);
        Assert.True(opts.ExpectedVolumeCrcs.ContainsKey("aln-re4a.rar"));
    }

    [Fact]
    public void NarrowToCombo_RestrictsVersionsAndArgsToWinner()
    {
        var full = ArchiveSetPlannerTestData.SampleOptions();
        var combo = new WinningCombo(351, [new RARCommandLineArgument("-m0")]);

        BruteForceOptions narrowed = ArchiveSetPlanner.NarrowToCombo(full, combo);

        Assert.Single(narrowed.RAROptions.RARVersions);
        Assert.Equal(351, narrowed.RAROptions.RARVersions[0].Start);
        Assert.Equal(351, narrowed.RAROptions.RARVersions[0].End);
        Assert.Single(narrowed.RAROptions.CommandLineArguments);
    }

    [Fact]
    public void ResolveSets_NoArchiveSets_NoSrr_SynthesizesSingleFlatSet()
    {
        var sets = ArchiveSetPlanner.ResolveSets(archiveSets: [], srrFilePath: null,
            flatOriginalNames: ["x.rar", "x.r00"], flatArchiveFiles: ["x.iso"]);
        Assert.Single(sets);
        Assert.Equal("", sets[0].Directory);
        Assert.Equal(["x.rar", "x.r00"], sets[0].VolumeNames);
    }
}
```

(Provide the small `ArchiveSetPlannerTestData` helpers in the test file: `SharedSettings()` returns the planner's shared-settings record with default toggles and an empty/one-version range; `SampleOptions()` returns a `BruteForceOptions` with ≥2 version ranges and ≥2 command-line combos so `NarrowToCombo` is observably narrowing.)

- [ ] **Step 2: Run to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~ArchiveSetPlannerTests" -p:BaseOutputPath=bin2/
```
Expected: build error — `ArchiveSetPlanner` does not exist.

- [ ] **Step 3: Implement `ArchiveSetPlanner`**

Create `ReScene.NET/ViewModels/Reconstruction/ArchiveSetPlanner.cs`. It is pure (no I/O except the explicit byte inputs the caller supplies). Capture the VM's non-per-set toggles in a `SharedReconstructionSettings` record (switch toggles, delete/stop/complete flags, host-OS patching toggle, old-volume-naming, output path, winrar path, release path, hash type). The planner builds `BruteForceOptions` from a set + shared settings, mirroring today's `BuildRAROptions`/`BuildBruteForceOptions` but reading **per-set** data:

```csharp
using ReScene.Core;
using ReScene.Core.IO;
using ReScene.RAR;
using ReScene.SRR;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>The non-per-set reconstruction settings shared across all sets in a run.</summary>
internal sealed record SharedReconstructionSettings
{
    public required string WinRarPath { get; init; }
    public required string ReleasePath { get; init; }
    public required string OutputPath { get; init; }
    public required IReadOnlyList<VersionRange> RarVersions { get; init; }
    public required IReadOnlyList<RARCommandLineArgument[]> CommandLineArguments { get; init; }
    public required HashType HashType { get; init; }
    public TriState SetFileArchiveAttribute { get; init; }
    public TriState SetFileNotContentIndexedAttribute { get; init; }
    public bool DeleteRARFiles { get; init; }
    public bool DeleteDuplicateCRCFiles { get; init; }
    public bool StopOnFirstMatch { get; init; }
    public bool CompleteAllVolumes { get; init; }
    public bool RenameToReleaseNames { get; init; }
    public bool EnableHostOSPatching { get; init; }
    public bool UseOldVolumeNaming { get; init; }
}

internal static class ArchiveSetPlanner
{
    /// <summary>
    /// Resolves the archive sets to reconstruct. Prefers the parsed <paramref name="archiveSets"/>;
    /// else re-parses the SRR at <paramref name="srrFilePath"/>; else synthesizes one flat set from
    /// the flat names/files (legacy / no-SRR single-set path).
    /// </summary>
    public static IReadOnlyList<SrrArchiveSet> ResolveSets(
        IReadOnlyList<SrrArchiveSet> archiveSets,
        string? srrFilePath,
        IReadOnlyList<string> flatOriginalNames,
        IReadOnlyCollection<string> flatArchiveFiles)
    {
        if (archiveSets.Count > 0)
        {
            return archiveSets;
        }

        if (!string.IsNullOrWhiteSpace(srrFilePath) && File.Exists(srrFilePath))
        {
            IReadOnlyList<SrrArchiveSet> reloaded = SRRFile.Load(srrFilePath).ArchiveSets;
            if (reloaded.Count > 0)
            {
                return reloaded;
            }
        }

        var flat = new SrrArchiveSet { Key = "", Directory = "" };
        flat.VolumeNames.AddRange(flatOriginalNames);
        foreach (string f in flatArchiveFiles)
        {
            flat.ArchivedFiles.Add(f);
        }

        return [flat];
    }

    /// <summary>
    /// Builds the per-volume expected CRC map (base filename -> CRC) for a set: embedded SFV bytes
    /// first (when present), else the user verification SFV, filtered to this set's volume base names.
    /// </summary>
    public static Dictionary<string, string> BuildExpectedVolumeCrcs(
        SrrArchiveSet set, byte[]? embeddedSfvBytes, SFVFile? userSfv)
    {
        var wanted = new HashSet<string>(set.VolumeNames.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Take(SFVFile sfv)
        {
            foreach (SFVFileEntry e in sfv.Entries)
            {
                string name = Path.GetFileName(e.FileName);
                if (wanted.Contains(name) && !result.ContainsKey(name))
                {
                    result[name] = e.CRC;
                }
            }
        }

        if (embeddedSfvBytes is { Length: > 0 })
        {
            Take(SFVFile.ParseBytes(embeddedSfvBytes, tolerant: true));
        }

        if (result.Count < wanted.Count && userSfv != null)
        {
            Take(userSfv);
        }

        return result;
    }

    /// <summary>Builds the brute-force options for one set, using only its content/names/metadata.</summary>
    public static BruteForceOptions BuildOptionsForSet(
        SrrArchiveSet set,
        SharedReconstructionSettings shared,
        Dictionary<string, string> expectedVolumeCrcs)
    {
        var options = new BruteForceOptions(shared.WinRarPath, shared.ReleasePath, WorkRootFor(shared, set))
        {
            HashType = shared.HashType,
            RAROptions = new RAROptions
            {
                SetFileArchiveAttribute = shared.SetFileArchiveAttribute,
                SetFileNotContentIndexedAttribute = shared.SetFileNotContentIndexedAttribute,
                CommandLineArguments = shared.CommandLineArguments,
                RARVersions = shared.RarVersions,
                DeleteRARFiles = shared.DeleteRARFiles,
                DeleteDuplicateCRCFiles = shared.DeleteDuplicateCRCFiles,
                StopOnFirstMatch = shared.StopOnFirstMatch,
                CompleteAllVolumes = shared.CompleteAllVolumes,
                RenameToOriginalNames = shared.RenameToReleaseNames,
                OriginalRarFileNames = [.. set.VolumeNames],
                ArchiveFileCrcs = new Dictionary<string, string>(set.ArchivedFileCrcs, StringComparer.OrdinalIgnoreCase),
                ArchiveFilePaths = new HashSet<string>(set.ArchivedFiles, StringComparer.OrdinalIgnoreCase),
                FileTimestamps = new Dictionary<string, DateTime>(set.ArchivedFileTimestamps, StringComparer.OrdinalIgnoreCase),
                FileCreationTimes = new Dictionary<string, DateTime>(set.ArchivedFileCreationTimes, StringComparer.OrdinalIgnoreCase),
                FileAccessTimes = new Dictionary<string, DateTime>(set.ArchivedFileAccessTimes, StringComparer.OrdinalIgnoreCase),
                EnableHostOSPatching = shared.EnableHostOSPatching,
                DetectedFileHostOS = set.DetectedHostOS,
                DetectedFileAttributes = set.DetectedFileAttributes,
                DetectedLargeFlag = set.HasLargeFiles,
                DetectedHighPackSize = set.DetectedHighPackSize,
                DetectedHighUnpSize = set.DetectedHighUnpSize,
                UseOldVolumeNaming = shared.UseOldVolumeNaming,
            },
        };

        foreach (string crc in expectedVolumeCrcs.Values)
        {
            options.Hashes.Add(crc);
        }

        foreach (KeyValuePair<string, string> kv in expectedVolumeCrcs)
        {
            options.ExpectedVolumeCrcs[kv.Key] = kv.Value;
        }

        return options;
    }

    /// <summary>The working directory for a set's run: OutputPath for a single root set, else an isolated subdir.</summary>
    public static string WorkRootFor(SharedReconstructionSettings shared, SrrArchiveSet set) =>
        string.IsNullOrEmpty(set.Key)
            ? shared.OutputPath
            : Path.Combine(shared.OutputPath, ".rescene-work", Sanitize(set.Key));

    /// <summary>Narrows options to a single winning combo (one version, one args set) for seeding.</summary>
    public static BruteForceOptions NarrowToCombo(BruteForceOptions full, WinningCombo combo)
    {
        var narrowed = new BruteForceOptions(full.RARInstallationsDirectoryPath, full.ReleaseDirectoryPath, full.OutputDirectoryPath)
        {
            HashType = full.HashType,
            RAROptions = CloneWith(full.RAROptions,
                versions: [new VersionRange(combo.Version, combo.Version)],
                args: [combo.Args]),
        };
        foreach (string h in full.Hashes) { narrowed.Hashes.Add(h); }
        foreach (KeyValuePair<string, string> kv in full.ExpectedVolumeCrcs) { narrowed.ExpectedVolumeCrcs[kv.Key] = kv.Value; }
        return narrowed;
    }

    private static RAROptions CloneWith(RAROptions src, IReadOnlyList<VersionRange> versions, IReadOnlyList<RARCommandLineArgument[]> args) =>
        new()
        {
            SetFileArchiveAttribute = src.SetFileArchiveAttribute,
            SetFileNotContentIndexedAttribute = src.SetFileNotContentIndexedAttribute,
            CommandLineArguments = args,
            RARVersions = versions,
            DeleteRARFiles = src.DeleteRARFiles,
            DeleteDuplicateCRCFiles = src.DeleteDuplicateCRCFiles,
            StopOnFirstMatch = src.StopOnFirstMatch,
            CompleteAllVolumes = src.CompleteAllVolumes,
            RenameToOriginalNames = src.RenameToOriginalNames,
            OriginalRarFileNames = src.OriginalRarFileNames,
            ArchiveFileCrcs = new Dictionary<string, string>(src.ArchiveFileCrcs, StringComparer.OrdinalIgnoreCase),
            ArchiveFilePaths = new HashSet<string>(src.ArchiveFilePaths, StringComparer.OrdinalIgnoreCase),
            FileTimestamps = new Dictionary<string, DateTime>(src.FileTimestamps, StringComparer.OrdinalIgnoreCase),
            FileCreationTimes = new Dictionary<string, DateTime>(src.FileCreationTimes, StringComparer.OrdinalIgnoreCase),
            FileAccessTimes = new Dictionary<string, DateTime>(src.FileAccessTimes, StringComparer.OrdinalIgnoreCase),
            EnableHostOSPatching = src.EnableHostOSPatching,
            DetectedFileHostOS = src.DetectedFileHostOS,
            DetectedFileAttributes = src.DetectedFileAttributes,
            DetectedLargeFlag = src.DetectedLargeFlag,
            DetectedHighPackSize = src.DetectedHighPackSize,
            DetectedHighUnpSize = src.DetectedHighUnpSize,
            UseOldVolumeNaming = src.UseOldVolumeNaming,
        };

    private static string Sanitize(string key)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(c, '_');
        }

        return key.Replace('/', '_');
    }
}
```

(If `BuildOptionsForSet` needs comment/CMT data, copy it from the shared settings — comments are release-wide; carry the existing global comment fields on `SharedReconstructionSettings` and set them on `RAROptions`. Confirm against today's `BuildRAROptions` and include whichever fields it set that are not per-set: `ArchiveComment`, `ArchiveCommentBytes`, `CmtCompressedData`, `CmtCompressionMethod`, `DetectedCmtHostOS`, `DetectedCmtFileTime`, `DetectedCmtFileAttributes`, `CustomPackerDetected`, `SRRFilePath`. Add them to `SharedReconstructionSettings` and the two builders.)

- [ ] **Step 4: Wire the per-set loop into `StartAsync`**

In `ReScene.NET/ViewModels/ReconstructorViewModel.cs`, replace the single `BuildBruteForceOptions()` + `RunAsync` call with the loop. Pseudocode of the new tail (adapt to the real surrounding code, preserving validation/logging/timer lifecycle):

```csharp
        SharedReconstructionSettings shared = BuildSharedSettings();   // pull current toggles + version ranges + command lines
        IReadOnlyList<SrrArchiveSet> sets = ArchiveSetPlanner.ResolveSets(
            _import.ArchiveSets, _import.SRRFilePath, _import.OriginalRarFileNames, _import.ArchiveFiles);

        SFVFile? userSfv = TryLoadUserSfv(VerificationPath);          // existing .sfv read, or null for .sha1
        var setOutcomes = new List<(SrrArchiveSet Set, bool Ok, bool Skipped)>();
        WinningCombo? seed = null;

        for (int i = 0; i < sets.Count; i++)
        {
            SrrArchiveSet set = sets[i];
            Log(LogTarget.System, $"=== Set {i + 1}/{sets.Count}: {(string.IsNullOrEmpty(set.Key) ? "(release)" : set.Key)} ===");

            byte[]? embedded = LoadEmbeddedSfvBytes(set);             // see step 5
            Dictionary<string, string> expected = ArchiveSetPlanner.BuildExpectedVolumeCrcs(set, embedded, userSfv);
            if (shared.CompleteAllVolumes && expected.Count < set.VolumeNames.Count)
            {
                Log(LogTarget.System, $"Set {set.Key}: no per-volume CRCs to verify (supply its .sfv); skipping.");
                setOutcomes.Add((set, false, true));
                continue;
            }

            BruteForceOptions options = ArchiveSetPlanner.BuildOptionsForSet(set, shared, expected);

            BruteForceRunResult result;
            if (seed != null && sets.Count > 1)
            {
                result = await _bruteForceService.RunAsync(ArchiveSetPlanner.NarrowToCombo(options, seed), token);
                if (!result.Success && !token.IsCancellationRequested)
                {
                    Log(LogTarget.System, $"Seed combo did not reproduce {set.Key}; full search…");
                    result = await _bruteForceService.RunAsync(options, token);
                }
            }
            else
            {
                result = await _bruteForceService.RunAsync(options, token);
            }

            if (token.IsCancellationRequested)
            {
                CleanupWorkRoot(options.OutputDirectoryPath, set);    // remove in-flight set's scratch + partial output
                break;
            }

            if (result.Success)
            {
                seed ??= result.Combo;                                // first winner seeds the rest
                RelocateVerifiedOutput(options.OutputDirectoryPath, set, sets.Count);  // multi-set: move to OutputPath\output\<dir>\
            }

            setOutcomes.Add((set, result.Success, false));
        }

        ReportSetSummary(setOutcomes, sets.Count, token.IsCancellationRequested);
```

Helpers to add to the VM (concrete bodies):
- `BuildSharedSettings()` — reads the current toggles, `RarCommandLineBuilder.BuildVersionRanges/BuildCommandLineArguments(BuildSwitchSettings())`, `HashType` from the verification extension, and the global comment/CMT fields from `_import`.
- `TryLoadUserSfv(path)` — `Path.GetExtension(path)==".sfv"` → `SFVFile.ReadFile(path)`, else `null`.
- `LoadEmbeddedSfvBytes(set)` — when `_import.SRRFilePath` exists, `SRRFile.Load(path).ReadStoredFile(path, name => RARVolumeIdentifier.GetArchiveSetKey(name).Equals(set.Key, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase))`. (For a single flat set with empty key, match any `.sfv` stored file.) Return null if no SRR / no match.
- `RelocateVerifiedOutput(workRoot, set, setCount)` — when `setCount == 1` (single set, workRoot == OutputPath), **no-op** (Manager already wrote to `OutputPath\output\`). Otherwise move every file from `workRoot\output\` into `OutputPath\output\<set.Directory>\` (create dir; clean a pre-existing target subfolder first for deterministic re-runs), then delete the `.rescene-work\<key>` scratch.
- `CleanupWorkRoot(workRoot, set)` — for a multi-set workRoot, delete the `.rescene-work\<key>` directory and any partial `OutputPath\output\<set.Directory>\`.
- `ReportSetSummary(outcomes, count, cancelled)` — log a per-set summary (✓ / ✗ / Cancelled / Not-attempted), set the overall success message; success only when all non-skipped sets passed and none were skipped.

> Keep the existing single-set path intact: when `sets.Count == 1`, `WorkRootFor` returns `OutputPath`, no relocation occurs, and output remains at `OutputPath\output\<name>` — byte-identical to today.

- [ ] **Step 5: Run planner tests (GREEN) + clean build + full app suite**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~ArchiveSetPlannerTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.NET/ReScene.NET.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: planner tests pass; **0 Warning(s) 0 Error(s)**; full suite green.

- [ ] **Step 6: Commit (app)**

```bash
cd E:/Projects/ReScene.NET
git add ReScene.NET/ViewModels/Reconstruction/ArchiveSetPlanner.cs ReScene.NET/ViewModels/ReconstructorViewModel.cs ReScene.NET.Tests/ArchiveSetPlannerTests.cs
git commit -m "$(cat <<'EOF'
feat(reconstructor): reconstruct each archive set independently

Per-set option builder (own content/names/CRCs/metadata), per-set isolated work
dirs with subfolder-preserving relocation, seeded-with-fallback cross-set search,
honest skip when a set has no per-volume CRCs, and per-set pass/fail summary.
Single-set runs keep today's path byte-identical.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Per-set progress + multi-set notice (app/UI)

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/BruteForceProgressEventArgs.cs` (add optional set index/total/key) — **submodule** (commit + pointer bump as in Task 6 step 0)
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (populate the set fields if Manager is given them) **or** set them in the app from the loop index (preferred — keep Manager set-agnostic)
- Modify: `ReScene.NET/ViewModels/Reconstruction/ReconstructionProgressTracker.cs` (a `Set` label per row; reset on set boundary)
- Modify: `ReScene.NET/ViewModels/ReconstructorViewModel.cs` (`VersionEntry` gains `SetText`; emit set banner; bind `ArchiveSetStatus`)
- Modify: `ReScene.NET/Views/BruteForceProgressWindow.xaml` (add a `Set` column; final summary line)
- Modify: `ReScene.NET/Views/ReconstructorView.xaml` + `ReScene.NET/Views/Wizards/ReconstructWizardBody.xaml` (bind `ArchiveSetStatus` info line via `FieldStatusLine`)
- Test: `ReScene.NET.Tests/ReconstructorViewModelArchiveSetTests.cs`

**Interfaces:**
- Produces: `ReconstructorViewModel.ArchiveSetStatus` (`FieldStatus`); `VersionEntry.SetText`.

> Preferred approach: keep `Manager` set-agnostic. The VM owns the set index (the loop), so it sets the active set label on the tracker before each set's `RunAsync`, and the tracker stamps it on rows it creates. This avoids threading a set dimension through the lib event. If a `Set` value must appear on rows created deep in `ApplyProgress`, store the current set label on the tracker via a setter the VM calls per set.

- [ ] **Step 1: Write the failing test (info line + set label)**

Create `ReScene.NET.Tests/ReconstructorViewModelArchiveSetTests.cs`:

```csharp
using ReScene.NET.Models;

namespace ReScene.NET.Tests;

public class ReconstructorViewModelArchiveSetTests
{
    [Fact]
    public void ArchiveSetStatus_MultipleSets_ShowsInfo()
    {
        ReconstructorViewModel vm = ReconstructorViewModelTestFactory.Create();   // existing test factory/helpers
        ReconstructorViewModelTestFactory.ImportFixture(vm,
            "cleanup_script/007.A.View.To.A.Kill.1985.UE.iNTERNAL.DVDRip.XviD-iNCiTE.fine_2cd.srr");

        Assert.Equal(FieldState.Info, vm.ArchiveSetStatus.State);
        Assert.Contains("2 archive sets", vm.ArchiveSetStatus.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArchiveSetStatus_SingleSet_IsNone()
    {
        ReconstructorViewModel vm = ReconstructorViewModelTestFactory.Create();
        ReconstructorViewModelTestFactory.ImportFixture(vm, "store_little/store_little.srr");
        Assert.Equal(FieldState.None, vm.ArchiveSetStatus.State);
    }
}
```

(Use the app test project's existing way of constructing `ReconstructorViewModel` and importing an SRR — mirror `SrrImportParserArchiveSetTests`/existing VM tests. If there is no factory, build the VM with its test doubles as other VM tests do, call the import command/method with the fixture path, and assert.)

- [ ] **Step 2: Run to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~ReconstructorViewModelArchiveSetTests" -p:BaseOutputPath=bin2/
```
Expected: build error — `ArchiveSetStatus` does not exist.

- [ ] **Step 3: Add `ArchiveSetStatus` + set it on import**

In `ReconstructorViewModel.cs`, add an observable `FieldStatus` property (mirroring the other field-status props from the field-guidance work):

```csharp
    [ObservableProperty]
    public partial FieldStatus ArchiveSetStatus { get; set; } = FieldStatus.None;
```

Where the import applies sets to `_import` (Task 7), set the status:

```csharp
        ArchiveSetStatus = _import.ArchiveSets.Count > 1
            ? FieldStatus.Info($"This release has {_import.ArchiveSets.Count} archive sets " +
                $"({string.Join(", ", _import.ArchiveSets.Select(s => string.IsNullOrEmpty(s.Directory) ? s.Key : s.Directory))}); each is reconstructed independently.")
            : FieldStatus.None;
```

Reset it to `FieldStatus.None` wherever the import is cleared/reset.

- [ ] **Step 4: Add the `Set` column + summary + info-line bindings**

- `VersionEntry`: add `public string SetText { get; set; } = string.Empty;` (or an `[ObservableProperty]` if rows update live). The tracker stamps it from a current-set label the VM sets before each set's run (add `SetActiveSet(string label)` to the tracker; store and apply in `_createRow`).
- `BruteForceProgressWindow.xaml`: add a `DataGridTextColumn Header="Set" Binding="{Binding SetText}"` as the first column; add a summary `TextBlock` bound to a VM `SetSummaryText` shown when the run ends.
- `ReconstructorView.xaml` (advanced Paths tab) and `ReconstructWizardBody.xaml` (import step): add `<c:FieldStatusLine Status="{Binding ArchiveSetStatus}"/>` where other field-status lines appear.

- [ ] **Step 5: Run (GREEN) + clean build + full app suite**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~ReconstructorViewModelArchiveSetTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.NET/ReScene.NET.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: pass; **0 Warning(s) 0 Error(s)**; full suite green.

- [ ] **Step 6: Commit (submodule pointer if BruteForceProgressEventArgs changed, then app)**

If `BruteForceProgressEventArgs` was modified, commit it in the submodule and bump the pointer first; otherwise just:

```bash
cd E:/Projects/ReScene.NET
git add ReScene.NET/ViewModels/Reconstruction/ReconstructionProgressTracker.cs ReScene.NET/ViewModels/ReconstructorViewModel.cs ReScene.NET/Views/BruteForceProgressWindow.xaml ReScene.NET/Views/ReconstructorView.xaml ReScene.NET/Views/Wizards/ReconstructWizardBody.xaml ReScene.NET.Tests/ReconstructorViewModelArchiveSetTests.cs
git commit -m "$(cat <<'EOF'
feat(reconstructor): per-set progress, summary, and multi-set notice

Brute Force Progress shows a Set column and per-set outcome summary; the import
surfaces an info line when a release has multiple archive sets.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification (after all tasks)

- [ ] Clean non-incremental builds with `-p:BaseOutputPath=bin2/`: **0 warnings, 0 errors** for `ReScene.Lib/ReScene/ReScene.csproj` and `ReScene.NET/ReScene.NET/ReScene.NET.csproj`.
- [ ] Full suites green: `ReScene.Lib/ReScene.Tests` and `ReScene.NET.Tests` (both with `-p:BaseOutputPath=bin2/`).
- [ ] Submodule pointer in the app commit points at the new `ReScene.Lib` HEAD.
- [ ] Delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- [ ] **Manual:** reconstruct `D:\Resident_Evil_4_PAL_MULTI5_NGC-ALiEN.srr` against the two ISOs — both discs reconstruct, all 60 volume CRCs match, output under `output\DVD1\` and `output\DVD2\`; and reconstruct a known single-set release to confirm unchanged output at `output\`.

## Notes on cross-cutting concerns

- **Submodule-first ordering:** Tasks 1–5 land in `ReScene.Lib` (its own branch/commits); Task 6 step 0 bumps the app's submodule pointer. Never edit the lib from the app working copy without committing in the submodule.
- **Back-compat:** no flat `SRRFile` field is removed; `ArchiveSets` is additive; single-set resolves to one set on the unchanged single-run path; `SFVFile.ReadFile` keeps its strict contract.
- **Fix C realization:** the correctness half — each set patches with *its own* Host OS / attributes / LARGE / timestamps (Task 1 capture → Task 8 `BuildOptionsForSet`) — is implemented. The speed half is the cross-set **seeding** (Task 8). There is no separate per-set WinRAR-*version* pruning from headers: the SRR's `UnpackVersion` identifies only the RAR format family (already covered by the UI version toggles), not the exact WinRAR build the brute-force iterates, so per-set version search stays within the user's selected ranges and seeding handles same-settings discs. `IsSolid`/`HasRecoveryRecord` are captured per set for completeness but are not consumed to select switches (switch selection remains user-driven, exactly as today — not worsened by multi-set).
- **YAGNI:** no cross-set parallelism; custom-packer path untouched; per-set comments use the release-wide comment (multi-set releases with differing per-set comments are out of scope, consistent with the spec).
- **Testability:** the risky logic is isolated in pure, RAR-free units (`VolumeMatchEvaluator`, `ArchiveSetPlanner`, `Manager.BuildExpectedInOrder`, parser grouping). The `rar.exe` path is covered by those units + the manual checklist.
