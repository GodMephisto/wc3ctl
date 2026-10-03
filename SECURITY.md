# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem. Report it privately
through GitHub instead, on the
[Security tab](https://github.com/GodMephisto/wc3ctl/security/advisories/new)
of this repository ("Report a vulnerability"). Only the maintainer can see it.

Include what you found, how to reproduce it (a small map or replay helps), and
which build you ran.

You should get a reply within a week. Once a fix is released, the advisory is
published and you are credited unless you ask not to be.

## Supported versions

Only the latest release gets fixes. Install it with the one-line installer in the
README, or download it from the Releases page.

## What counts

wc3ctl, the Studio and the MCP server read and write map files you point them
at, read your local Warcraft III install, and the MCP setup edits your AI app
configs and user PATH. Things worth reporting include

- a map, model or replay file that makes any of them write outside the path it
  was given, run code, or crash in a way that could be exploited
- setup changing a config file or PATH entry it should not touch
- a map edit that changes bytes of a file nobody asked to edit

A map that fails to load or a wrong value in a command's output is a normal bug,
so please open an issue for those.
