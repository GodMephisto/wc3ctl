"""Export the MCP server and the libraries under it from this repository into a wc3-mcp checkout.

wc3ctl is where the code is written. The public wc3-mcp repository carries the same server as its
own product (exe wc3-mcp, registered in AI apps as "wc3"), so its source is copied from here rather
than edited there. Run this after committing here, then build, test and commit in the wc3-mcp
checkout.

    python scripts/export-wc3-mcp.py <path to wc3-mcp checkout>            dry run, prints the plan
    python scripts/export-wc3-mcp.py <path to wc3-mcp checkout> --apply    writes the files

What it does
  * Takes the files from this repository's HEAD commit, never from the working tree, so what is
    exported is exactly a commit you can name. It refuses when the exported paths have uncommitted
    changes, because those would silently be left out.
  * Mirrors the six projects in EXPORTED_PROJECTS, removing files the checkout has there and HEAD
    does not. Paths in TARGET_OWNED stay as the checkout has them.
  * Copies tests/Wc3.Tests except the files that use the CLI (the Wc3Ctl namespace), which cannot
    build without it, and drops the CLI project reference from the test project.
  * Leaves every other file in the checkout alone. Its README, installer, workflows, bundle
    manifest, registry entry, licenses and Directory.Build.props (the wc3-mcp identity) are its own.
  * Refuses to write anything that contains a Windows user profile path, or the git user name or
    email configured here, so a machine path or a private identity cannot reach the public repo.
"""
import io
import pathlib
import re
import subprocess
import sys
import tarfile

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

EXPORTED_PROJECTS = [
    "src/Wc3.MapDocument", "src/Wc3.GameData", "src/Wc3.Modeling",
    "src/Wc3.Render", "src/Wc3.Commands", "src/Wc3.Mcp",
]
TESTS = "tests/Wc3.Tests"
TARGET_OWNED = {"src/Wc3.Mcp/README.md"}
CLI_USE = re.compile(r"\bWc3Ctl\b")
CLI_REFERENCE = re.compile(r"^\s*<ProjectReference Include=\"[^\"]*wc3ctl\.csproj\" />\s*\r?\n", re.M)
PROFILE_PATH = re.compile(r"(?i)\b[a-z]:[\\/]+users[\\/]+(?!me\b|bob\b|<)[^\\/\s\"']+")

SOURCE = pathlib.Path(__file__).resolve().parent.parent


def git(*args, cwd=SOURCE):
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, check=True).stdout


def head_files(paths):
    """Path to bytes for every file under paths in HEAD."""
    raw = git("archive", "--format=tar", "HEAD", *paths)
    with tarfile.open(fileobj=io.BytesIO(raw)) as tar:
        return {m.name: tar.extractfile(m).read() for m in tar.getmembers() if m.isfile()}


def same(a, b):
    return a.replace(b"\r\n", b"\n") == b.replace(b"\r\n", b"\n")


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    apply = "--apply" in sys.argv
    if len(args) != 1:
        print(__doc__)
        return 2
    target = pathlib.Path(args[0]).resolve()
    if not (target / ".git").exists() or not (target / "src" / "Wc3.Mcp").exists():
        print(f"{target} is not a wc3-mcp checkout")
        return 2

    dirty = git("status", "--porcelain", "--", *EXPORTED_PROJECTS, TESTS).decode().strip()
    if dirty:
        print("These exported paths have uncommitted changes, commit them first:\n" + dirty)
        return 1

    files = head_files(EXPORTED_PROJECTS + [TESTS])
    excluded = sorted(p for p, b in files.items()
                      if p.startswith(TESTS + "/") and p.endswith(".cs") and CLI_USE.search(b.decode("utf-8", "replace")))
    for p in excluded:
        del files[p]
    project = TESTS + "/Wc3.Tests.csproj"
    files[project] = CLI_REFERENCE.sub("", files[project].decode("utf-8")).encode("utf-8")
    for p in TARGET_OWNED:
        files.pop(p, None)

    name = git("config", "user.name").decode().strip()
    email = git("config", "user.email").decode().strip()
    private = [s for s in (name, email) if len(s) >= 3]
    leaks = []
    for p, b in files.items():
        text = b.decode("utf-8", "replace")
        for i, line in enumerate(text.splitlines(), 1):
            if PROFILE_PATH.search(line) or any(s.lower() in line.lower() for s in private):
                leaks.append(f"  {p}:{i}")
    if leaks:
        print("Refusing to export. These lines carry a user profile path or the local git identity:")
        print("\n".join(leaks[:50]))
        return 1

    tracked = set(git("ls-files", "--", *EXPORTED_PROJECTS, TESTS, cwd=target).decode().splitlines())
    added = sorted(p for p in files if not (target / p).exists())
    changed = sorted(p for p in files if (target / p).exists() and not same((target / p).read_bytes(), files[p]))
    removed = sorted(p for p in tracked - set(files) if p not in TARGET_OWNED)

    commit = git("rev-parse", "--short", "HEAD").decode().strip()
    print(f"wc3ctl {commit} into {target}")
    print(f"  {len(added)} added, {len(changed)} changed, {len(removed)} removed, {len(files)} exported")
    print(f"  tests left out because they use the CLI: {', '.join(pathlib.Path(p).name for p in excluded) or 'none'}")
    for label, group in (("add", added), ("change", changed), ("remove", removed)):
        for p in group:
            print(f"  {label:7}{p}")

    if not apply:
        print("Dry run. Add --apply to write.")
        return 0
    for p in added + changed:
        out = target / p
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_bytes(files[p])
    for p in removed:
        (target / p).unlink(missing_ok=True)
    print(f"Written. Next, in {target}, build, run the hermetic tests, and commit "
          f"with the source commit {commit} in the message.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
