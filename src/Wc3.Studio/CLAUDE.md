# Wc3.Studio (Avalonia GUI)

World-Editor-style shell over the shared command layer. **Reuse `Wc3.Commands`, never parse maps or reimplement logic in the UI** (Principle #3). Build reusable controls in `src/Wc3.Studio/Controls/`, do not hardcode or duplicate markup (see the root CLAUDE.md).

- **Avalonia 11.2.x** (do NOT scaffold 12. Its source generators need a newer Roslyn than the SDK ships, giving CS9057 / missing InitializeComponent).
- **Panel contract**: `IMapPanel.ShowMap(MapSession)`. `MapSession { MapDocument? Current, string? MapPath, string? GameDir }`. Panels rebuild from the session each call.
- **Lazy per-tab load**: the shell (`MainWindow`) calls `ShowMap` only on the tab you open, once per map. Do not move heavy work back to "load all panels on open."
- **Editing pitfall (fixed, do not reintroduce)**: do NOT put an editable `TextBox` inside a `ListBox`/virtualized item template. Recycling and focus clear the bound value ("clicking the box erases it"). Use read-only rows plus a single dedicated editor plus Apply.
- **Object editor**: the type switcher (7 `ObjectKind`s) runs `ObjectListCommand.Execute(doc, kind, gameDir)`, selecting runs `ObjectGetCommand`, editing runs `ObjectSetCommand` then `doc.Save(<map>.edited<ext>)` (never clobber the original). Write-back works for ALL 7 kinds and is byte-faithful. This used to be units-only, it no longer is.
- **Dev/QA**: `Wc3.Studio.exe --open <map> [--open-target <map>]` auto-loads maps on startup so the UI can be driven and screenshotted without the file dialog.
- **Publish**: `dist-studio/` locks `Wc3.Studio.dll` while the app is running, close it before `dotnet publish`. For quick iteration use `dotnet run --project src/Wc3.Studio` (separate output).
