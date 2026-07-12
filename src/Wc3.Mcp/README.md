# Wc3.Mcp (seam only)

This project exists to prove the shared-command-layer seam: it references ONLY
`Wc3.Commands` (never `wc3ctl`). A future MCP server will expose each command in
`Wc3.Commands` as an MCP tool, mapping tool arguments to the same `Execute`
methods the CLI calls and returning the same result POCOs as JSON.

Not implemented in slice 1 by design (seam-only decision).
