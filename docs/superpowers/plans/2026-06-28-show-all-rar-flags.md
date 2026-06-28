# Show All RAR Header Flags Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decode every RAR header flag bit (set and unset) into a child row so the Compare tab aligns and highlights the differing flag, and the Inspector shows the full flag map.

**Architecture:** One change in the lib's `RARDetailedParser` (`RAR/RARDetailedHeader.cs`): `EmitFlags` emits a child for every flag in its table (value = description when set, `"Not set"` when clear); `LONG_BLOCK` is emitted always; the RAR5 main-archive/file/end flag blocks are refactored to small flag tables routed through `EmitFlags`. No app/XAML change — the Compare/Inspector views render whatever children the parser produces, and the Compare per-child diff highlighting works as-is.

**Tech Stack:** .NET (`ReScene.Lib` = `net8.0;net10.0`), xUnit.

**Spec:** `docs/superpowers/specs/2026-06-28-show-all-rar-flags-design.md`

## Global Constraints

- **Submodule.** All changes are in `ReScene.Lib` (working dir `E:\Projects\ReScene.NET\ReScene.Lib`, branch `feature/show-all-rar-flags`). Commit there.
- **Build/test only with `-p:BaseOutputPath=bin2/`** (the running app locks `bin/`). NEVER kill any process.
- **Verify non-incrementally:** `dotnet build … --no-incremental` → **0 warnings, 0 errors** (strict analyzers).
- After verifying, delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- **No public API change:** `EmitFlags` and the flag tables are private; `RARHeaderField`/`RARDetailedBlock` are untouched.
- **Set rows are byte-identical to today** (same name + same description); only unset rows are added, plus `LONG_BLOCK` now always shown.
- **End the commit message** with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## Task 1: Emit every flag (set and unset) in the RAR header decoder

**Files:**
- Modify: `ReScene.Lib/ReScene/RAR/RARDetailedHeader.cs` (`EmitFlags`; `AddRAR4FlagDescriptions` `LONG_BLOCK`; `ParseRAR5MainHeader`/`ParseRAR5FileHeader`/`ParseRAR5EndHeader` flag emission + three new RAR5 flag tables)
- Test: `ReScene.Lib/ReScene.Tests/RARDetailedParserTests.cs`

**Interfaces:**
- Produces (private): three RAR5 flag tables `_rar5MainArchiveFlags`, `_rar5FileFlags`, `_rar5EndFlags`; `EmitFlags` now emits all table entries.

- [ ] **Step 1: Write the failing tests**

Add a parameterized archive-header builder and three tests to `ReScene.Lib/ReScene.Tests/RARDetailedParserTests.cs`. Put the builder in the "Synthetic RAR4 Helpers" region (next to `BuildArchiveHeader`):

```csharp
    /// <summary>
    /// Builds a RAR4 archive header (type 0x73) with the given HEAD_FLAGS and a valid CRC.
    /// </summary>
    private static byte[] BuildArchiveHeaderWithFlags(ushort flags)
    {
        byte[] header = new byte[13];
        header[2] = 0x73; // Archive header type
        BitConverter.GetBytes(flags).CopyTo(header, 3); // Flags
        BitConverter.GetBytes((ushort)13).CopyTo(header, 5); // Header size

        uint crc32 = Crc32Algorithm.Compute(header, 2, header.Length - 2);
        BitConverter.GetBytes((ushort)(crc32 & 0xFFFF)).CopyTo(header, 0);
        return header;
    }
```

Add a new region with the tests:

```csharp
    #region All-Flags (set + unset) Tests

    [Fact]
    public void Parse_RAR4ArchiveHeader_FlagsField_ListsEveryFlag_SetAndUnset()
    {
        // 0x0109 = VOLUME (0x0001) | SOLID (0x0008) | FIRST_VOLUME (0x0100)
        byte[] archiveHeader = BuildArchiveHeaderWithFlags(0x0109);
        byte[] endBlock = BuildEndBlock();

        using MemoryStream stream = BuildRAR4Stream(archiveHeader, endBlock);
        IReadOnlyList<RARDetailedBlock> blocks = RARDetailedParser.Parse(stream);

        RARDetailedBlock arc = blocks.First(b => b.BlockType == "Archive Header");
        RARHeaderField flags = arc.Fields.First(f => f.Name == "Flags");

        // Every archive flag is present, regardless of whether its bit is set.
        foreach (string name in new[]
                 { "VOLUME", "COMMENT", "LOCK", "SOLID", "NEW_NUMBERING", "AV", "PROTECT", "PASSWORD", "FIRST_VOLUME" })
        {
            Assert.Contains(flags.Children, c => c.Name == name);
        }

        // Set bits keep their description; clear bits read "Not set".
        Assert.Equal("Multi-volume archive", flags.Children.First(c => c.Name == "VOLUME").Value);
        Assert.Equal("Solid archive", flags.Children.First(c => c.Name == "SOLID").Value);
        Assert.Equal("First volume", flags.Children.First(c => c.Name == "FIRST_VOLUME").Value);
        Assert.Equal("Not set", flags.Children.First(c => c.Name == "COMMENT").Value);
        Assert.Equal("Not set", flags.Children.First(c => c.Name == "LOCK").Value);
        // LONG_BLOCK (0x8000) is not set here, but is now always listed.
        Assert.Equal("Not set", flags.Children.First(c => c.Name == "LONG_BLOCK").Value);
    }

    [Fact]
    public void Parse_RAR4FileHeader_FlagsField_ListsUnsetFlagsAsNotSet()
    {
        // BuildFileHeaderNoLarge sets only LONG_BLOCK (0x8000); the file flags are otherwise clear.
        byte[] archiveHeader = BuildArchiveHeader();
        byte[] fileHeader = BuildFileHeaderNoLarge("plain.txt", 100);
        byte[] endBlock = BuildEndBlock();

        using MemoryStream stream = BuildRAR4Stream(archiveHeader, fileHeader, endBlock);
        IReadOnlyList<RARDetailedBlock> blocks = RARDetailedParser.Parse(stream);

        RARHeaderField flags = blocks.First(b => b.BlockType == "File Header").Fields.First(f => f.Name == "Flags");

        Assert.Equal("Has ADD_SIZE field", flags.Children.First(c => c.Name == "LONG_BLOCK").Value);
        Assert.Equal("Not set", flags.Children.First(c => c.Name == "SPLIT_BEFORE").Value);
        Assert.Equal("Not set", flags.Children.First(c => c.Name == "LARGE").Value);
        // DICT_SIZE remains a single value row (a 3-bit value, not a flag).
        Assert.Contains(flags.Children, c => c.Name == "DICT_SIZE");
    }

    [Fact]
    public void Parse_RAR5MainHeader_FlagsField_ListsEveryFlag_SetAndUnset()
    {
        // Main archive flags 0x0005 = VOLUME (0x0001) | SOLID (0x0004); VOLNUMBER not set (so no extra read).
        byte[] mainBlock = BuildRAR5Block(1, 0, EncodeVInt(0x0005));
        byte[] endBlock = BuildRAR5Block(5, 0, EncodeVInt(0));

        var ms = new MemoryStream();
        ms.Write(RAR5Signature);
        ms.Write(mainBlock);
        ms.Write(endBlock);
        ms.Position = 0;

        IReadOnlyList<RARDetailedBlock> blocks = RARDetailedParser.Parse(ms);

        RARHeaderField flags = blocks.First(b => b.BlockType == "Main Archive Header").Fields.First(f => f.Name == "Archive Flags");

        foreach (string name in new[] { "VOLUME", "VOLNUMBER", "SOLID", "PROTECT", "LOCK" })
        {
            Assert.Contains(flags.Children, c => c.Name == name);
        }

        Assert.Equal("Multi-volume", flags.Children.First(c => c.Name == "VOLUME").Value);
        Assert.Equal("Solid archive", flags.Children.First(c => c.Name == "SOLID").Value);
        Assert.Equal("Not set", flags.Children.First(c => c.Name == "VOLNUMBER").Value);
        Assert.Equal("Not set", flags.Children.First(c => c.Name == "LOCK").Value);
    }

    #endregion
```

- [ ] **Step 2: Run the tests to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~RARDetailedParserTests" -p:BaseOutputPath=bin2/
```
Expected: the three new tests FAIL — unset flags (`COMMENT`/`LOCK`/`SPLIT_BEFORE`/`LARGE`/`VOLNUMBER`) are absent and `LONG_BLOCK` is absent when clear (current code emits only set flags). The pre-existing tests still pass.

- [ ] **Step 3: Make `EmitFlags` emit every flag**

In `ReScene.Lib/ReScene/RAR/RARDetailedHeader.cs`, replace `EmitFlags` (currently around lines 644-655):

```csharp
    private static void EmitFlags(
        RARHeaderField flagsField, ushort flags,
        (ushort Mask, string Name, string Description)[] table)
    {
        foreach ((ushort mask, string name, string description) in table)
        {
            if ((flags & mask) != 0)
            {
                flagsField.Children.Add(new RARHeaderField { Name = name, Value = description });
            }
        }
    }
```

with:

```csharp
    private static void EmitFlags(
        RARHeaderField flagsField, ushort flags,
        (ushort Mask, string Name, string Description)[] table)
    {
        foreach ((ushort mask, string name, string description) in table)
        {
            flagsField.Children.Add(new RARHeaderField
            {
                Name = name,
                Value = (flags & mask) != 0 ? description : "Not set"
            });
        }
    }
```

- [ ] **Step 4: Make `LONG_BLOCK` always emit**

In `AddRAR4FlagDescriptions` (around lines 657-663), replace:

```csharp
        // Common flags
        if ((flags & 0x8000) != 0)
        {
            flagsField.Children.Add(new RARHeaderField { Name = "LONG_BLOCK", Value = "Has ADD_SIZE field" });
        }
```

with:

```csharp
        // Common flags (LONG_BLOCK is shown for every block, set or not)
        flagsField.Children.Add(new RARHeaderField
        {
            Name = "LONG_BLOCK",
            Value = (flags & 0x8000) != 0 ? "Has ADD_SIZE field" : "Not set"
        });
```

- [ ] **Step 5: Add the three RAR5 flag tables**

Add these next to the existing RAR4 flag tables (after `_rar5HeaderFlags`, around line 642). Names/descriptions/order copied verbatim from the current inline RAR5 code:

```csharp
    private static readonly (ushort Mask, string Name, string Description)[] _rar5MainArchiveFlags =
    [
        (0x0001, "VOLUME", "Multi-volume"),
        (0x0002, "VOLNUMBER", "Volume number present"),
        (0x0004, "SOLID", "Solid archive"),
        (0x0008, "PROTECT", "Recovery record present"),
        (0x0010, "LOCK", "Locked archive"),
    ];

    private static readonly (ushort Mask, string Name, string Description)[] _rar5FileFlags =
    [
        (0x0001, "DIRECTORY", "Directory entry"),
        (0x0002, "UTIME", "Unix time present"),
        (0x0004, "CRC32", "CRC32 present"),
        (0x0008, "UNPSIZE", "Unpacked size unknown"),
    ];

    private static readonly (ushort Mask, string Name, string Description)[] _rar5EndFlags =
    [
        (0x0001, "NEXTVOLUME", "Archive continues"),
    ];
```

- [ ] **Step 6: Route the RAR5 main-archive flags through `EmitFlags`**

In `ParseRAR5MainHeader` (around lines 1171-1199), replace the inline block:

```csharp
            ulong archFlags = cursor.EmitVInt("Archive Flags", out RARHeaderField archFlagsField);
            archFlagsField.Value = FormatHex(archFlags, archFlagsField.Length);

            if ((archFlags & 0x0001) != 0)
            {
                archFlagsField.Children.Add(new RARHeaderField { Name = "VOLUME", Value = "Multi-volume" });
            }

            if ((archFlags & 0x0002) != 0)
            {
                archFlagsField.Children.Add(new RARHeaderField { Name = "VOLNUMBER", Value = "Volume number present" });
            }

            if ((archFlags & 0x0004) != 0)
            {
                archFlagsField.Children.Add(new RARHeaderField { Name = "SOLID", Value = "Solid archive" });
            }

            if ((archFlags & 0x0008) != 0)
            {
                archFlagsField.Children.Add(new RARHeaderField { Name = "PROTECT", Value = "Recovery record present" });
            }

            if ((archFlags & 0x0010) != 0)
            {
                archFlagsField.Children.Add(new RARHeaderField { Name = "LOCK", Value = "Locked archive" });
            }

            block.Fields.Add(archFlagsField);
```

with:

```csharp
            ulong archFlags = cursor.EmitVInt("Archive Flags", out RARHeaderField archFlagsField);
            archFlagsField.Value = FormatHex(archFlags, archFlagsField.Length);
            EmitFlags(archFlagsField, (ushort)archFlags, _rar5MainArchiveFlags);
            block.Fields.Add(archFlagsField);
```

The `if ((archFlags & 0x0002) != 0)` volume-number read that follows (`block.Fields.Add(archFlagsField)` then the `// Volume number (vint)` block) stays exactly as-is — it tests the raw `archFlags`, not the children.

- [ ] **Step 7: Route the RAR5 file flags through `EmitFlags`**

In `ParseRAR5FileHeader` (around lines 1218-1241), replace the inline block:

```csharp
            ulong fileFlags = cursor.EmitVInt("File Flags", out RARHeaderField fileFlagsField);
            fileFlagsField.Value = FormatHex(fileFlags, fileFlagsField.Length);

            if ((fileFlags & 0x0001) != 0)
            {
                fileFlagsField.Children.Add(new RARHeaderField { Name = "DIRECTORY", Value = "Directory entry" });
            }

            if ((fileFlags & 0x0002) != 0)
            {
                fileFlagsField.Children.Add(new RARHeaderField { Name = "UTIME", Value = "Unix time present" });
            }

            if ((fileFlags & 0x0004) != 0)
            {
                fileFlagsField.Children.Add(new RARHeaderField { Name = "CRC32", Value = "CRC32 present" });
            }

            if ((fileFlags & 0x0008) != 0)
            {
                fileFlagsField.Children.Add(new RARHeaderField { Name = "UNPSIZE", Value = "Unpacked size unknown" });
            }

            block.Fields.Add(fileFlagsField);
```

with:

```csharp
            ulong fileFlags = cursor.EmitVInt("File Flags", out RARHeaderField fileFlagsField);
            fileFlagsField.Value = FormatHex(fileFlags, fileFlagsField.Length);
            EmitFlags(fileFlagsField, (ushort)fileFlags, _rar5FileFlags);
            block.Fields.Add(fileFlagsField);
```

The later reads gated on `fileFlags` (unpacked size, attributes, the `(fileFlags & 0x0002)` mtime, the `(fileFlags & 0x0004)` CRC32, compression info, etc.) stay exactly as-is.

- [ ] **Step 8: Route the RAR5 end flags through `EmitFlags`**

In `ParseRAR5EndHeader` (around lines 1393-1401), replace the inline block:

```csharp
            ulong endFlags = cursor.EmitVInt("End Flags", out RARHeaderField endFlagsField);
            endFlagsField.Value = FormatHex(endFlags, endFlagsField.Length);

            if ((endFlags & 0x0001) != 0)
            {
                endFlagsField.Children.Add(new RARHeaderField { Name = "NEXTVOLUME", Value = "Archive continues" });
            }

            block.Fields.Add(endFlagsField);
```

with:

```csharp
            ulong endFlags = cursor.EmitVInt("End Flags", out RARHeaderField endFlagsField);
            endFlagsField.Value = FormatHex(endFlags, endFlagsField.Length);
            EmitFlags(endFlagsField, (ushort)endFlags, _rar5EndFlags);
            block.Fields.Add(endFlagsField);
```

- [ ] **Step 9: Run the new tests (GREEN) + full suite + clean build**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj \
  --filter "FullyQualifiedName~RARDetailedParserTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.Lib/ReScene/ReScene.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: the three new tests pass; **0 Warning(s) 0 Error(s)**; full lib suite green. (Existing flag tests — `Parse_LargeFlagFlagsFieldHasLargeChild`, `Parse_RAR4EndBlock_FlagsShowDataCRCChild`, `Parse_RAR5FileHeader_HasCompressionInfoField`, etc. — assert presence of *set* flags / specific children, so they remain green.)

If any existing test fails because it asserted the absence of a flag child or an exact child count, update it to the new all-flags reality (the spec's intent). Do not weaken assertions on *set* rows.

- [ ] **Step 10: Commit (submodule)**

```bash
cd E:/Projects/ReScene.NET/ReScene.Lib
git add ReScene/RAR/RARDetailedHeader.cs ReScene.Tests/RARDetailedParserTests.cs
git commit -m "$(cat <<'EOF'
feat(rar): decode every header flag, set and unset

Flag fields now list every flag in their table (value = description when set,
"Not set" when clear), LONG_BLOCK is always shown, and the RAR5
main-archive/file/end flag blocks are routed through the same EmitFlags helper via
flag tables. This makes the Compare tab align and highlight the differing flag
and shows the full flag map in the Inspector. No public API change.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification (after the task)

- [ ] Clean non-incremental build of `ReScene.Lib/ReScene/ReScene.csproj` with `-p:BaseOutputPath=bin2/`: **0 warnings, 0 errors**.
- [ ] Full `ReScene.Lib` suite green; full `ReScene.NET` app suite green (no app change, but confirm the new lib doesn't perturb it after the pointer bump at release time).
- [ ] Delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- [ ] **Manual:** open the original-vs-reconstructed RAR in Compare — the archive-header `Flags` field lists all flags and `SOLID` is highlighted as the difference (`"Solid archive"` vs `"Not set"`); the Inspector shows the full flag map.

## Notes on cross-cutting concerns

- **No app change:** `CompareNodePropertyBuilder` already diffs child rows by name and `IsDifferent`; with both sides now listing `SOLID`, the differing value highlights automatically.
- **DRY:** the RAR5 flag blocks now share the single `EmitFlags` path with RAR4; `DICT_SIZE` (a 3-bit value), `EXT_TIME` (already present/not-present), and the RAR5 Compression-Info `SOLID` bit (already Yes/No) are intentionally left as-is.
- **Delivery:** lib-only change → bump the app's submodule pointer at release time; ships as **v1.7.1** (version/lib-release scope confirmed then).
