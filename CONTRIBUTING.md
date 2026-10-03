# Contributing

Thanks for helping. Bug reports, map files that break something, and pull
requests are all welcome.

## Reporting a bug

Open an issue with the bug template. The most useful thing you can attach is
the map (or replay) that shows the problem, plus the command you ran and what
it printed. Security problems go through [SECURITY.md](SECURITY.md) instead.

## Building and testing

You need Windows and the .NET 8 SDK.

```powershell
git clone https://github.com/GodMephisto/wc3ctl
cd wc3ctl
dotnet build
dotnet test --filter "Category!=Corpus&Category!=GameData"
```

Tests marked `GameData` need a Warcraft III install, and `Corpus` tests need
real maps in your Documents\Warcraft III\Maps folder (or set `WC3_CORPUS_DIR`).
CI runs only the hermetic set, so run the others yourself if your change
touches game data or map parsing.

## How the code is laid out

Map logic lives in `Wc3.Commands`, which returns plain result objects. The CLI
(`src/wc3ctl`), the Studio (`src/Wc3.Studio`) and the MCP server
(`src/Wc3.Mcp`) only parse input and render those results, so a fix in a
command reaches all three. Never re-implement map logic in a front-end.

The MCP server and the libraries under it are also published on their own as
[wc3-mcp](https://github.com/GodMephisto/wc3-mcp). That repository's `src/` and
`tests/` are copied from here by `scripts/export-wc3-mcp.py`, so code changes for
either belong here.

## Pull requests

- Keep untouched files byte for byte. A map that is opened and saved without
  edits must come out identical, apart from the MPQ bookkeeping files.
- Add a test that fails without your change.
- A new MCP tool needs a CLI command (or an entry in the CLI-only list with
  the reason) and a smoke test in `McpToolSmokeTests`, or the parity and
  coverage tests fail.
- Do not bump the pinned dependency versions. Each pin has a reason, see the
  project files.
- Commit messages follow Conventional Commits, for example
  `fix(cameras): handle a map with no w3c file`.

## License of contributions

By submitting a contribution you agree it is licensed under the Apache License
2.0, the same as the rest of the project (section 5 of the LICENSE).
