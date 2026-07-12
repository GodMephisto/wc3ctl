# Object-Data Merge via CASC — Implementation Plan (Slice 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** `wc3ctl object get <map> <rawcode>` shows a **unit's full merged fields** — base game data (read from the installed Reforged CASC) overlaid with the map's `war3map.w3u` deltas, with human field names; plus `object list`.

**Architecture:** New `Wc3.GameData` project reads base data from the WC3 install's CASC (via `CascLib.NET`, which bundles the native x64 `CascLib.dll`), parses the unit SLKs with a small built-in SLK parser, and resolves fields through `unitmetadata.slk`. `Wc3.Commands` merges base ⊕ map deltas; `wc3ctl` renders. Graceful degradation to deltas-only when the install is unreachable.

**Tech Stack:** C#/.NET 8, `CascLib.NET` 1.50.0.206-alpha.3 (bundles native CascLib.dll), existing War3Net stack.

## Global Constraints
- `net8.0`; nullable enabled; `<LangVersion>latest</LangVersion>`; namespaces: model `Wc3.Model`, commands `Wc3.Commands`, new layer `Wc3.GameData`, CLI `Wc3Ctl`.
- New dependency allowed: `CascLib.NET` (prerelease) in `Wc3.GameData` only. No other new deps.
- **`Wc3.GameData` must not reference `Wc3.Commands`, `wc3ctl`, or `Wc3.Model`** (it's pure game-install access). `Wc3.Commands` references `Wc3.Model` + `Wc3.GameData`.
- Never crash on a missing/unreadable install — degrade to deltas + a `Diagnostic`, exit 0 (reuse the CLI `RunSafely` pattern already in `src/wc3ctl/Program.cs`).
- TDD; commit per task. Integration tests that touch the real install use `[Trait("Category","GameData")]` and self-skip when `D:\Warcraft III` is absent, so `--filter "Category!=Corpus&Category!=GameData"` stays hermetic.

## Spike-verified facts (use verbatim)
- Package `CascLib.NET`, namespace `CascLib.NET`: `new CascStorage(string path)`; `bool TryOpenFile(string path, out Stream stream)`; `Stream OpenFile(string path)`; `CascStorage` is `IEnumerable` of `CascFindData { string FileName; string PlainName; ulong FileSize; ... }`; `int TotalFileCount`.
- Open path: `D:\Warcraft III` (product auto-detected as `w3`).
- Base file names (exact, note the `:` mod-layer syntax and `\` separators):
  - `war3.w3mod:units\unitmetadata.slk` — field metadata
  - `war3.w3mod:units\unitdata.slk`, `unitbalance.slk`, `unitweapons.slk`, `unitui.slk`, `unitabilities.slk` — base unit fields
- `unitmetadata.slk` columns (row 1 headers): `ID, field, slk, index, category, displayName, sort, type, ...`. `field` = 4-char code (matches w3u delta ids); `slk` = which data file the field lives in (e.g. `UnitData`, `UnitUI`, `UnitWeapons`, `UnitBalance`, `UnitAbilities`); `displayName` = a WESTRING key.
- Unit SLKs are keyed by rawcode in their **first column** (`unitID`).
- SLK text format: line records `ID;PWXL`, `B;X<cols>;Y<rows>`, `C;X<col>;Y<row>;K<value>` (Y omitted → reuse last), value `K"quoted"` or `K<number>`; `""` escapes a quote inside a string.

## File Structure
```
src/Wc3.GameData/            (new project; refs CascLib.NET only)
  Wc3.GameData.csproj
  SlkTable.cs                parse SLK bytes → cells[row][col]; header→index; row lookup by first-col key
  GameInstall.cs             locate WC3 install (registry/paths) + explicit override
  IGameDataSource.cs         byte[]? ReadFile(string name); IReadOnlyList<string> ListFiles(); (abstraction)
  CascGameDataSource.cs      CascLib.NET-backed IGameDataSource (open install, read by name)
  UnitMetadata.cs            field-code → { SlkName, Column, DisplayName } (from unitmetadata.slk)
  BaseUnitStore.cs           base rawcode → field-code→value (metadata + unit SLKs)
  GameData.cs                facade: TryOpenUnits(gameDir?, out store, out diagnostic)
src/Wc3.Commands/
  Results.cs                 + MergedField, MergedObjectResult, ObjectListResult, ObjectListItem
  ObjectGetCommand.cs        REPLACE stub: base ⊕ delta merge (+ IBaseUnitSource seam for testing)
  ObjectListCommand.cs       enumerate map custom units
src/wc3ctl/
  Program.cs                 wire `object get` args + `object list` + global --game-dir
  Render.cs                  + Render.ObjectGet(merged), Render.ObjectList
tests/Wc3.Tests/
  SlkTableTests.cs, GameInstallTests.cs, UnitMetadataTests.cs, ObjectCommandTests.cs  (hermetic)
  GameDataIntegrationTests.cs  ([Trait GameData]: CASC open + hfoo resolution)
```

---

## Task 1: Wc3.GameData project scaffold + CascLib.NET

**Files:** Create `src/Wc3.GameData/Wc3.GameData.csproj`; modify `Wc3.sln`; Test: `tests/Wc3.Tests/GameDataSmokeTest.cs`
**Interfaces:** Produces a buildable `Wc3.GameData` project referenced by `Wc3.Tests`, with `CascLib.NET` resolving.

- [ ] **Step 1: Create project + package + references**
```bash
cd "D:/playground/Programming/Wc3_CLI"
dotnet new classlib -n Wc3.GameData -o src/Wc3.GameData -f net8.0
rm src/Wc3.GameData/Class1.cs
dotnet sln add src/Wc3.GameData/Wc3.GameData.csproj
dotnet add src/Wc3.GameData package CascLib.NET --prerelease
dotnet add tests/Wc3.Tests reference src/Wc3.GameData
```
Set `<Nullable>enable</Nullable>` + `<LangVersion>latest</LangVersion>` in the new csproj.

- [ ] **Step 2: Smoke test**
```csharp
// tests/Wc3.Tests/GameDataSmokeTest.cs
namespace Wc3.Tests;
public class GameDataSmokeTest
{
    [Fact]
    public void CascLib_type_is_referencable()
        => Assert.Equal("CascStorage", typeof(CascLib.NET.CascStorage).Name);
}
```
- [ ] **Step 3:** `dotnet test --filter "Category!=Corpus&Category!=GameData"` → passes (smoke incl.).
- [ ] **Step 4:** Commit `feat(gamedata): scaffold Wc3.GameData with CascLib.NET`.

---

## Task 2: SlkTable — minimal SLK parser

**Files:** Create `src/Wc3.GameData/SlkTable.cs`; Test: `tests/Wc3.Tests/SlkTableTests.cs`
**Interfaces:** Produces
- `sealed class SlkTable` with `static SlkTable Parse(byte[] bytes)`,
- `IReadOnlyList<string> Headers` (row-1 values, lowercased),
- `bool TryGetRow(string firstColKey, out IReadOnlyDictionary<string,string> row)` — row keyed by header name (lowercased) → cell value, looked up by matching the first data column against `firstColKey` (case-insensitive).

- [ ] **Step 1: Failing test**
```csharp
// tests/Wc3.Tests/SlkTableTests.cs
using Wc3.GameData;
namespace Wc3.Tests;
public class SlkTableTests
{
    // 2 cols (unitID, name), header row 1, one data row: hfoo, "Footman"
    private const string Slk =
        "ID;PWXL;N;E\r\nB;X2;Y2;D0\r\nC;X1;Y1;K\"unitID\"\r\nC;X2;K\"name\"\r\nC;X1;Y2;K\"hfoo\"\r\nC;X2;K\"Footman\"\r\nE\r\n";

    [Fact]
    public void Parses_headers_and_row_by_first_column()
    {
        var t = SlkTable.Parse(System.Text.Encoding.ASCII.GetBytes(Slk));
        Assert.Contains("unitid", t.Headers);
        Assert.Contains("name", t.Headers);
        Assert.True(t.TryGetRow("hfoo", out var row));
        Assert.Equal("Footman", row["name"]);
    }

    [Fact]
    public void Unknown_key_returns_false()
    {
        var t = SlkTable.Parse(System.Text.Encoding.ASCII.GetBytes(Slk));
        Assert.False(t.TryGetRow("zzzz", out _));
    }
}
```
- [ ] **Step 2:** Run `dotnet test --filter SlkTableTests` → FAIL (no SlkTable).
- [ ] **Step 3: Implement**
```csharp
// src/Wc3.GameData/SlkTable.cs
using System.Text;
namespace Wc3.GameData;

public sealed class SlkTable
{
    private readonly List<string> _headers = new();
    private readonly Dictionary<string, Dictionary<string, string>> _rowsByKey = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Headers => _headers;

    public bool TryGetRow(string firstColKey, out IReadOnlyDictionary<string, string> row)
    {
        if (_rowsByKey.TryGetValue(firstColKey, out var r)) { row = r; return true; }
        row = new Dictionary<string, string>();
        return false;
    }

    public static SlkTable Parse(byte[] bytes)
    {
        var table = new SlkTable();
        // cells[row][col] (1-based indices as they appear in SLK)
        var cells = new Dictionary<int, Dictionary<int, string>>();
        int curX = 1, curY = 1, maxCol = 0;

        using var reader = new StringReader(Encoding.UTF8.GetString(bytes));
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            char rec = line[0];
            if (rec != 'C' && rec != 'F') continue;           // only cell + format carry X/Y/K
            string? k = null;
            foreach (var field in SplitSlk(line.Substring(1)))
            {
                if (field.Length < 1) continue;
                char tag = field[0];
                string rest = field.Substring(1);
                switch (tag)
                {
                    case 'X': if (int.TryParse(rest, out var x)) curX = x; break;
                    case 'Y': if (int.TryParse(rest, out var y)) curY = y; break;
                    case 'K': k = Unquote(rest); break;
                }
            }
            if (rec == 'C' && k != null)
            {
                if (!cells.TryGetValue(curY, out var rowMap)) cells[curY] = rowMap = new();
                rowMap[curX] = k;
                if (curX > maxCol) maxCol = curX;
            }
        }

        // Row 1 = headers.
        if (cells.TryGetValue(1, out var header))
            for (int c = 1; c <= maxCol; c++)
                table._headers.Add(header.TryGetValue(c, out var h) ? h.Trim().ToLowerInvariant() : $"col{c}");

        // Data rows keyed by first column's value.
        foreach (var (y, rowMap) in cells)
        {
            if (y == 1) continue;
            if (!rowMap.TryGetValue(1, out var key) || string.IsNullOrWhiteSpace(key)) continue;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 1; c <= table._headers.Count; c++)
                if (rowMap.TryGetValue(c, out var v)) dict[table._headers[c - 1]] = v;
            table._rowsByKey[key] = dict;
        }
        return table;
    }

    // SLK fields are ';'-separated; a ';;' inside a value is a literal ';'.
    private static IEnumerable<string> SplitSlk(string s)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ';')
            {
                if (i + 1 < s.Length && s[i + 1] == ';') { sb.Append(';'); i++; }
                else { parts.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(s[i]);
        }
        parts.Add(sb.ToString());
        return parts;
    }

    private static string Unquote(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v.Substring(1, v.Length - 2).Replace("\"\"", "\"");
        return v;
    }
}
```
- [ ] **Step 4:** Run `dotnet test --filter SlkTableTests` → PASS.
- [ ] **Step 5:** Commit `feat(gamedata): minimal SLK parser`.

---

## Task 3: GameInstall detection

**Files:** Create `src/Wc3.GameData/GameInstall.cs`; Test: `tests/Wc3.Tests/GameInstallTests.cs`
**Interfaces:** Produces `static class GameInstall` with `static string? Locate(string? overridePath = null)` — returns `overridePath` if it exists; else the WC3 install dir from registry (`HKCU\SOFTWARE\Blizzard Entertainment\Warcraft III` `InstallPath`, uninstall `InstallLocation`) or common paths (`D:\Warcraft III`, `C:\Program Files (x86)\Warcraft III`, `C:\Program Files\Warcraft III`); else `null`.

- [ ] **Step 1: Failing test** (override path is honored; missing → null)
```csharp
// tests/Wc3.Tests/GameInstallTests.cs
using Wc3.GameData;
namespace Wc3.Tests;
public class GameInstallTests
{
    [Fact] public void Override_that_exists_is_returned()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try { Assert.Equal(dir, GameInstall.Locate(dir)); }
        finally { Directory.Delete(dir); }
    }
    [Fact] public void Nonexistent_override_falls_through_or_null()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "no_such_wc3_" + System.Guid.NewGuid().ToString("N"));
        var result = GameInstall.Locate(bogus);
        Assert.NotEqual(bogus, result); // never returns a path that doesn't exist
    }
}
```
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: Implement** (registry read is Windows-only; guard with `OperatingSystem.IsWindows()`).
```csharp
// src/Wc3.GameData/GameInstall.cs
using Microsoft.Win32;
namespace Wc3.GameData;

public static class GameInstall
{
    public static string? Locate(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && Directory.Exists(overridePath))
            return overridePath;

        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, sub, val) in new[]
            {
                (RegistryHive.CurrentUser, @"SOFTWARE\Blizzard Entertainment\Warcraft III", "InstallPath"),
                (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Warcraft III", "InstallLocation"),
            })
            {
                try
                {
                    using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(sub);
                    if (key?.GetValue(val) is string p && Directory.Exists(p)) return p;
                }
                catch { /* ignore, fall through */ }
            }
        }
        foreach (var p in new[] { @"D:\Warcraft III", @"C:\Program Files (x86)\Warcraft III", @"C:\Program Files\Warcraft III" })
            if (Directory.Exists(p)) return p;
        return null;
    }
}
```
Add `<PackageReference Include="Microsoft.Win32.Registry" Version="5.0.0" />` **only if** the target framework doesn't already include it (net8.0 includes `Microsoft.Win32.Registry` in the Windows runtime; if the build errors on the using, add the package — note in the commit which was needed). This is the single allowed exception to the dependency rule (framework-adjacent registry access).
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(gamedata): locate WC3 install (registry/paths/override)`.

---

## Task 4: IGameDataSource + CascGameDataSource

**Files:** Create `src/Wc3.GameData/IGameDataSource.cs`, `src/Wc3.GameData/CascGameDataSource.cs`; Test: `tests/Wc3.Tests/GameDataIntegrationTests.cs`
**Interfaces:** Produces
- `interface IGameDataSource : IDisposable { byte[]? ReadFile(string name); }`
- `sealed class CascGameDataSource : IGameDataSource` with `static bool TryOpen(string installDir, out CascGameDataSource? source, out string? error)`, wrapping `CascLib.NET.CascStorage`; `ReadFile` uses `TryOpenFile` + copies the stream to a `byte[]` (null if absent).

- [ ] **Step 1: Integration test** (self-skips without the install)
```csharp
// tests/Wc3.Tests/GameDataIntegrationTests.cs
using Wc3.GameData;
namespace Wc3.Tests;
public class GameDataIntegrationTests
{
    private const string Install = @"D:\Warcraft III";

    [Fact]
    [Trait("Category", "GameData")]
    public void Opens_casc_and_reads_unit_metadata()
    {
        if (!Directory.Exists(Install)) return;
        Assert.True(CascGameDataSource.TryOpen(Install, out var src, out var err), err);
        using (src)
        {
            var bytes = src!.ReadFile(@"war3.w3mod:units\unitmetadata.slk");
            Assert.NotNull(bytes);
            Assert.True(bytes!.Length > 1000);
        }
    }
}
```
- [ ] **Step 2:** Run `dotnet test --filter "Category=GameData"` → FAIL (types missing).
- [ ] **Step 3: Implement**
```csharp
// src/Wc3.GameData/IGameDataSource.cs
namespace Wc3.GameData;
public interface IGameDataSource : IDisposable
{
    byte[]? ReadFile(string name);
}
```
```csharp
// src/Wc3.GameData/CascGameDataSource.cs
using CascLib.NET;
namespace Wc3.GameData;

public sealed class CascGameDataSource : IGameDataSource
{
    private readonly CascStorage _storage;
    private CascGameDataSource(CascStorage storage) => _storage = storage;

    public static bool TryOpen(string installDir, out CascGameDataSource? source, out string? error)
    {
        source = null; error = null;
        try { source = new CascGameDataSource(new CascStorage(installDir)); return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public byte[]? ReadFile(string name)
    {
        if (!_storage.TryOpenFile(name, out var stream) || stream is null) return null;
        using (stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    public void Dispose() => _storage.Dispose();
}
```
- [ ] **Step 4:** Run `dotnet test --filter "Category=GameData"` → PASS on this machine (or skips if `D:\Warcraft III` absent — run it here where the install exists). If `CascStorage` throws on open, capture the exact message in the test output and STOP (spike proved it opens; a failure here means an environment change).
- [ ] **Step 5:** Commit `feat(gamedata): CASC-backed game data source`.

---

## Task 5: UnitMetadata — field-code → {slk, column, displayName}

**Files:** Create `src/Wc3.GameData/UnitMetadata.cs`; Test: `tests/Wc3.Tests/UnitMetadataTests.cs`
**Interfaces:** Produces
- `sealed record UnitFieldMeta(string Code, string SlkName, string Column, string DisplayName)`
- `sealed class UnitMetadata` with `static UnitMetadata FromSlk(SlkTable metaTable)` and `IReadOnlyList<UnitFieldMeta> Fields`, `bool TryGet(string code, out UnitFieldMeta meta)`.
Built by iterating metadata rows (keyed by `ID`), reading columns `field`, `slk`, `index`(ignored for v1), `displayname`.

- [ ] **Step 1: Failing test** (feed a tiny metadata SLK)
```csharp
// tests/Wc3.Tests/UnitMetadataTests.cs
using Wc3.GameData;
namespace Wc3.Tests;
public class UnitMetadataTests
{
    // headers: ID, field, slk, displayName ; two field rows
    private const string Meta =
        "ID;PWXL\r\nC;X1;Y1;K\"ID\"\r\nC;X2;K\"field\"\r\nC;X3;K\"slk\"\r\nC;X4;K\"displayName\"\r\n" +
        "C;X1;Y2;K\"uhpm\"\r\nC;X2;K\"unitBalance\"\r\nC;X3;K\"UnitBalance\"\r\nC;X4;K\"WESTRING_HP\"\r\n" +
        "C;X1;Y3;K\"unam\"\r\nC;X2;K\"name\"\r\nC;X3;K\"UnitUI\"\r\nC;X4;K\"WESTRING_NAME\"\r\nE\r\n";

    [Fact]
    public void Maps_field_code_to_slk_column_and_display()
    {
        var m = UnitMetadata.FromSlk(SlkTable.Parse(System.Text.Encoding.ASCII.GetBytes(Meta)));
        Assert.True(m.TryGet("uhpm", out var hp));
        Assert.Equal("UnitBalance", hp.SlkName);
        Assert.Equal("name", (m.TryGet("unam", out var n) ? n.Column : ""));
        Assert.Equal("WESTRING_HP", hp.DisplayName);
    }
}
```
Note: in the real `unitmetadata.slk` the row key is the metadata `ID` column and the 4-char code is the `field` column. Confirm against the real file in the integration test of Task 6; if the real column that holds the w3u-matching 4-char code is named differently than `field`, adjust here (the test above pins the shape we rely on).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: Implement**
```csharp
// src/Wc3.GameData/UnitMetadata.cs
namespace Wc3.GameData;

public sealed record UnitFieldMeta(string Code, string SlkName, string Column, string DisplayName);

public sealed class UnitMetadata
{
    private readonly Dictionary<string, UnitFieldMeta> _byCode = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<UnitFieldMeta> Fields => _byCode.Values.ToList();
    public bool TryGet(string code, out UnitFieldMeta meta) => _byCode.TryGetValue(code, out meta!);

    public static UnitMetadata FromSlk(SlkTable meta)
    {
        var m = new UnitMetadata();
        // The metadata table's first column is "ID"; we enumerate every data row via its ID.
        foreach (var id in EnumerateIds(meta))
        {
            if (!meta.TryGetRow(id, out var row)) continue;
            string code = row.TryGetValue("field", out var f) ? f : id;
            string slk = row.TryGetValue("slk", out var s) ? s : "";
            string col = code;                                   // the SLK column that holds this field
            string disp = row.TryGetValue("displayname", out var d) ? d : code;
            if (!string.IsNullOrWhiteSpace(code))
                m._byCode[code] = new UnitFieldMeta(code, slk, col, disp);
        }
        return m;
    }

    // SlkTable indexes rows by first-column value; expose them via a helper on SlkTable.
    private static IEnumerable<string> EnumerateIds(SlkTable meta) => meta.RowKeys;
}
```
This needs `SlkTable.RowKeys`. Add to `SlkTable` (Task 2 file): `public IEnumerable<string> RowKeys => _rowsByKey.Keys;` — include this addition in THIS task's commit (small, and Task 2's tests still pass).

Note on `Column`: for v1 the SLK column that stores a field's value is the **field code itself** in the data SLKs' header? NO — confirm in Task 6. In the real unit SLKs the column headers are human names (e.g. `name`, `HP`), and metadata's `field`→`column` mapping is what bridges them. If the data SLK is keyed by the metadata `field` value directly, `Column = code`; otherwise `Column` must come from a metadata column naming the SLK field. Task 6's `hfoo` integration test is the arbiter — adjust `col` here to the metadata column that names the data-SLK column (candidates: a `field`/`name`/`column` header in unitmetadata.slk). Keep the record shape; only the source column may change.
- [ ] **Step 4:** Run → PASS.
- [ ] **Step 5:** Commit `feat(gamedata): unit field metadata parser`.

---

## Task 6: BaseUnitStore — resolve a base rawcode → fields

**Files:** Create `src/Wc3.GameData/BaseUnitStore.cs`; Test: extend `tests/Wc3.Tests/GameDataIntegrationTests.cs` + a hermetic unit test in `tests/Wc3.Tests/BaseUnitStoreTests.cs`
**Interfaces:** Produces
- `sealed class BaseUnitStore` with `static BaseUnitStore Build(IGameDataSource src)` (reads metadata + the 5 unit SLKs) and `bool TryGetUnit(string rawcode, out IReadOnlyDictionary<string,string> fieldsByCode)` — returns each metadata field-code → the base value for that unit (empty if the unit isn't a base unit).

**Consumes:** `IGameDataSource` (Task 4), `SlkTable` (Task 2), `UnitMetadata` (Task 5).

- [ ] **Step 1: Hermetic unit test** with a fake `IGameDataSource` returning tiny SLKs (metadata + one data SLK), assert a base rawcode resolves the field.
```csharp
// tests/Wc3.Tests/BaseUnitStoreTests.cs
using Wc3.GameData;
namespace Wc3.Tests;
public class BaseUnitStoreTests
{
    private sealed class FakeSource : IGameDataSource
    {
        private readonly Dictionary<string, byte[]> _f;
        public FakeSource(Dictionary<string, byte[]> f) => _f = f;
        public byte[]? ReadFile(string name) => _f.TryGetValue(name, out var b) ? b : null;
        public void Dispose() { }
    }
    private static byte[] A(string s) => System.Text.Encoding.ASCII.GetBytes(s);

    [Fact]
    public void Resolves_base_unit_field_via_metadata()
    {
        // metadata: field "unam" lives in slk "UnitUI", column named "name"
        var meta = "ID;PWXL\r\nC;X1;Y1;K\"ID\"\r\nC;X2;K\"field\"\r\nC;X3;K\"slk\"\r\nC;X4;K\"column\"\r\nC;X5;K\"displayName\"\r\n" +
                   "C;X1;Y2;K\"unam\"\r\nC;X2;K\"unam\"\r\nC;X3;K\"UnitUI\"\r\nC;X4;K\"name\"\r\nC;X5;K\"WESTRING_NAME\"\r\nE\r\n";
        // UnitUI.slk: unitID + name, row hfoo=Footman
        var ui = "ID;PWXL\r\nC;X1;Y1;K\"unitID\"\r\nC;X2;K\"name\"\r\nC;X1;Y2;K\"hfoo\"\r\nC;X2;K\"Footman\"\r\nE\r\n";
        var src = new FakeSource(new()
        {
            [@"war3.w3mod:units\unitmetadata.slk"] = A(meta),
            [@"war3.w3mod:units\unitui.slk"] = A(ui),
        });
        var store = BaseUnitStore.Build(src);
        Assert.True(store.TryGetUnit("hfoo", out var fields));
        Assert.Equal("Footman", fields["unam"]);
    }
}
```
This test pins the resolution contract: metadata maps `field`→(`slk`,`column`); the data SLK (named by `slk`, lowercased + `.slk`) is keyed by `unitID`; the value is read from `column`.
- [ ] **Step 2:** Run `dotnet test --filter BaseUnitStoreTests` → FAIL.
- [ ] **Step 3: Implement**
```csharp
// src/Wc3.GameData/BaseUnitStore.cs
namespace Wc3.GameData;

public sealed class BaseUnitStore
{
    private const string Dir = @"war3.w3mod:units\";
    private readonly UnitMetadata _meta;
    private readonly Dictionary<string, SlkTable> _slks; // slk-name (lower) -> table

    private BaseUnitStore(UnitMetadata meta, Dictionary<string, SlkTable> slks) { _meta = meta; _slks = slks; }

    public static BaseUnitStore Build(IGameDataSource src)
    {
        var metaBytes = src.ReadFile(Dir + "unitmetadata.slk")
            ?? throw new InvalidDataException("unitmetadata.slk not found in game data");
        var meta = UnitMetadata.FromSlk(SlkTable.Parse(metaBytes));

        var slks = new Dictionary<string, SlkTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "unitdata", "unitbalance", "unitweapons", "unitui", "unitabilities" })
        {
            var b = src.ReadFile(Dir + name + ".slk");
            if (b != null) slks[name] = SlkTable.Parse(b);
        }
        return new BaseUnitStore(meta, slks);
    }

    public bool TryGetUnit(string rawcode, out IReadOnlyDictionary<string, string> fieldsByCode)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fm in _meta.Fields)
        {
            if (!_slks.TryGetValue(fm.SlkName, out var table)) continue;
            if (table.TryGetRow(rawcode, out var row) && row.TryGetValue(fm.Column, out var val) && val.Length > 0)
                result[fm.Code] = val;
        }
        fieldsByCode = result;
        return result.Count > 0;
    }
}
```
- [ ] **Step 4:** Run `dotnet test --filter BaseUnitStoreTests` → PASS.
- [ ] **Step 5: Real-data integration test** — append to `GameDataIntegrationTests.cs`:
```csharp
    [Fact]
    [Trait("Category", "GameData")]
    public void Resolves_footman_base_stats()
    {
        if (!Directory.Exists(Install)) return;
        Assert.True(CascGameDataSource.TryOpen(Install, out var src, out var err), err);
        using (src)
        {
            var store = BaseUnitStore.Build(src!);
            Assert.True(store.TryGetUnit("hfoo", out var fields), "Footman hfoo should resolve");
            Assert.NotEmpty(fields);
        }
    }
```
Run `dotnet test --filter "Category=GameData"`. Expected: PASS. **If `hfoo` resolves 0 fields**, the metadata `slk`/`column` mapping differs from the assumption — inspect `unitmetadata.slk` rows (print a few in the test) to find the real column that names the data-SLK column, fix `UnitMetadata.FromSlk`'s `col`/`slk` source, re-run. Record the real column names in the commit message. (This is the one place reality must be confirmed; the spike proved the files are readable.)
- [ ] **Step 6:** Commit `feat(gamedata): resolve base unit fields from SLKs`.

---

## Task 7: GameData facade with graceful open

**Files:** Create `src/Wc3.GameData/GameData.cs`; Test: covered via command tests (Task 8).
**Interfaces:** Produces `static class GameData` with
`static bool TryOpenUnits(string? gameDirOverride, out BaseUnitStore? store, out string diagnostic)` — locates install (Task 3), opens CASC (Task 4), builds the store (Task 6); on any failure sets `store=null` and a human diagnostic ("Warcraft III install not found; pass --game-dir" / the CASC error), returning false. Never throws.

- [ ] **Step 1: Implement** (no separate test; exercised by Task 8's degradation test)
```csharp
// src/Wc3.GameData/GameData.cs
namespace Wc3.GameData;

public static class GameData
{
    public static bool TryOpenUnits(string? gameDirOverride, out BaseUnitStore? store, out string diagnostic)
    {
        store = null; diagnostic = "";
        var dir = GameInstall.Locate(gameDirOverride);
        if (dir is null) { diagnostic = "Warcraft III install not found (pass --game-dir <path>)"; return false; }
        if (!CascGameDataSource.TryOpen(dir, out var src, out var err))
        { diagnostic = $"could not open game data at {dir}: {err}"; return false; }
        try { store = BaseUnitStore.Build(src!); return true; }
        catch (Exception ex) { diagnostic = $"could not read base unit data: {ex.Message}"; src!.Dispose(); return false; }
    }
}
```
Note: `BaseUnitStore` should keep the `IGameDataSource` alive only during `Build` (it copies SLK bytes into `SlkTable`s), so disposing `src` after `Build` is safe — verify `Build` fully reads before returning (it does). Adjust to dispose `src` after a successful `Build` too (add `finally { src.Dispose(); }` around the build).
- [ ] **Step 2:** `dotnet build` → succeeds.
- [ ] **Step 3:** Commit `feat(gamedata): graceful units facade`.

---

## Task 8: ObjectGetCommand — merge base ⊕ deltas

**Files:** Modify `src/Wc3.Commands/ObjectGetCommand.cs` (replace stub), `src/Wc3.Commands/Results.cs`; add `dotnet add src/Wc3.Commands reference src/Wc3.GameData`; Test: `tests/Wc3.Tests/ObjectCommandTests.cs`
**Interfaces:** Produces
- `sealed record MergedField(string Code, string Name, string Value, string Source)` (Source ∈ `base|map`)
- `sealed record MergedObjectResult(string Rawcode, bool Found, string? BaseRawcode, IReadOnlyList<MergedField> Fields, IReadOnlyList<string> Diagnostics)`
- `static MergedObjectResult ObjectGetCommand.Execute(MapDocument doc, string rawcode, string? gameDirOverride)`

**CONFIRM by reflection (like Slice 1 Task 8):** the War3Net `war3map.w3u` model shape. `doc.GetFile("war3map.w3u")?.Model` is a War3Net object-data type. Determine how to enumerate its custom units and, per unit, get: the **new** rawcode, the **base/old** rawcode, and the modified fields (each a 4-char id + typed value). Write a tiny reflection probe first (temp test that Console.WriteLines the type + members), then code against the real shape. Likely `MapUnitObjectData.BaseUnits` / `NewUnits` with `OldId`/`NewId` and `Modifications` (`Id`, `Value`). Record the real names in the report.

- [ ] **Step 1: Hermetic test** with an injectable base source so no install is needed. Add an overload `Execute(MapDocument doc, string rawcode, Func<string,IReadOnlyDictionary<string,string>?> baseLookup, IReadOnlyList<string> preDiagnostics)` that the public `Execute` calls after opening `GameData`. Test the merge:
```csharp
// tests/Wc3.Tests/ObjectCommandTests.cs
using Wc3.Commands;
using Wc3.Model;
namespace Wc3.Tests;
public class ObjectCommandTests
{
    [Fact]
    public void Merges_base_and_delta_with_source_labels()
    {
        // No real map needed for the merge core: call the seam overload with a fake map-delta + base.
        var baseFields = new Dictionary<string,string> { ["unam"]="Footman", ["uhpm"]="420" };
        var result = ObjectGetCommand.Merge(
            rawcode: "H001", baseRawcode: "hfoo",
            baseFields: baseFields,
            deltaFields: new Dictionary<string,string> { ["uhpm"]="999" },
            nameLookup: code => code=="unam" ? "Name" : code=="uhpm" ? "Hit Points" : code,
            diagnostics: System.Array.Empty<string>());
        Assert.True(result.Found);
        Assert.Equal("hfoo", result.BaseRawcode);
        var hp = result.Fields.Single(f => f.Code=="uhpm");
        Assert.Equal("999", hp.Value);
        Assert.Equal("map", hp.Source);
        Assert.Equal("base", result.Fields.Single(f => f.Code=="unam").Source);
    }
}
```
Expose `ObjectGetCommand.Merge(...)` as an internal-testable static (pure): base fields overlaid by deltas, each labeled, names via `nameLookup`, sorted by name.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3: Implement** `Merge` (pure) + the public `Execute` that: loads the map's w3u deltas for `rawcode` (base rawcode + delta field map — via the reflection-confirmed War3Net shape), opens `GameData.TryOpenUnits(gameDirOverride, …)`, resolves base fields (`store.TryGetUnit(baseRawcode)`), builds a `nameLookup` from `store`'s metadata displayName (fallback to code), and calls `Merge`. On `TryOpenUnits==false`: return deltas-only (Source=`map`) with the diagnostic; `Found=true` if the map defines the rawcode. On rawcode not in map and no base: `Found=false`.
Show the full `Merge` code and the `Execute` body using the confirmed w3u member names. (Keep the old `ObjectGetResult` type or replace its usage in the CLI — update Render accordingly in Task 9.)
- [ ] **Step 4:** Run `dotnet test --filter ObjectCommandTests` → PASS.
- [ ] **Step 5:** Commit `feat(commands): object get merges base game data with map deltas`.

---

## Task 9: ObjectListCommand + CLI wiring

**Files:** Create `src/Wc3.Commands/ObjectListCommand.cs`; modify `src/wc3ctl/Program.cs`, `src/wc3ctl/Render.cs`; Test: extend `tests/Wc3.Tests/CliTests.cs`
**Interfaces:** Produces `ObjectListResult`/`ObjectListItem(string Rawcode, string? BaseRawcode)` and `ObjectListCommand.Execute(MapDocument doc)` enumerating the map's custom units (from the w3u model). CLI: real `object get <map> <rawcode> [--field X] [--game-dir P]` (render `MergedObjectResult` as a table: `NAME (code)  VALUE  [src]`), `object list <map> [--game-dir P]`, both honoring `--json`; add a global `--game-dir` option.

- [ ] **Step 1:** Failing CLI test: build a synthetic map with a `war3map.w3u`? (Hard to synthesize binary w3u.) Instead test `object list` returns empty cleanly on a map with no w3u, and that `object get` on a map with no install degrades (exit 0, message mentions deltas/'not found'). Keep it hermetic:
```csharp
// append to CliTests.cs
[Fact]
public async Task Object_get_unknown_rawcode_is_clean_exit0()
{
    var map = SyntheticMap.Build(new Dictionary<string, byte[]> { ["war3map.j"] = new byte[]{1} });
    var path = Path.Combine(Path.GetTempPath(), $"wc3ctl_og_{System.Guid.NewGuid():N}.w3x");
    File.WriteAllBytes(path, map);
    try {
        var sw = new StringWriter(); var o = System.Console.Out; System.Console.SetOut(sw);
        int code = await Wc3Ctl.Program.Main(new[]{ "object","get", path, "H999", "--game-dir", "Z:\\no_such" });
        System.Console.SetOut(o);
        Assert.Equal(0, code);
    } finally { File.Delete(path); }
}
```
- [ ] **Step 2:** Run → FAIL/После wiring compile errors.
- [ ] **Step 3:** Implement `ObjectListCommand` + rewire the `object get` subcommand (it currently calls the stub) to the new `Execute` with `--game-dir`; add `object list`; extend `Render`. Follow the existing System.CommandLine + `RunSafely` patterns already in `Program.cs` (use the `ctx`/`ParseResult` handler style used by the `extract` command). Show the full new/changed handler + Render methods.
- [ ] **Step 4:** Run `dotnet test --filter "Category!=Corpus&Category!=GameData"` → PASS.
- [ ] **Step 5: Manual real check** (this is the payoff — needs the install + a map with custom units):
```bash
# find a custom unit rawcode first via object list, then get it
dotnet run --project src/wc3ctl -- object list "C:/Users/GodMephisto/Documents/Warcraft III/Maps/Download/Anime_WOS2_0.25c1.w3x" | head
dotnet run --project src/wc3ctl -- object get "C:/Users/GodMephisto/Documents/Warcraft III/Maps/Download/Anime_WOS2_0.25c1.w3x" <rawcode-from-list>
```
Confirm merged fields print with base ⊕ delta. Paste output in the report.
- [ ] **Step 6:** Commit `feat(cli): object get (merged) + object list with --game-dir`.

---

## Task 10: Native-DLL publish check + README + full green

**Files:** Modify `src/wc3ctl/wc3ctl.csproj` (if needed), `README.md`; verify publish.
**Interfaces:** none.

- [ ] **Step 1:** Publish and verify the native CascLib.dll ships with the single-file exe:
```bash
dotnet publish src/wc3ctl -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
./dist/wc3ctl.exe object get "C:/Users/GodMephisto/Documents/Warcraft III/Maps/Download/Anime_WOS2_0.25c1.w3x" <rawcode>
```
If it fails with a CascLib.dll load error, add `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>` to `wc3ctl.csproj`'s PropertyGroup, republish, retest. Record which was needed.
- [ ] **Step 2:** Run full suites: `dotnet test --filter "Category!=Corpus&Category!=GameData"` (hermetic, all pass); `dotnet test --filter "Category=GameData"` (install-backed, pass here); `dotnet test --filter "Category=Corpus"` (unchanged, pass).
- [ ] **Step 3:** Update `README.md`: document `object get`/`object list`, the `--game-dir` override, that full stats require an installed WC3 (else deltas-only with a note), and the CascLib.NET native dependency.
- [ ] **Step 4:** Commit `docs: object get/list + gamedata; full suite green`.

---

## Self-Review
**Spec coverage:** CASC read (T1,4) ✓; SLK parse (T2) ✓; install detect (T3) ✓; field metadata (T5) ✓; base resolve (T6) ✓; graceful degrade (T7,8) ✓; merge base⊕delta + names (T8) ✓; object list (T9) ✓; CLI + --game-dir + --json (T9) ✓; native-dll distribution (T10) ✓; seam (Wc3.GameData no Commands/CLI dep — T1 refs, enforced) ✓; hfoo success criterion (T6) ✓.
**Placeholder scan:** the two "confirm against reality" points (T5/T6 metadata column mapping; T8 War3Net w3u shape) carry the exact test that arbitrates them + the concrete fallback — these are characterization steps, not placeholders. No TBD/TODO.
**Type consistency:** `IGameDataSource.ReadFile`, `SlkTable.{Parse,TryGetRow,RowKeys,Headers}`, `UnitMetadata.{FromSlk,TryGet,Fields}`, `UnitFieldMeta{Code,SlkName,Column,DisplayName}`, `BaseUnitStore.{Build,TryGetUnit}`, `GameData.TryOpenUnits`, `MergedField`/`MergedObjectResult`, `ObjectGetCommand.{Execute,Merge}` are used consistently across tasks.
**Known-reality checkpoints (call out during execution):** (1) unitmetadata.slk's real column that names the data-SLK column (T6 `hfoo` test arbitrates); (2) War3Net w3u model member names (T8 reflection); (3) single-file native-dll extraction (T10).
