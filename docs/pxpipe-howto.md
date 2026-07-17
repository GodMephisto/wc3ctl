# Using pxpipe with this repo

`pxpipe` (https://github.com/teamchong/pxpipe) is the local proxy Claude Code talks to
in this environment. This note captures how it works and the one gotcha that bit us, so
future sessions don't rediscover it the hard way.

## What it actually is

pxpipe is an **imaging proxy**, not a model router. For every model listed in
`PXPIPE_MODELS`, it **renders the conversation context into PNG images** before sending it
upstream, to cut token cost. Any model *not* on the list "passes through byte-identical."

That's why a session running through pxpipe receives its history as images rather than text.

## Configuration (env vars)

Set before launching Claude Code; pxpipe listens on `127.0.0.1:47821`.

| Var | Meaning |
|-----|---------|
| `ANTHROPIC_BASE_URL` | Points Claude Code at the proxy, e.g. `http://127.0.0.1:47821`. |
| `PXPIPE_MODELS` | **Allowlist** of model IDs to image (comma-separated). Default `claude-fable-5`. `off` disables imaging. Matching is by **exact** model ID — sibling variants are not auto-included. |
| `CLAUDE_CODE_SUBAGENT_MODEL` | Route subagents to a non-allowlisted model (e.g. `claude-sonnet-4-6`) so their traffic passes through as text instead of being imaged. |
| `PXPIPE_GPT_HISTORY_MAX_IMAGES` | History image budget on the GPT path (default 32). |
| `OPENAI_BASE_URL` | Upstream for Codex/Responses-provider models. |

Log of proxied requests: `~/.pxpipe/events.jsonl`.

> Note: `PXPIPE_GATEWAY_BASE_URL` / `PXPIPE_UPSTREAM` are referenced in some setups but are
> **not** in the published README — verify against `src/core/index.ts` before relying on them.

## The gotcha: imaging breaks the auto-mode permission classifier

Claude Code's auto-mode classifier (the thing that auto-approves/denies shell commands) is an
automated system that needs a **byte-exact text prompt and a parseable text reply**. It runs
on the background/"fast" model.

In this environment the fast model is `claude-opus-4-8`, and `claude-opus-4-8` was on the
`PXPIPE_MODELS` allowlist. So pxpipe **imaged the classifier's own prompt into a PNG**, the
classifier couldn't parse its I/O, and every command was blocked with:

```
Auto mode could not evaluate this action and is blocking it for safety
Stage 2 classifier error - blocking based on stage 1 assessment
```

It looks intermittent (retries "sometimes work") because occasionally a parse squeaks through.

### Fixes (pick one)

1. **Simplest / guaranteed** — drop opus from the imaging list:
   `PXPIPE_MODELS=claude-fable-5`
   Classifier passes through byte-identical and works. Cost: the main opus thread is no
   longer imaged (higher token use).

2. **Keep token savings AND fix the classifier** — leave `claude-opus-4-8` imaged for the main
   thread, but point Claude Code's **small/fast (background) model** at a model that is *not*
   on the allowlist (a real haiku/sonnet). Verify the exact small-fast-model setting/env key
   for your Claude Code build.

3. **No pxpipe change** — add explicit allow-rules in `.claude/settings.local.json` so those
   commands skip the classifier entirely:
   ```json
   { "permissions": { "allow": [ "Bash(git:*)", "PowerShell(git:*)", "Bash(dotnet:*)", "PowerShell(dotnet:*)" ] } }
   ```
   Both tool prefixes because commands may run through either the Bash or PowerShell tool.

**After changing pxpipe config, restart pxpipe (and Claude Code), then verify:** run a trivial
`git status` — it should auto-approve with no stage-2 error.

## Subagents / parallel work

Subagents execute their commands through the same classifier, so they are blocked by the same
issue until it's fixed. Once fixed, run parallel agents in **separate git worktrees** — multiple
agents building the same solution otherwise collide on `bin/obj` file locks and merge conflicts.
