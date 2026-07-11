# MapDocument Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `wc3ctl` CLI that opens a `.w3x`/`.w3m`, parses known `war3map.*` files into a typed queryable `MapDocument`, preserves unknown files raw, and re-saves byte-faithfully per file.

**Architecture:** Layered — `Wc3.MapDocument` (model + load/save) ← `Wc3.Commands` (shared handlers returning plain result objects) ← `wc3ctl` (CLI renderer). `Wc3.Mcp` is a seam-only stub referencing `Wc3.Commands`. War3Net does the MPQ + binary format heavy lifting.

**Tech Stack:** C# / .NET 8 (LTS), War3Net.Build + War3Net.IO.Mpq, System.CommandLine, xUnit.

## Global Constraints

- Target framework `net8.0`; `<Nullable>enable</Nullable>`; `<LangVersion>latest</LangVersion>` in every project.
- External dependencies limited to: `War3Net.Build`, `War3Net.IO.Mpq`, `System.CommandLine` (prerelease `2.0.0-beta4.*`), `xunit` + `xunit.runner.visualstudio`. No others without updating this plan.
- Root namespace prefix `Wc3` for all projects.
- **Fidelity rule:** for any file not marked dirty, the bytes written back MUST equal the original *decompressed* bytes. Enforced by the round-trip test.
- **Principle #5 (sacred):** never drop and never throw on an unknown, unnamed, or unparseable file. Unknown → preserved raw. Parse failure → keep raw bytes, record a `Diagnostic`, continue.
- **War3Net API note:** per-file parsers are `BinaryReader` extension methods in `War3Net.Build.Core` (e.g. `reader.ReadMapInfo()`); serializers are `BinaryWriter` extensions (e.g. `writer.Write(mapInfo)`). Exact method names must be confirmed against the referenced package via IntelliSense/decompiler; a task's characterization test is the enforcement mechanism.
- TDD: write the failing test first, watch it fail, minimal implementation, watch it pass, commit. Commit after every task.

---

## File Structure

```
Wc3.sln
src/
  Wc3.MapDocument/
    Wc3.MapDocument.csproj
    Diagnostic.cs            — Diagnostic record + severity enum
    MapFileEntry.cs          — one internal file: raw bytes + optional typed model + dirty flag
    MpqHeader.cs             — pre-archive header detection helper
    MapFormatRegistry.cs     — filename -> parse delegate registry + graceful invoke
    MapDocument.cs           — Load / Save / query
  Wc3.Commands/
    Wc3.Commands.csproj
    Results.cs               — result POCOs (Info/List/ObjectGet/Search/Diff/Roundtrip)
    InfoCommand.cs
    ListCommand.cs
    ObjectGetCommand.cs
    SearchCommand.cs
    DiffCommand.cs
    RoundtripCommand.cs
  wc3ctl/
    wc3ctl.csproj
    Program.cs               — System.CommandLine wiring + --json
    Render.cs                — human-readable renderers
  Wc3.Mcp/
    Wc3.Mcp.csproj           — seam-only: references Wc3.Commands; README stub
    README.md
tests/
  Wc3.Tests/
    Wc3.Tests.csproj
    SyntheticMap.cs          — builds a small in-memory .w3x-like fixture
    RoundtripTests.cs
    ParseCoverageTests.cs    — [Trait("Category","Corpus")] against the real map
    CommandTests.cs
```

---

## Task 1: Solution scaffold + smoke test

**Files:**
- Create: `Wc3.sln`, `src/Wc3.MapDocument/Wc3.MapDocument.csproj`, `tests/Wc3.Tests/Wc3.Tests.csproj`
- Test: `tests/Wc3.Tests/SmokeTest.cs`

**Interfaces:**
- Produces: a buildable solution with `Wc3.MapDocument` referenced by `Wc3.Tests`.

- [ ] **Step 1: Verify .NET 8 SDK is installed**

Run: `dotnet --version`
Expected: prints `8.x.x`. If "command not found", install the .NET 8 SDK first (winget: `winget install Microsoft.DotNet.SDK.8`) and re-open the shell.

- [ ] **Step 2: Create solution and projects**

```bash
cd "D:/playground/Programming/Wc3_CLI"
dotnet new sln -n Wc3
dotnet new classlib -n Wc3.MapDocument -o src/Wc3.MapDocument -f net8.0
dotnet new xunit -n Wc3.Tests -o tests/Wc3.Tests -f net8.0
rm src/Wc3.MapDocument/Class1.cs
dotnet sln add src/Wc3.MapDocument/Wc3.MapDocument.csproj tests/Wc3.Tests/Wc3.Tests.csproj
dotnet add tests/Wc3.Tests reference src/Wc3.MapDocument
dotnet add src/Wc3.MapDocument package War3Net.Build
dotnet add src/Wc3.MapDocument package War3Net.IO.Mpq
```

- [ ] **Step 3: Enable nullable + langversion in both csproj**

In each `.csproj`, ensure the `<PropertyGroup>` contains:
```xml
<Nullable>enable</Nullable>
<LangVersion>latest</LangVersion>
```

- [ ] **Step 4: Write the smoke test**

```csharp
// tests/Wc3.Tests/SmokeTest.cs
namespace Wc3.Tests;

public class SmokeTest
{
    [Fact]
    public void War3Net_types_are_referencable()
    {
        var t = typeof(War3Net.IO.Mpq.MpqArchive);
        Assert.Equal("MpqArchive", t.Name);
    }
}
```

- [ ] **Step 5: Build and run**

Run: `dotnet test`
Expected: PASS (1 test). Confirms War3Net resolves and the solution builds.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: solution scaffold with War3Net + xunit smoke test"
```

---

## Task 2: Pre-archive header detection

**Files:**
- Create: `src/Wc3.MapDocument/MpqHeader.cs`
- Test: `tests/Wc3.Tests/MpqHeaderTests.cs`

**Interfaces:**
- Produces: `static int Wc3.MapDocument.MpqHeader.FindArchiveOffset(byte[] fileBytes)` — returns the byte offset where the MPQ archive begins (0 if the file starts with the MPQ magic; otherwise the first 512-byte boundary carrying `MPQ\x1A`). Returns `-1` if no MPQ magic is found.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Wc3.Tests/MpqHeaderTests.cs
using Wc3.MapDocument;
namespace Wc3.Tests;

public class MpqHeaderTests
{
    private static readonly byte[] Magic = { (byte)'M', (byte)'P', (byte)'Q', 0x1A };

    [Fact]
    public void Finds_offset_zero_when_no_preheader()
    {
        var bytes = new byte[1024];
        Magic.CopyTo(bytes, 0);
        Assert.Equal(0, MpqHeader.FindArchiveOffset(bytes));
    }

    [Fact]
    public void Finds_offset_after_512_byte_wc3_header()
    {
        var bytes = new byte[2048];
        bytes[0] = (byte)'H'; bytes[1] = (byte)'M'; bytes[2] = (byte)'3'; bytes[3] = (byte)'W';
        Magic.CopyTo(bytes, 0x200);
        Assert.Equal(0x200, MpqHeader.FindArchiveOffset(bytes));
    }

    [Fact]
    public void Returns_minus_one_when_absent()
    {
        Assert.Equal(-1, MpqHeader.FindArchiveOffset(new byte[600]));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter MpqHeaderTests`
Expected: FAIL — `MpqHeader` does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/Wc3.MapDocument/MpqHeader.cs
namespace Wc3.MapDocument;

public static class MpqHeader
{
    // MPQ archive header magic: 'M','P','Q', 0x1A
    private static readonly byte[] ArchiveMagic = { 0x4D, 0x50, 0x51, 0x1A };

    /// <summary>Offset where the MPQ archive begins. WC3 maps prefix a 512-byte
    /// header (starts with "HM3W"); the archive is aligned to a 512-byte boundary.</summary>
    public static int FindArchiveOffset(byte[] fileBytes)
    {
        for (int offset = 0; offset + 4 <= fileBytes.Length; offset += 0x200)
        {
            if (fileBytes[offset] == ArchiveMagic[0]
                && fileBytes[offset + 1] == ArchiveMagic[1]
                && fileBytes[offset + 2] == ArchiveMagic[2]
                && fileBytes[offset + 3] == ArchiveMagic[3])
            {
                return offset;
            }
        }
        return -1;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter MpqHeaderTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: detect MPQ archive offset behind WC3 pre-archive header"
```

---

## Task 3: MapFileEntry + Diagnostic model

**Files:**
- Create: `src/Wc3.MapDocument/Diagnostic.cs`, `src/Wc3.MapDocument/MapFileEntry.cs`
- Test: `tests/Wc3.Tests/MapFileEntryTests.cs`

**Interfaces:**
- Produces:
  - `enum Wc3.MapDocument.DiagnosticSeverity { Info, Warning, Error }`
  - `record Diagnostic(DiagnosticSeverity Severity, string FileName, string Message)`
  - `class MapFileEntry` with: `string? FileName`, `int BlockIndex`, `byte[] RawBytes` (decompressed original), `bool IsKnown`, `object? Model` (get/set), `bool IsParsed => Model is not null`, `bool IsDirty` (get/set, default false).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Wc3.Tests/MapFileEntryTests.cs
using Wc3.MapDocument;
namespace Wc3.Tests;

public class MapFileEntryTests
{
    [Fact]
    public void Unparsed_entry_reports_not_parsed_and_not_dirty()
    {
        var e = new MapFileEntry { FileName = "x", BlockIndex = 0, RawBytes = new byte[] { 1 }, IsKnown = false };
        Assert.False(e.IsParsed);
        Assert.False(e.IsDirty);
    }

    [Fact]
    public void Setting_model_marks_parsed()
    {
        var e = new MapFileEntry { FileName = "x", BlockIndex = 0, RawBytes = System.Array.Empty<byte>(), IsKnown = true };
        e.Model = new object();
        Assert.True(e.IsParsed);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter MapFileEntryTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

```csharp
// src/Wc3.MapDocument/Diagnostic.cs
namespace Wc3.MapDocument;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record Diagnostic(DiagnosticSeverity Severity, string FileName, string Message);
```

```csharp
// src/Wc3.MapDocument/MapFileEntry.cs
namespace Wc3.MapDocument;

public sealed class MapFileEntry
{
    public required string? FileName { get; init; }   // null => unnamed (protected map)
    public required int BlockIndex { get; init; }
    public required byte[] RawBytes { get; init; }     // original DECOMPRESSED bytes
    public required bool IsKnown { get; init; }
    public object? Model { get; set; }
    public bool IsParsed => Model is not null;
    public bool IsDirty { get; set; }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter MapFileEntryTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: MapFileEntry + Diagnostic model"
```

---

## Task 4: Synthetic map fixture (test helper)

**Files:**
- Create: `tests/Wc3.Tests/SyntheticMap.cs`
- Test: `tests/Wc3.Tests/SyntheticMapTests.cs`

**Interfaces:**
- Produces: `static byte[] Wc3.Tests.SyntheticMap.Build(IReadOnlyDictionary<string, byte[]> files)` — returns a `.w3x`-like byte array: a 512-byte header beginning with `HM3W`, followed by a valid MPQ containing `files`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Wc3.Tests/SyntheticMapTests.cs
using Wc3.MapDocument;
namespace Wc3.Tests;

public class SyntheticMapTests
{
    [Fact]
    public void Built_file_has_mpq_at_offset_0x200()
    {
        var bytes = SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3i"] = new byte[] { 1, 2, 3 },
            ["readme.txt"] = new byte[] { 9 },
        });
        Assert.Equal(0x200, MpqHeader.FindArchiveOffset(bytes));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter SyntheticMapTests`
Expected: FAIL — `SyntheticMap` does not exist.

- [ ] **Step 3: Implement**

```csharp
// tests/Wc3.Tests/SyntheticMap.cs
using War3Net.IO.Mpq;
namespace Wc3.Tests;

public static class SyntheticMap
{
    public static byte[] Build(IReadOnlyDictionary<string, byte[]> files)
    {
        var builder = new MpqArchiveBuilder();
        foreach (var (name, data) in files)
            builder.AddFile(MpqFile.New(new MemoryStream(data), name));

        using var mpq = new MemoryStream();
        builder.SaveTo(mpq);

        using var outStream = new MemoryStream();
        var header = new byte[0x200];
        header[0] = (byte)'H'; header[1] = (byte)'M'; header[2] = (byte)'3'; header[3] = (byte)'W';
        outStream.Write(header, 0, header.Length);
        mpq.Position = 0;
        mpq.CopyTo(outStream);
        return outStream.ToArray();
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter SyntheticMapTests`
Expected: PASS. (If `MpqArchiveBuilder`'s empty constructor or `SaveTo(Stream)` signature differs, fix per IntelliSense — this is the characterization point for the builder API.)

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "test: synthetic .w3x fixture builder"
```

---

## Task 5: MapDocument.Load (raw preservation, no parsing yet)

**Files:**
- Create: `src/Wc3.MapDocument/MapDocument.cs`
- Test: `tests/Wc3.Tests/MapDocumentLoadTests.cs`

**Interfaces:**
- Consumes: `MpqHeader.FindArchiveOffset`, `MapFileEntry`, `Diagnostic`.
- Produces:
  - `class MapDocument` with `IReadOnlyList<MapFileEntry> Files`, `byte[] PreArchiveData`, `IReadOnlyList<Diagnostic> Diagnostics`.
  - `static MapDocument Load(byte[] fileBytes)` and `static MapDocument Load(string path)`.
  - `MapFileEntry? GetFile(string fileName)` (case-insensitive).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Wc3.Tests/MapDocumentLoadTests.cs
using Wc3.MapDocument;
namespace Wc3.Tests;

public class MapDocumentLoadTests
{
    private static byte[] SampleMap() => SyntheticMap.Build(new Dictionary<string, byte[]>
    {
        ["war3map.w3i"] = new byte[] { 1, 2, 3, 4 },
        ["war3map.j"]   = new byte[] { 5, 6 },
        ["mystery.bin"] = new byte[] { 7, 8, 9 },
    });

    [Fact]
    public void Loads_all_named_files_with_raw_bytes()
    {
        var doc = MapDocument.Load(SampleMap());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, doc.GetFile("war3map.w3i")!.RawBytes);
        Assert.Equal(new byte[] { 7, 8, 9 }, doc.GetFile("mystery.bin")!.RawBytes);
    }

    [Fact]
    public void Preserves_pre_archive_header()
    {
        var doc = MapDocument.Load(SampleMap());
        Assert.Equal(0x200, doc.PreArchiveData.Length);
        Assert.Equal((byte)'H', doc.PreArchiveData[0]);
    }

    [Fact]
    public void Marks_known_files_known_and_others_unknown()
    {
        var doc = MapDocument.Load(SampleMap());
        Assert.True(doc.GetFile("war3map.w3i")!.IsKnown);
        Assert.False(doc.GetFile("mystery.bin")!.IsKnown);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter MapDocumentLoadTests`
Expected: FAIL — `MapDocument` does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/Wc3.MapDocument/MapDocument.cs
using War3Net.IO.Mpq;

namespace Wc3.MapDocument;

public sealed class MapDocument
{
    private readonly byte[] _originalBytes;
    private readonly List<MapFileEntry> _files = new();
    private readonly List<Diagnostic> _diagnostics = new();

    public IReadOnlyList<MapFileEntry> Files => _files;
    public byte[] PreArchiveData { get; private set; } = Array.Empty<byte>();
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    private MapDocument(byte[] originalBytes) => _originalBytes = originalBytes;

    public static MapDocument Load(string path) => Load(File.ReadAllBytes(path));

    public static MapDocument Load(byte[] fileBytes)
    {
        var doc = new MapDocument(fileBytes);

        int offset = MpqHeader.FindArchiveOffset(fileBytes);
        if (offset < 0)
            throw new InvalidDataException("No MPQ archive magic found in file.");
        doc.PreArchiveData = fileBytes[..offset];

        using var stream = new MemoryStream(fileBytes);
        using var archive = MpqArchive.Open(stream, loadListFile: true);

        int block = 0;
        foreach (var entry in archive)
        {
            byte[] raw;
            using (var fs = archive.OpenFile(entry))
            {
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                raw = ms.ToArray();
            }

            string? name = entry.FileName;
            bool known = name is not null && MapFormatRegistry.IsKnown(name);
            doc._files.Add(new MapFileEntry
            {
                FileName = name,
                BlockIndex = block++,
                RawBytes = raw,
                IsKnown = known,
            });
        }

        doc.ParseKnownFiles();
        return doc;
    }

    // Filled in Task 7.
    private void ParseKnownFiles() { }

    public MapFileEntry? GetFile(string fileName) =>
        _files.FirstOrDefault(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
}
```

Note: `MapFormatRegistry.IsKnown` is created in Task 6. Add a temporary stub `internal static bool IsKnown(string n) => n.StartsWith("war3map", StringComparison.OrdinalIgnoreCase);` in a new `MapFormatRegistry.cs` if implementing out of order, then replace in Task 6.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter MapDocumentLoadTests`
Expected: PASS (3 tests). (If `entry.FileName` is null for named files, ensure `loadListFile: true` and that the synthetic builder wrote a listfile; adjust if War3Net requires `archive.AddFileName`.)

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: MapDocument.Load with raw byte preservation"
```

---

## Task 6: Format registry + graceful parse invocation

**Files:**
- Create/replace: `src/Wc3.MapDocument/MapFormatRegistry.cs`
- Test: `tests/Wc3.Tests/MapFormatRegistryTests.cs`

**Interfaces:**
- Produces:
  - `delegate object Wc3.MapDocument.ParseFn(byte[] raw)`
  - `static bool MapFormatRegistry.IsKnown(string fileName)`
  - `static bool MapFormatRegistry.TryGetParser(string fileName, out ParseFn parser)`
  - `static object ReadWith<T>(byte[] raw, Func<System.IO.BinaryReader, T> read)` — helper wrapping raw bytes in a `BinaryReader`.
  - Registry seeded with the known `war3map.*` filenames (parsers wired in Task 8; unwired-but-known names return `false` from `TryGetParser`).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Wc3.Tests/MapFormatRegistryTests.cs
using Wc3.MapDocument;
namespace Wc3.Tests;

public class MapFormatRegistryTests
{
    [Theory]
    [InlineData("war3map.w3i")]
    [InlineData("war3map.w3e")]
    [InlineData("war3map.doo")]
    [InlineData("war3mapUnits.doo")]
    [InlineData("war3map.imp")]
    public void Known_formats_are_recognized(string name) => Assert.True(MapFormatRegistry.IsKnown(name));

    [Fact]
    public void Unknown_formats_are_not_recognized() => Assert.False(MapFormatRegistry.IsKnown("mystery.bin"));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter MapFormatRegistryTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

```csharp
// src/Wc3.MapDocument/MapFormatRegistry.cs
namespace Wc3.MapDocument;

public delegate object ParseFn(byte[] raw);

public static class MapFormatRegistry
{
    // Known WC3 map files. Parse delegates are wired in Task 8; a null delegate
    // means "known but not yet parsed into a typed model" (degrades to raw).
    private static readonly Dictionary<string, ParseFn?> _parsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["war3map.w3e"] = null,          // terrain
        ["war3map.wpm"] = null,          // pathing
        ["war3map.doo"] = null,          // doodads
        ["war3mapUnits.doo"] = null,     // units/items
        ["war3map.w3i"] = null,          // info
        ["war3map.w3r"] = null,          // regions
        ["war3map.w3c"] = null,          // cameras
        ["war3map.w3s"] = null,          // sounds
        ["war3map.wtg"] = null,          // triggers
        ["war3map.wct"] = null,          // custom text triggers
        ["war3map.j"] = null,            // JASS script
        ["war3map.lua"] = null,          // Lua script
        ["war3map.wts"] = null,          // trigger strings
        ["war3map.imp"] = null,          // imports
        ["war3map.w3u"] = null,          // unit object data
        ["war3map.w3a"] = null,          // ability object data
        ["war3map.w3t"] = null,          // item object data
        ["war3map.w3b"] = null,          // destructable object data
        ["war3map.w3d"] = null,          // doodad object data
        ["war3map.w3h"] = null,          // buff object data
        ["war3map.w3q"] = null,          // upgrade object data
    };

    public static bool IsKnown(string fileName) => _parsers.ContainsKey(fileName);

    public static bool TryGetParser(string fileName, out ParseFn parser)
    {
        if (_parsers.TryGetValue(fileName, out var p) && p is not null)
        {
            parser = p;
            return true;
        }
        parser = _ => throw new InvalidOperationException();
        return false;
    }

    internal static void Register(string fileName, ParseFn parser) => _parsers[fileName] = parser;

    public static object ReadWith<T>(byte[] raw, Func<BinaryReader, T> read)
    {
        using var ms = new MemoryStream(raw);
        using var reader = new BinaryReader(ms);
        return read(reader)!;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter MapFormatRegistryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: known-format registry with graceful parse hooks"
```

---

## Task 7: Wire graceful parsing into Load

**Files:**
- Modify: `src/Wc3.MapDocument/MapDocument.cs` (`ParseKnownFiles`)
- Test: `tests/Wc3.Tests/MapDocumentParseTests.cs`

**Interfaces:**
- Consumes: `MapFormatRegistry.TryGetParser`, `ReadWith`.
- Produces: after `Load`, each known file with a wired, succeeding parser has `Model != null`; a parser that throws leaves `Model == null` and appends a `Diagnostic(Warning, fileName, ...)`. Never throws.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Wc3.Tests/MapDocumentParseTests.cs
using Wc3.MapDocument;
namespace Wc3.Tests;

public class MapDocumentParseTests
{
    [Fact]
    public void Parser_that_throws_degrades_to_raw_with_diagnostic()
    {
        MapFormatRegistry.Register("war3map.w3i", _ => throw new InvalidDataException("boom"));
        var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
        {
            ["war3map.w3i"] = new byte[] { 1, 2, 3 },
        }));

        var entry = doc.GetFile("war3map.w3i")!;
        Assert.False(entry.IsParsed);
        Assert.NotEmpty(entry.RawBytes);
        Assert.Contains(doc.Diagnostics, d => d.FileName == "war3map.w3i" && d.Severity == DiagnosticSeverity.Warning);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter MapDocumentParseTests`
Expected: FAIL — `ParseKnownFiles` is a no-op.

- [ ] **Step 3: Implement**

Replace the `ParseKnownFiles` stub in `MapDocument.cs`:
```csharp
private void ParseKnownFiles()
{
    foreach (var entry in _files)
    {
        if (entry.FileName is null || !MapFormatRegistry.TryGetParser(entry.FileName, out var parse))
            continue;
        try
        {
            entry.Model = parse(entry.RawBytes);
        }
        catch (Exception ex)
        {
            _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, entry.FileName,
                $"Parse failed, preserved as raw: {ex.Message}"));
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter MapDocumentParseTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: graceful parse-on-load with diagnostics"
```

---

## Task 8: Wire War3Net parsers for all known formats

**Files:**
- Create: `src/Wc3.MapDocument/Parsers.cs` (a `static ModuleInitializer` or explicit `MapFormatRegistry.RegisterDefaults()`)
- Modify: `MapDocument.cs` to call `MapFormatRegistry.RegisterDefaults()` once (static ctor).
- Test: `tests/Wc3.Tests/ParseCoverageTests.cs`

**Interfaces:**
- Consumes: War3Net.Build.Core `BinaryReader` extensions.
- Produces: `static void MapFormatRegistry.RegisterDefaults()` (idempotent) that wires a `ParseFn` for every entry in the registry using the corresponding War3Net reader extension.

**War3Net type map (confirm exact extension names against the package):**

| File | War3Net reader extension (expected) | Model type |
|---|---|---|
| war3map.w3i | `r.ReadMapInfo()` | `MapInfo` |
| war3map.w3e | `r.ReadMapEnvironment()` | `MapEnvironment` |
| war3map.wpm | `r.ReadMapPathingMap()` | `MapPathingMap` |
| war3map.doo | `r.ReadMapDoodads()` | `MapDoodads` |
| war3mapUnits.doo | `r.ReadMapUnits()` | `MapUnits` |
| war3map.w3r | `r.ReadMapRegions()` | `MapRegions` |
| war3map.w3c | `r.ReadMapCameras()` | `MapCameras` |
| war3map.w3s | `r.ReadMapSounds()` | `MapSounds` |
| war3map.wtg | `r.ReadMapTriggers()` | `MapTriggers` |
| war3map.wct | `r.ReadMapCustomTextTriggers()` | `MapCustomTextTriggers` |
| war3map.wts | `r.ReadMapTriggerStrings()` | `MapTriggerStrings` |
| war3map.imp | `r.ReadMapImportedFiles()` | `ImportedFiles` |
| war3map.w3u | `r.ReadUnitObjectData()` | `UnitObjectData` |
| war3map.w3a | `r.ReadAbilityObjectData()` | `AbilityObjectData` |
| war3map.w3t | `r.ReadItemObjectData()` | `ItemObjectData` |
| war3map.w3b | `r.ReadDestructableObjectData()` | `DestructableObjectData` |
| war3map.w3d | `r.ReadDoodadObjectData()` | `DoodadObjectData` |
| war3map.w3h | `r.ReadBuffObjectData()` | `BuffObjectData` |
| war3map.w3q | `r.ReadUpgradeObjectData()` | `UpgradeObjectData` |
| war3map.j / war3map.lua | raw text | `string` (UTF-8) |

- [ ] **Step 1: Write the failing test (parse coverage over the real corpus)**

```csharp
// tests/Wc3.Tests/ParseCoverageTests.cs
using Wc3.MapDocument;
namespace Wc3.Tests;

public class ParseCoverageTests
{
    // The real map lives outside the repo; skip cleanly if absent.
    private const string CorpusMap = @"C:\Users\GodMephisto\Downloads\ggg_en_1.11r_slk.w3x";

    [Fact]
    [Trait("Category", "Corpus")]
    public void Every_known_file_parses_or_has_diagnostic_no_crash()
    {
        if (!File.Exists(CorpusMap)) return; // corpus-optional

        var doc = MapDocument.Load(CorpusMap);

        foreach (var f in doc.Files.Where(f => f.IsKnown))
        {
            bool parsed = f.IsParsed;
            bool hasDiag = doc.Diagnostics.Any(d => d.FileName == f.FileName);
            Assert.True(parsed || hasDiag, $"{f.FileName}: neither parsed nor diagnosed");
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails / is inconclusive**

Run: `dotnet test --filter ParseCoverageTests`
Expected: FAIL to compile until `RegisterDefaults` exists (test references only public API, so it compiles, but with no parsers wired every known file is unparsed and undiagnosed → assertion FAILS). Confirms the gap.

- [ ] **Step 3: Implement RegisterDefaults**

```csharp
// src/Wc3.MapDocument/Parsers.cs
using System.Text;
using War3Net.Build.Info;
using War3Net.Build.Environment;
using War3Net.Build.Widget;
using War3Net.Build.Object;
using War3Net.Build.Script;
using War3Net.Build.Audio;
// NOTE: adjust the using-namespaces + extension names to the referenced War3Net version.

namespace Wc3.MapDocument;

public static class DefaultParsers
{
    private static bool _registered;

    public static void RegisterDefaults()
    {
        if (_registered) return;
        _registered = true;

        Wire("war3map.w3i", r => r.ReadMapInfo());
        Wire("war3map.w3e", r => r.ReadMapEnvironment());
        Wire("war3map.wpm", r => r.ReadMapPathingMap());
        Wire("war3map.doo", r => r.ReadMapDoodads());
        Wire("war3mapUnits.doo", r => r.ReadMapUnits());
        Wire("war3map.w3r", r => r.ReadMapRegions());
        Wire("war3map.w3c", r => r.ReadMapCameras());
        Wire("war3map.w3s", r => r.ReadMapSounds());
        Wire("war3map.wtg", r => r.ReadMapTriggers());
        Wire("war3map.wct", r => r.ReadMapCustomTextTriggers());
        Wire("war3map.wts", r => r.ReadMapTriggerStrings());
        Wire("war3map.imp", r => r.ReadImportedFiles());
        Wire("war3map.w3u", r => r.ReadUnitObjectData());
        Wire("war3map.w3a", r => r.ReadAbilityObjectData());
        Wire("war3map.w3t", r => r.ReadItemObjectData());
        Wire("war3map.w3b", r => r.ReadDestructableObjectData());
        Wire("war3map.w3d", r => r.ReadDoodadObjectData());
        Wire("war3map.w3h", r => r.ReadBuffObjectData());
        Wire("war3map.w3q", r => r.ReadUpgradeObjectData());

        MapFormatRegistry.Register("war3map.j", raw => Encoding.UTF8.GetString(raw));
        MapFormatRegistry.Register("war3map.lua", raw => Encoding.UTF8.GetString(raw));
    }

    private static void Wire<T>(string file, Func<BinaryReader, T> read) =>
        MapFormatRegistry.Register(file, raw => MapFormatRegistry.ReadWith(raw, read)!);
}
```

Add to `MapDocument`'s static constructor:
```csharp
static MapDocument() => DefaultParsers.RegisterDefaults();
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter ParseCoverageTests`
Expected: PASS. Any format whose extension name differs will surface as a compile error (fix the name) or a runtime parse exception captured as a diagnostic (still passes coverage, but note it for a follow-up). Record any diagnosed-but-unparsed formats in the commit message.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: wire War3Net parsers for all known war3map formats"
```

---

## Task 9: MapDocument.Save + round-trip fidelity gate

**Files:**
- Modify: `src/Wc3.MapDocument/MapDocument.cs` (add `Save`)
- Test: `tests/Wc3.Tests/RoundtripTests.cs`

**Interfaces:**
- Consumes: `MpqArchiveBuilder`, `MpqFile`, `PreArchiveData`.
- Produces: `void MapDocument.Save(string path)` and `byte[] MapDocument.SaveToBytes()`. No-edit save yields per-file decompressed-byte-identical output and identical pre-archive header.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/Wc3.Tests/RoundtripTests.cs
using Wc3.MapDocument;
namespace Wc3.Tests;

public class RoundtripTests
{
    private static byte[] Sample() => SyntheticMap.Build(new Dictionary<string, byte[]>
    {
        ["war3map.w3i"] = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray(),
        ["war3map.j"]   = System.Text.Encoding.UTF8.GetBytes("function main takes nothing returns nothing\nendfunction\n"),
        ["mystery.bin"] = new byte[] { 42, 0, 255, 7 },
    });

    [Fact]
    public void No_edit_roundtrip_preserves_every_file_and_header()
    {
        var original = MapDocument.Load(Sample());
        var rebuilt = MapDocument.Load(original.SaveToBytes());

        Assert.Equal(original.PreArchiveData, rebuilt.PreArchiveData);

        var origByName = original.Files.Where(f => f.FileName != null)
                                       .ToDictionary(f => f.FileName!, f => f.RawBytes);
        var newByName = rebuilt.Files.Where(f => f.FileName != null)
                                     .ToDictionary(f => f.FileName!, f => f.RawBytes);

        Assert.Equal(origByName.Keys.OrderBy(k => k), newByName.Keys.OrderBy(k => k));
        foreach (var (name, bytes) in origByName)
            Assert.Equal(bytes, newByName[name]);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter RoundtripTests`
Expected: FAIL — `SaveToBytes` does not exist.

- [ ] **Step 3: Implement**

```csharp
// add to MapDocument.cs
public void Save(string path) => File.WriteAllBytes(path, SaveToBytes());

public byte[] SaveToBytes()
{
    using var source = new MemoryStream(_originalBytes);
    using var archive = MpqArchive.Open(source, loadListFile: true);
    var builder = new MpqArchiveBuilder(archive);

    foreach (var entry in _files.Where(f => f.IsDirty && f.FileName is not null))
    {
        builder.RemoveFile(entry.FileName!);
        builder.AddFile(MpqFile.New(new MemoryStream(SerializeEntry(entry)), entry.FileName!));
    }

    using var mpq = new MemoryStream();
    builder.SaveTo(mpq);

    using var outStream = new MemoryStream();
    outStream.Write(PreArchiveData, 0, PreArchiveData.Length);
    mpq.Position = 0;
    mpq.CopyTo(outStream);
    return outStream.ToArray();
}

// No editing this slice, so dirty files never occur; serialization is a later slice.
private static byte[] SerializeEntry(MapFileEntry entry) =>
    throw new NotSupportedException("Editing/serialization arrives in a later slice.");
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter RoundtripTests`
Expected: PASS. If a file's bytes differ, inspect whether War3Net regenerated `(listfile)`/`(attributes)`; if so, exclude those two special files from the comparison OR preserve them explicitly — decide based on the actual diff and note the decision in the commit.

- [ ] **Step 5: Add the corpus round-trip assertion**

Append to `RoundtripTests.cs`:
```csharp
[Fact]
[Trait("Category", "Corpus")]
public void Real_map_roundtrips_every_file()
{
    const string path = @"C:\Users\GodMephisto\Downloads\ggg_en_1.11r_slk.w3x";
    if (!File.Exists(path)) return;

    var original = MapDocument.Load(path);
    var rebuilt = MapDocument.Load(original.SaveToBytes());

    var orig = original.Files.Where(f => f.FileName != null).ToDictionary(f => f.FileName!, f => f.RawBytes);
    var round = rebuilt.Files.Where(f => f.FileName != null).ToDictionary(f => f.FileName!, f => f.RawBytes);
    foreach (var (name, bytes) in orig)
        Assert.True(round.ContainsKey(name) && round[name].SequenceEqual(bytes), $"mismatch: {name}");
}
```

Run: `dotnet test --filter RoundtripTests`
Expected: PASS (both tests; corpus test may take several seconds on the 63 MB map).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: byte-faithful per-file round-trip save + fidelity gate"
```

---

## Task 10: Shared command layer — Info + List

**Files:**
- Create: `src/Wc3.Commands/Wc3.Commands.csproj`, `src/Wc3.Commands/Results.cs`, `src/Wc3.Commands/InfoCommand.cs`, `src/Wc3.Commands/ListCommand.cs`
- Modify: `Wc3.sln` (add project), `tests/Wc3.Tests` reference.
- Test: `tests/Wc3.Tests/CommandTests.cs`

**Interfaces:**
- Consumes: `MapDocument`, War3Net `MapInfo`.
- Produces:
  - `record FileListResult(IReadOnlyList<FileEntryInfo> Files)` and `record FileEntryInfo(string? Name, int SizeBytes, bool Known, bool Parsed)`.
  - `record MapInfoResult(string Name, string Author, int Players, int? Width, int? Height, IReadOnlyList<string> Diagnostics)`.
  - `static MapInfoResult InfoCommand.Execute(MapDocument doc)`.
  - `static FileListResult ListCommand.Execute(MapDocument doc)`.

- [ ] **Step 1: Create the project and wire references**

```bash
cd "D:/playground/Programming/Wc3_CLI"
dotnet new classlib -n Wc3.Commands -o src/Wc3.Commands -f net8.0
rm src/Wc3.Commands/Class1.cs
dotnet sln add src/Wc3.Commands/Wc3.Commands.csproj
dotnet add src/Wc3.Commands reference src/Wc3.MapDocument
dotnet add tests/Wc3.Tests reference src/Wc3.Commands
```
Set `<Nullable>enable</Nullable>` and `<LangVersion>latest</LangVersion>` in the new csproj.

- [ ] **Step 2: Write the failing test**

```csharp
// tests/Wc3.Tests/CommandTests.cs
using Wc3.MapDocument;
using Wc3.Commands;
namespace Wc3.Tests;

public class CommandTests
{
    private static MapDocument Doc() => MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]>
    {
        ["war3map.j"] = new byte[] { 1, 2, 3 },
        ["mystery.bin"] = new byte[] { 9 },
    }));

    [Fact]
    public void List_reports_all_files_with_metadata()
    {
        var result = ListCommand.Execute(Doc());
        Assert.Contains(result.Files, f => f.Name == "war3map.j" && f.Known);
        Assert.Contains(result.Files, f => f.Name == "mystery.bin" && !f.Known);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter CommandTests`
Expected: FAIL — command types do not exist.

- [ ] **Step 4: Implement**

```csharp
// src/Wc3.Commands/Results.cs
namespace Wc3.Commands;

public sealed record FileEntryInfo(string? Name, int SizeBytes, bool Known, bool Parsed);
public sealed record FileListResult(IReadOnlyList<FileEntryInfo> Files);
public sealed record MapInfoResult(
    string Name, string Author, int Players, int? Width, int? Height, IReadOnlyList<string> Diagnostics);
```

```csharp
// src/Wc3.Commands/ListCommand.cs
using Wc3.MapDocument;
namespace Wc3.Commands;

public static class ListCommand
{
    public static FileListResult Execute(MapDocument doc) => new(
        doc.Files.Select(f => new FileEntryInfo(f.FileName, f.RawBytes.Length, f.IsKnown, f.IsParsed)).ToList());
}
```

```csharp
// src/Wc3.Commands/InfoCommand.cs
using Wc3.MapDocument;
using War3Net.Build.Info;
namespace Wc3.Commands;

public static class InfoCommand
{
    public static MapInfoResult Execute(MapDocument doc)
    {
        var info = doc.GetFile("war3map.w3i")?.Model as MapInfo;
        var diags = doc.Diagnostics.Select(d => $"{d.Severity}: {d.FileName} — {d.Message}").ToList();
        return new MapInfoResult(
            Name: info?.MapName ?? "(unknown)",
            Author: info?.MapAuthor ?? "(unknown)",
            Players: info?.Players?.Count ?? 0,
            Width: info?.CameraBoundsComplements is null ? null : info?.PlayableMapWidth,
            Height: info?.CameraBoundsComplements is null ? null : info?.PlayableMapHeight,
            Diagnostics: diags);
    }
}
```
Note: `MapInfo` property names (`MapName`, `MapAuthor`, `Players`, `PlayableMapWidth`…) must be confirmed against the package; adjust to the real property names — the test only exercises `ListCommand`, so `InfoCommand` compiling against the real `MapInfo` is the check.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test --filter CommandTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: shared command layer with info + list"
```

---

## Task 11: Command layer — ObjectGet, Search, Diff, Roundtrip

**Files:**
- Create: `src/Wc3.Commands/ObjectGetCommand.cs`, `SearchCommand.cs`, `DiffCommand.cs`, `RoundtripCommand.cs`; extend `Results.cs`.
- Test: extend `tests/Wc3.Tests/CommandTests.cs`

**Interfaces:**
- Produces:
  - `record SearchHit(string FileName, string Context)`, `record SearchResult(IReadOnlyList<SearchHit> Hits)`.
  - `record DiffEntry(string Name, string Change)` (Change ∈ `added|removed|modified`), `record DiffResult(IReadOnlyList<DiffEntry> Entries)`.
  - `record RoundtripResult(bool Faithful, IReadOnlyList<string> Mismatches)`.
  - `record ObjectGetResult(string Rawcode, bool Found, IReadOnlyDictionary<string,string> Fields)`.
  - `static SearchResult SearchCommand.Execute(MapDocument doc, string query)` — searches `war3map.wts` strings and file names (case-insensitive substring).
  - `static DiffResult DiffCommand.Execute(MapDocument a, MapDocument b)` — compares file sets + raw bytes.
  - `static RoundtripResult RoundtripCommand.Execute(MapDocument doc)` — re-loads `doc.SaveToBytes()` and diffs per-file bytes + header.
  - `static ObjectGetResult ObjectGetCommand.Execute(MapDocument doc, string rawcode, string? field)` — for this slice returns `Found=false` unless object data is trivially indexable; a full object index is a later slice (return `Found=false` with an empty map when the model isn't indexable yet).

- [ ] **Step 1: Write the failing tests**

```csharp
// append to CommandTests.cs
[Fact]
public void Diff_detects_added_and_removed_files()
{
    var a = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]> { ["a.txt"] = new byte[]{1}, ["shared.txt"] = new byte[]{2} }));
    var b = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]> { ["b.txt"] = new byte[]{1}, ["shared.txt"] = new byte[]{2} }));
    var diff = Wc3.Commands.DiffCommand.Execute(a, b);
    Assert.Contains(diff.Entries, e => e.Name == "a.txt" && e.Change == "removed");
    Assert.Contains(diff.Entries, e => e.Name == "b.txt" && e.Change == "added");
    Assert.DoesNotContain(diff.Entries, e => e.Name == "shared.txt");
}

[Fact]
public void Roundtrip_command_reports_faithful_for_unedited_map()
{
    var doc = MapDocument.Load(SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[]{1,2,3}, ["x.bin"] = new byte[]{4} }));
    var result = Wc3.Commands.RoundtripCommand.Execute(doc);
    Assert.True(result.Faithful, string.Join(",", result.Mismatches));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter CommandTests`
Expected: FAIL — new command types missing.

- [ ] **Step 3: Implement**

```csharp
// append to Results.cs
namespace Wc3.Commands;
public sealed record SearchHit(string FileName, string Context);
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits);
public sealed record DiffEntry(string Name, string Change);
public sealed record DiffResult(IReadOnlyList<DiffEntry> Entries);
public sealed record RoundtripResult(bool Faithful, IReadOnlyList<string> Mismatches);
public sealed record ObjectGetResult(string Rawcode, bool Found, IReadOnlyDictionary<string,string> Fields);
```

```csharp
// src/Wc3.Commands/DiffCommand.cs
using Wc3.MapDocument;
namespace Wc3.Commands;

public static class DiffCommand
{
    public static DiffResult Execute(MapDocument a, MapDocument b)
    {
        var av = a.Files.Where(f => f.FileName != null).ToDictionary(f => f.FileName!, f => f.RawBytes);
        var bv = b.Files.Where(f => f.FileName != null).ToDictionary(f => f.FileName!, f => f.RawBytes);
        var entries = new List<DiffEntry>();
        foreach (var name in av.Keys.Union(bv.Keys))
        {
            bool inA = av.ContainsKey(name), inB = bv.ContainsKey(name);
            if (inA && !inB) entries.Add(new DiffEntry(name, "removed"));
            else if (!inA && inB) entries.Add(new DiffEntry(name, "added"));
            else if (!av[name].SequenceEqual(bv[name])) entries.Add(new DiffEntry(name, "modified"));
        }
        return new DiffResult(entries);
    }
}
```

```csharp
// src/Wc3.Commands/RoundtripCommand.cs
using Wc3.MapDocument;
namespace Wc3.Commands;

public static class RoundtripCommand
{
    public static RoundtripResult Execute(MapDocument doc)
    {
        var rebuilt = MapDocument.Load(doc.SaveToBytes());
        var mismatches = new List<string>();
        if (!doc.PreArchiveData.SequenceEqual(rebuilt.PreArchiveData))
            mismatches.Add("(pre-archive header)");
        var diff = DiffCommand.Execute(doc, rebuilt);
        mismatches.AddRange(diff.Entries.Select(e => $"{e.Name}:{e.Change}"));
        return new RoundtripResult(mismatches.Count == 0, mismatches);
    }
}
```

```csharp
// src/Wc3.Commands/SearchCommand.cs
using Wc3.MapDocument;
namespace Wc3.Commands;

public static class SearchCommand
{
    public static SearchResult Execute(MapDocument doc, string query)
    {
        var hits = new List<SearchHit>();
        foreach (var f in doc.Files.Where(f => f.FileName != null))
            if (f.FileName!.Contains(query, StringComparison.OrdinalIgnoreCase))
                hits.Add(new SearchHit(f.FileName!, "filename"));
        return new SearchResult(hits);
    }
}
```

```csharp
// src/Wc3.Commands/ObjectGetCommand.cs
using Wc3.MapDocument;
namespace Wc3.Commands;

public static class ObjectGetCommand
{
    // Full object-data indexing is a later slice; this returns Found=false for now
    // but establishes the command signature and result shape.
    public static ObjectGetResult Execute(MapDocument doc, string rawcode, string? field) =>
        new(rawcode, Found: false, Fields: new Dictionary<string, string>());
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter CommandTests`
Expected: PASS (all command tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: diff, roundtrip, search, object-get commands"
```

---

## Task 12: wc3ctl CLI front-end

**Files:**
- Create: `src/wc3ctl/wc3ctl.csproj`, `src/wc3ctl/Program.cs`, `src/wc3ctl/Render.cs`
- Modify: `Wc3.sln`
- Test: `tests/Wc3.Tests/CliTests.cs`

**Interfaces:**
- Consumes: all commands in `Wc3.Commands`.
- Produces: an executable `wc3ctl` with subcommands `info`, `ls`, `object get`, `search`, `diff`, `roundtrip`, each accepting a map path argument and a global `--json` option. `Render` converts result POCOs to human strings; `--json` uses `System.Text.Json`.

- [ ] **Step 1: Create the project**

```bash
cd "D:/playground/Programming/Wc3_CLI"
dotnet new console -n wc3ctl -o src/wc3ctl -f net8.0
dotnet sln add src/wc3ctl/wc3ctl.csproj
dotnet add src/wc3ctl reference src/Wc3.Commands src/Wc3.MapDocument
dotnet add src/wc3ctl package System.CommandLine --prerelease
```
Set nullable/langversion. Set `<AssemblyName>wc3ctl</AssemblyName>`.

- [ ] **Step 2: Write the failing test (invoke the root command in-process)**

```csharp
// tests/Wc3.Tests/CliTests.cs
namespace Wc3.Tests;

public class CliTests
{
    [Fact]
    public async Task Ls_json_lists_files()
    {
        var map = SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[] { 1 } });
        var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_test_{System.Guid.NewGuid():N}.w3x");
        File.WriteAllBytes(path, map);
        try
        {
            var sw = new StringWriter();
            var console = System.Console.Out;
            System.Console.SetOut(sw);
            int code = await Wc3Ctl.Program.Main(new[] { "ls", path, "--json" });
            System.Console.SetOut(console);
            Assert.Equal(0, code);
            Assert.Contains("war3map.j", sw.ToString());
        }
        finally { File.Delete(path); }
    }
}
```
Add `dotnet add tests/Wc3.Tests reference src/wc3ctl`. Ensure `Program` is `public` with `public static Task<int> Main(string[] args)` and namespace `Wc3Ctl`.

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter CliTests`
Expected: FAIL — `Wc3Ctl.Program` not accessible / not built.

- [ ] **Step 4: Implement Program + Render**

```csharp
// src/wc3ctl/Render.cs
using System.Text.Json;
using Wc3.Commands;
namespace Wc3Ctl;

public static class Render
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string AsJson(object o) => JsonSerializer.Serialize(o, Json);

    public static string List(FileListResult r) =>
        string.Join("\n", r.Files.Select(f =>
            $"{f.Name ?? "(unnamed)",-28} {f.SizeBytes,10}  {(f.Known ? "known" : "unknown")}{(f.Parsed ? "/parsed" : "")}"));

    public static string Info(MapInfoResult r) =>
        $"Name:    {r.Name}\nAuthor:  {r.Author}\nPlayers: {r.Players}\nSize:    {r.Width}x{r.Height}"
        + (r.Diagnostics.Count == 0 ? "" : "\n\nDiagnostics:\n" + string.Join("\n", r.Diagnostics));

    public static string Roundtrip(RoundtripResult r) =>
        r.Faithful ? "OK — round-trip is byte-faithful for all files."
                   : "MISMATCH:\n" + string.Join("\n", r.Mismatches);

    public static string Diff(DiffResult r) =>
        r.Entries.Count == 0 ? "(identical)" : string.Join("\n", r.Entries.Select(e => $"{e.Change,-9} {e.Name}"));

    public static string Search(SearchResult r) =>
        r.Hits.Count == 0 ? "(no hits)" : string.Join("\n", r.Hits.Select(h => $"{h.FileName}  [{h.Context}]"));
}
```

```csharp
// src/wc3ctl/Program.cs
using System.CommandLine;
using Wc3.Commands;
using Wc3.MapDocument;

namespace Wc3Ctl;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var jsonOption = new Option<bool>("--json", "Emit machine-readable JSON.");
        var mapArg = new Argument<string>("map", "Path to a .w3x/.w3m map.");

        var root = new RootCommand("wc3ctl — Warcraft III map tool");
        root.AddGlobalOption(jsonOption);

        void Emit(bool json, object result, Func<string> human) =>
            Console.WriteLine(json ? Render.AsJson(result) : human());

        var info = new Command("info", "Show map metadata.") { mapArg };
        info.SetHandler((string map, bool json) =>
        {
            var r = InfoCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.Info(r));
        }, mapArg, jsonOption);

        var ls = new Command("ls", "List internal files.") { mapArg };
        ls.SetHandler((string map, bool json) =>
        {
            var r = ListCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.List(r));
        }, mapArg, jsonOption);

        var rt = new Command("roundtrip", "Verify byte-faithful round-trip.") { mapArg };
        rt.SetHandler((string map, bool json) =>
        {
            var r = RoundtripCommand.Execute(MapDocument.Load(map));
            Emit(json, r, () => Render.Roundtrip(r));
        }, mapArg, jsonOption);

        var search = new Command("search", "Search map contents.") { mapArg, new Argument<string>("query") };
        var queryArg = (Argument<string>)search.Arguments[1];
        search.SetHandler((string map, string query, bool json) =>
        {
            var r = SearchCommand.Execute(MapDocument.Load(map), query);
            Emit(json, r, () => Render.Search(r));
        }, mapArg, queryArg, jsonOption);

        var mapB = new Argument<string>("mapB");
        var diff = new Command("diff", "Diff two maps.") { mapArg, mapB };
        diff.SetHandler((string a, string b, bool json) =>
        {
            var r = DiffCommand.Execute(MapDocument.Load(a), MapDocument.Load(b));
            Emit(json, r, () => Render.Diff(r));
        }, mapArg, mapB, jsonOption);

        var objRawcode = new Argument<string>("rawcode");
        var objField = new Option<string?>("--field");
        var objGet = new Command("object", "Object data queries.");
        var objGetSub = new Command("get", "Get an object's fields.") { mapArg, objRawcode, objField };
        objGetSub.SetHandler((string map, string rawcode, string? field, bool json) =>
        {
            var r = ObjectGetCommand.Execute(MapDocument.Load(map), rawcode, field);
            Emit(json, r, () => r.Found ? string.Join("\n", r.Fields.Select(kv => $"{kv.Key}={kv.Value}")) : $"{rawcode}: not found");
        }, mapArg, objRawcode, objField, jsonOption);
        objGet.AddCommand(objGetSub);

        root.AddCommand(info); root.AddCommand(ls); root.AddCommand(rt);
        root.AddCommand(search); root.AddCommand(diff); root.AddCommand(objGet);

        return await root.InvokeAsync(args);
    }
}
```

Note: `System.CommandLine` beta API for handler binding may differ slightly across beta versions; adjust `SetHandler`/`AddGlobalOption` to the referenced beta. The CLI test is the enforcement.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test --filter CliTests`
Expected: PASS.

- [ ] **Step 6: Manual check against the real map**

Run:
```bash
dotnet run --project src/wc3ctl -- ls "C:/Users/GodMephisto/Downloads/ggg_en_1.11r_slk.w3x"
dotnet run --project src/wc3ctl -- roundtrip "C:/Users/GodMephisto/Downloads/ggg_en_1.11r_slk.w3x"
```
Expected: `ls` prints the file table; `roundtrip` prints `OK — round-trip is byte-faithful for all files.` (If not OK, inspect the mismatch list — this is the real acceptance moment.)

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: wc3ctl CLI (info/ls/roundtrip/search/diff/object get) with --json"
```

---

## Task 13: MCP seam stub

**Files:**
- Create: `src/Wc3.Mcp/Wc3.Mcp.csproj`, `src/Wc3.Mcp/README.md`
- Modify: `Wc3.sln`

**Interfaces:**
- Produces: a project that references `Wc3.Commands` and documents the adapter plan. No server implementation this slice; the point is to prove `Wc3.Commands` has no CLI/MCP-specific dependency (it compiles as the sole reference of a non-CLI consumer).

- [ ] **Step 1: Create project referencing only the command layer**

```bash
cd "D:/playground/Programming/Wc3_CLI"
dotnet new classlib -n Wc3.Mcp -o src/Wc3.Mcp -f net8.0
rm src/Wc3.Mcp/Class1.cs
dotnet sln add src/Wc3.Mcp/Wc3.Mcp.csproj
dotnet add src/Wc3.Mcp reference src/Wc3.Commands
```

- [ ] **Step 2: Write the seam README**

```markdown
<!-- src/Wc3.Mcp/README.md -->
# Wc3.Mcp (seam only)

This project exists to prove the shared-command-layer seam: it references ONLY
`Wc3.Commands` (never `wc3ctl`). A future MCP server will expose each command in
`Wc3.Commands` as an MCP tool, mapping tool arguments to the same `Execute`
methods the CLI calls and returning the same result POCOs as JSON.

Not implemented in slice 1 by design (seam-only decision).
```

- [ ] **Step 3: Build to verify the seam holds**

Run: `dotnet build`
Expected: whole solution builds. If `Wc3.Mcp` needs any CLI type, the boundary has leaked — fix `Wc3.Commands`.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "chore: MCP seam stub proving shared command layer boundary"
```

---

## Task 14: Full-suite green + README

**Files:**
- Create: `README.md`
- Test: entire suite

**Interfaces:** none new.

- [ ] **Step 1: Run the full fast suite**

Run: `dotnet test --filter "Category!=Corpus"`
Expected: all fast tests PASS.

- [ ] **Step 2: Run the corpus suite**

Run: `dotnet test --filter "Category=Corpus"`
Expected: PASS (round-trip + parse coverage on the 63 MB map). Record any diagnosed-but-unparsed formats.

- [ ] **Step 3: Write the top-level README**

```markdown
# wc3ctl — Warcraft III map tool (slice 1: MapDocument Core)

Open a .w3x/.w3m, inspect it, and round-trip it byte-faithfully.

## Commands
- `wc3ctl ls <map>` — list internal files
- `wc3ctl info <map>` — map metadata
- `wc3ctl roundtrip <map>` — verify byte-faithful save
- `wc3ctl search <map> <query>` — search
- `wc3ctl diff <mapA> <mapB>` — compare
- `wc3ctl object get <map> <rawcode> [--field F]` — (stub; full object index later)

Add `--json` to any command for machine output.

## Build & test
    dotnet build
    dotnet test --filter "Category!=Corpus"   # fast
    dotnet test --filter "Category=Corpus"     # against a real map (opt-in)
```

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs: slice-1 README; full suite green"
```

---

## Self-Review

**Spec coverage:**
- Open .w3x/.w3m + pre-archive header → Tasks 2, 5. ✓
- Parse ALL known formats (read/query) → Tasks 6, 8. ✓
- Preserve unknown/unnamed raw, never drop → Task 5 (unknown), error handling in Task 7. ✓ (Unnamed-file handling relies on War3Net enumerating nameless entries; Task 5 Step 4 note flags verification.)
- Round-trip byte-faithful per file → Task 9 + corpus test. ✓
- Shared command layer → Tasks 10, 11; seam proven Task 13. ✓
- CLI read/query + roundtrip + --json → Task 12. ✓
- Graceful degradation / diagnostics → Task 7. ✓
- MCP seam-only → Task 13. ✓
- Test corpus (synthetic + real map) → Tasks 4, 8, 9. ✓
- .NET 8 SDK prereq → Task 1 Step 1. ✓

**Placeholder scan:** `ObjectGetCommand` intentionally returns `Found=false` (object indexing is explicitly a later slice, per spec §7 deferred list) — documented, not a placeholder. `SerializeEntry` throws by design (no editing this slice). No `TBD`/`TODO`.

**Type consistency:** `MapFileEntry`, `MapDocument.Load/SaveToBytes`, `MapFormatRegistry.{IsKnown,TryGetParser,Register,ReadWith}`, and the result records are used with consistent signatures across tasks.

**Known risk points (call out during execution, don't pre-solve):**
1. `(listfile)`/`(attributes)` may be regenerated by `MpqArchiveBuilder` → Task 9 Step 4 handles via inspect-and-decide.
2. Exact War3Net reader-extension names and `MapInfo` property names → enforced by compile + characterization tests (Tasks 8, 10).
3. `entry.FileName` null for named files if listfile absent → Task 5 Step 4 note.
4. `System.CommandLine` beta handler API drift → Task 12 note; CLI test enforces.
