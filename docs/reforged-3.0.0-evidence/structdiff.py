# Structural diffs for every format in the build, all linear rather than quadratic.
#
# difflib.SequenceMatcher is the whole performance problem and it was also giving WRONG
# answers. On UI/TriggerData.txt it reported 74 removed lines where the true answer is zero
# removed, because it scores an edited value as a delete plus an insert. On Units/UnitSkin.txt
# it took 21.8 seconds by itself.
#
# Every file here has structure, so use it. A key set comparison is one pass of hashing, and
# it answers the question actually being asked, which is what was added, removed or changed
# rather than how one line sequence transforms into another.
import hashlib
import re

SECTION = re.compile(r"^\s*\[([^\]]+)\]\s*$")
JASS_FN = re.compile(r"^\s*(?:function|native)\s+([A-Za-z_]\w*)\b")


def parse_ini(text):
    """[Section] plus key=value into {(section, key): value}. Repeated keys inside a section
    are kept by appending an ordinal, because several of these files legitimately repeat a
    key and collapsing them would silently lose changes."""
    out, section, seen = {}, "", {}
    for line in text.splitlines():
        s = line.strip()
        if not s or s.startswith("//"):
            continue
        m = SECTION.match(s)
        if m:
            section = m.group(1)
            seen = {}
            continue
        if "=" not in s:
            continue
        k, v = s.split("=", 1)
        k = k.strip()
        n = seen.get(k, 0)
        seen[k] = n + 1
        out[(section, k if n == 0 else f"{k}#{n}")] = v.strip()
    return out


def parse_jass(text):
    """Function name to a hash of its body. A source file's meaning is its functions, so
    compare those rather than its line sequence."""
    out, name, body = {}, None, []
    for line in text.splitlines():
        m = JASS_FN.match(line)
        if m:
            if name:
                out[name] = hashlib.md5("\n".join(body).encode("utf-8", "replace")).hexdigest()
            name, body = m.group(1), [line]
        elif name:
            body.append(line)
    if name:
        out[name] = hashlib.md5("\n".join(body).encode("utf-8", "replace")).hexdigest()
    return out


def parse_lines(text):
    """Last resort. Multiset of lines, so a reorder reads as no change, which is correct
    for a file whose line order carries no meaning."""
    out, seen = {}, {}
    for line in text.splitlines():
        s = line.rstrip()
        if not s:
            continue
        n = seen.get(s, 0)
        seen[s] = n + 1
        out[(s, n)] = ""
    return out


def _report(a, b, kind, limit, fmt):
    added = sorted(b.keys() - a.keys(), key=repr)
    removed = sorted(a.keys() - b.keys(), key=repr)
    changed = sorted((k for k in a.keys() & b.keys() if a[k] != b[k]), key=repr)
    out = [f"  parsed as   {kind}",
           f"  entries     {len(a):,} -> {len(b):,}",
           f"  ADDED       {len(added):,}",
           f"  REMOVED     {len(removed):,}",
           f"  changed     {len(changed):,}"]
    for title, keys in (("ADDED", added), ("REMOVED", removed)):
        if keys:
            out.append(f"  --- {title} ---")
            for k in keys[:limit]:
                out.append("      " + fmt(k, (b if title == "ADDED" else a)[k]))
            if len(keys) > limit:
                out.append(f"      ... and {len(keys) - limit:,} more")
    if changed:
        out.append("  --- CHANGED ---")
        for k in changed[:limit]:
            out.append(f"      {fmt(k, '')}  {a[k]!r} -> {b[k]!r}")
        if len(changed) > limit:
            out.append(f"      ... and {len(changed) - limit:,} more")
    return "\n".join(out)


def diff(path, ta, tb, limit=25):
    low = path.lower()
    if low.endswith((".txt", ".fdf", ".ini")):
        a, b = parse_ini(ta), parse_ini(tb)
        if a and b:
            return _report(a, b, "section and key",
                           limit, lambda k, v: f"[{k[0]}] {k[1]} = {v}"[:150])
    if low.endswith((".j", ".lua", ".ai")):
        a, b = parse_jass(ta), parse_jass(tb)
        if a and b:
            return _report(a, b, "functions", limit, lambda k, v: str(k)[:150])
    a, b = parse_lines(ta), parse_lines(tb)
    return _report(a, b, "line multiset", limit, lambda k, v: str(k[0])[:150])
