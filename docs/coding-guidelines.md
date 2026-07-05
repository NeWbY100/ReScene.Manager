# Coding Guidelines

Conventions for ReScene.NET (WPF app) and its `ReScene.Lib` submodule. Formatting and
most style rules are enforced by [`.editorconfig`](../.editorconfig) — run `dotnet format`
or let the IDE apply it. This document covers the conventions that the analyzer does **not**
enforce, and reinforces the few style preferences that matter most here.

## File organization

**One top-level type per file.** Each top-level `class`, `record`, `struct`, `enum`, and
`interface` lives in its own file, and the file is named after the type (`RARHeaderField`
→ `RARHeaderField.cs`). This keeps files focused, makes types easy to find, and keeps diffs
scoped to a single type.

- **Nested types stay with their parent.** A type declared *inside* another type (e.g. a
  private helper class inside a ViewModel, or a nested DTO) belongs in the parent's file —
  it is part of that type, not a separate top-level type.
- Applies to production and test code alike. A file that collected several small test
  doubles or fakes should be split so each double has its own file.
- `partial` types may of course span multiple files; that is the one intentional exception,
  and each part still declares only that one type.

## Naming

**Spell the `SRR`, `SRS`, and `RAR` acronyms in all-caps** wherever they appear in an
identifier — `SRRFile`, `RARCommandLineBuilder`, `ISRRVerifyService`, `OriginalRARFileNames`
— to match the `SRR`/`SRS`/`RAR` namespaces and the core `SRRFile`/`RARArchive` types. Do
**not** use mixed case (`Srr`, `Rar`): the two casings do not encode any file-vs-format
distinction, so keeping them consistent avoids confusion. The one exception is a leading
acronym in a camelCase local, which stays lowercase by normal convention (`srrPath`,
`rarVersion`). This is not analyzer-enforced.

> .NET's Framework Design Guidelines technically prefer PascalCase for 3-letter acronyms
> (`Srr`), but this codebase standardized on all-caps to match its namespaces.

## Language style

These are `suggestion`-level in `.editorconfig`; follow them in new and touched code.

**Collection expressions** over factory/LINQ-materialization calls:

```csharp
// prefer
byte[] empty = [];
List<string> names = [.. items.Select(i => i.Name)];
string[] copy = [.. buffer];

// over
byte[] empty = Array.Empty<byte>();
List<string> names = items.Select(i => i.Name).ToList();
string[] copy = buffer.ToArray();
```

**Expression-bodied members** when the body is a single expression on one line:

```csharp
// prefer
public void StopRun() => _stopwatch.Stop();
public string? GetNextVolumePath(string p, bool old) => old ? OldStyle(p) : NewStyle(p);

// over
public void StopRun()
{
    _stopwatch.Stop();
}
```

Keep a block body when the logic spans multiple statements or the single line would be too
long to read comfortably.

## Enforced by `.editorconfig` (for reference)

- **File-scoped namespaces** (`namespace ReScene.RAR;`), `using` directives outside the namespace.
- **Explicit accessibility modifiers** always.
- **`var` only when the type is apparent** from the right-hand side; otherwise use the explicit type.
- Braces required on control-flow statements; Allman brace placement.
- Pattern matching over `is`/`as`-with-cast; null-propagation and coalescing where clearer.

When in doubt, match the surrounding code and let `.editorconfig` settle formatting.
