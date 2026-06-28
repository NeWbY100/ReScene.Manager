# Show All RAR Header Flags (Set and Unset) (Design)

**Date:** 2026-06-28
**Status:** Draft (pending review)
**Branch:** `feature/show-all-rar-flags`
**Scope:** `ReScene.Lib` (`RARDetailedParser` in `RAR/RARDetailedHeader.cs`). No app changes.

## Background

In the Compare tab, comparing an original RAR with a reconstructed one reported the archive-header
`Flags` field as different (`0x0109` vs `0x0101`) but did not make it clear **which** flag differs.
The decoder lists a child row only for each flag bit that is **set**
(`RARDetailedParser.EmitFlags`, `RARDetailedHeader.cs:644`), so:

- `0x0109` → `VOLUME`, `SOLID`, `FIRST_VOLUME`
- `0x0101` → `VOLUME`, `FIRST_VOLUME`

The differing flag (`SOLID`, `0x0008`) is simply absent from the right side rather than shown as
"off." The Compare view matches child rows by name (`CompareNodePropertyBuilder` `childDiff =
otherChild.Value != child.Value`), so a flag set on one side and absent on the other never aligns and
is never highlighted — the user must eyeball the two lists to find the difference.

## Goal

Make the differing flag obvious by listing **every** flag for a decoded flags field — set *and*
unset — so the lists align on both sides and the Compare tab's existing per-child diff highlighting
marks the differing flag automatically. The full flag map also benefits the Inspector (the block
view is shared).

## Decision (agreed)

Show all flags everywhere (Inspector + Compare), implemented once in the lib's RAR header decoder.

## Architecture

The block/field model (`RARDetailedBlock`/`RARHeaderField`) and both consuming views
(`CompareNodePropertyBuilder`, the Inspector) are unchanged. Only the flag-child generation in
`RARDetailedParser` changes, so the new rows flow into both views and the Compare diff highlighting
works without any app change.

### `EmitFlags` — emit every flag, set or unset

`RARDetailedHeader.cs:644-655` currently appends a child only when `(flags & mask) != 0`, with the
flag's description as the value. Change it to append a child for **every** entry in the table:

- **set:** value = the flag's description (unchanged from today, e.g. `"Multi-volume archive"`).
- **unset:** value = `"Not set"`.

This covers the tables already routed through `EmitFlags`: RAR4 archive header
(`_rar4ArchiveFlags`), RAR4 file/service low and high (`_rar4FileFlagsLow`/`_rar4FileFlagsHigh`),
RAR4 end-of-archive (`_rar4EndFlags`), and the RAR5 generic header `HFL_*` flags
(`_rar5HeaderFlags`).

### `LONG_BLOCK` — emit always

The generic `LONG_BLOCK` bit (`0x8000`) is appended in `AddRAR4FlagDescriptions`
(`RARDetailedHeader.cs:660`) only when set. Emit it always: set → `"Has ADD_SIZE field"`, unset →
`"Not set"`. It stays first in the child list (before the type-specific table), preserving the
existing relative order.

### RAR5 inline flag blocks — route through `EmitFlags`

The RAR5 main-archive flags (`ParseRAR5MainHeader`, `:1174-1198`), file flags
(`ParseRAR5FileHeader`, `:1221-1239`), and end flags (`ParseRAR5EndHeader`, `:1396-1399`) are
currently inline `if ((flags & mask) != 0)` checks. Refactor each into a small flag table and route
it through `EmitFlags`, so they also emit all flags and stay DRY/consistent with RAR4. The flag
names/descriptions and their order are preserved verbatim from the current inline code:

- `_rar5MainArchiveFlags`: `VOLUME` "Multi-volume", `VOLNUMBER` "Volume number present", `SOLID`
  "Solid archive", `PROTECT` "Recovery record present", `LOCK` "Locked archive".
- `_rar5FileFlags`: `DIRECTORY` "Directory entry", `UTIME` "Unix time present", `CRC32` "CRC32
  present", `UNPSIZE` "Unpacked size unknown".
- `_rar5EndFlags`: `NEXTVOLUME` "Archive continues".

The subsequent reads gated on a flag bit (volume number after `VOLNUMBER`, mtime after `UTIME`, data
CRC after `CRC32`, etc.) keep testing the raw `flags` value and are unchanged — only the child-row
emission moves to the table.

### Out of scope (already convey full state, or not simple flag bits)

- `DICT_SIZE` — a 3-bit dictionary-size value, already emitted as one row; unchanged.
- `EXT_TIME` per-timestamp children — already show `"Present, …"` / `"Not present"`; unchanged.
- RAR5 Compression-Info `SOLID` bit — already `"Yes"`/`"No"`; unchanged.
- RAR5 extra-area Locator/Metadata flags (`QLIST`, `RR`, …) — rare and deeper; left as-is.

No public API change: `EmitFlags` and the flag tables are private; `RARHeaderField`/`RARDetailedBlock`
are untouched.

## Data Flow

`RARDetailedParser.Parse(...)` → `RARDetailedBlock.Fields[Flags].Children` now contains one row per
known flag (set/unset) → `CompareNodePropertyBuilder.ShowDetailedBlockProperties` renders the
children and marks `IsDifferent` when the matched child's value differs → the Compare grid highlights
the differing flag (e.g. `SOLID`: `"Solid archive"` vs `"Not set"`). The Inspector renders the same
children, showing the full flag map.

## Error Handling

None — display-only decoding; no new failure modes. The flag tables are exhaustive and fixed.

## Testing & Verification

- **Lib unit tests** (`RARDetailedParser`/header tests): for a known RAR4 archive-header flags value
  (e.g. `0x0109`), the `Flags` field's children include every `_rar4ArchiveFlags` entry in table
  order, with `SOLID` = `"Solid archive"` and a clear-bit flag (e.g. `LOCK`) = `"Not set"`; an
  equivalent assertion for a RAR5 main-archive flags value.
- **Update existing snapshot/detailed-header tests** to the new all-flags output (the parser's child
  lists grow; snapshots are regenerated to match).
- **Build:** clean non-incremental, **0 warnings / 0 errors**; full `ReScene.Lib` and `ReScene.NET`
  suites green.
- **Manual:** re-open the original-vs-reconstructed RAR in Compare; the archive-header `Flags` field
  lists all flags and `SOLID` is highlighted as the difference; the Inspector shows the full flag
  map.

## Non-Goals

- No change to `CompareNodePropertyBuilder`, the Inspector view, or any XAML — the highlighting and
  rendering already handle whatever children the lib emits.
- No change to flag names, descriptions, or their order for set flags (byte-identical set rows;
  only unset rows are added).

## Delivery

Lib-only change → the app's submodule pointer is bumped to the new `ReScene.Lib` commit. Ships as a
backward-compatible diagnostics improvement (target **v1.7.1**); whether the library is released
alongside the app (lib csproj bumped to match the tag) is confirmed at release time.

## File Structure

- `ReScene.Lib/ReScene/RAR/RARDetailedHeader.cs` — `EmitFlags` emits all flags; `LONG_BLOCK` always
  emitted; RAR5 main/file/end flag blocks refactored to tables + `EmitFlags`.
- `ReScene.Lib/ReScene.Tests/…` — new all-flags assertions; updated detailed-header snapshots.
