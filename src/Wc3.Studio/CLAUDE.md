# Wc3.Studio — Avalonia GUI

World-Editor-style shell over the shared command layer. **Reuse `Wc3.Commands` — never parse maps or reimplement logic in the UI** (Principle #3).

- **Avalonia 11.2.x** (do NOT scaffold 12 — its source generators need a newer Roslyn than SDK 8.0.422 → CS9057/missing InitializeComponent).
- **Panel contract**: `IMapPanel.ShowMap(MapSession)`. `MapSession { MapDocument? Current; string? MapPath; string? GameDir }`. Panels rebuild from the session each call.
- **Lazy per-tab load**: the shell (`MainWindow`) calls `ShowMap` only on the tab you open, once per map — don't move heavy work back to "load all panels on open."
- **Editing pitfall (fixed — don't reintroduce)**: do NOT put an editable `TextBox` inside a `ListBox`/virtualized item template — recycling/focus clears the bound value ("clicking the box erases it"). Use read-only rows + a single dedicated editor + Apply.
- **Object editor**: type switcher (7 `ObjectKind`s) → `ObjectListCommand.Execute(doc, kind, gameDir)`; select → `ObjectGetCommand`; edit (Units only for now — write-back is units-only) → `ObjectSetCommand` → `doc.Save(<map>.edited<ext>)` (never clobber the original).
- **Publish**: `dist-studio/` locks `Wc3.Studio.dll` while the app is running — close it before `dotnet publish`. For quick iteration use `dotnet run --project src/Wc3.Studio` (separate output).
