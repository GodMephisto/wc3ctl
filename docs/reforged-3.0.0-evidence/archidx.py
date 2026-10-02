# Reads the CASC archive indices in Data/indices and resolves an encoding key to the
# archive and byte range that holds it.
#
# Ordinary content files are not served loose. Asking for one by ekey returns 403 for the
# CURRENT build as readily as for the old one, which is the control that shows this is the
# transport and not the build's age. The archives are content addressed and cumulative, so a
# file that has not changed since an old build is still sitting in an archive the current
# install indexes.
import pathlib
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

IDX_DIR = pathlib.Path(r"C:\Warcraft III\Data\indices")


def parse_index(path):
    """Yield (ekey_hex, offset, size) for an archive index, or () if the footer says it is
    not one. Returns the parsed footer too, so a caller can see why a file was skipped."""
    raw = path.read_bytes()
    if len(raw) < 28:
        return None, []
    # Footer is the last 28 bytes. The element count is what makes the parse checkable.
    f = raw[-28:]
    version, _u0, _u1, block_kb, offset_bytes, size_bytes, key_bytes, checksum_size = f[8:16]
    num_elements, = struct.unpack_from("<I", f, 16)
    entry_size = key_bytes + size_bytes + offset_bytes
    if entry_size == 0 or key_bytes != 16:
        return dict(version=version, entry_size=entry_size, num=num_elements), []

    block_size = block_kb * 1024
    per_block = block_size // entry_size
    out = []
    pos = 0
    remaining = num_elements
    while remaining > 0 and pos + block_size <= len(raw):
        block = raw[pos:pos + block_size]
        take = min(per_block, remaining)
        p = 0
        for _ in range(take):
            key = block[p:p + key_bytes]
            if key == b"\0" * key_bytes:
                p += entry_size
                continue
            q = p + key_bytes
            size = int.from_bytes(block[q:q + size_bytes], "big")
            off = int.from_bytes(block[q + size_bytes:q + size_bytes + offset_bytes], "big") \
                if offset_bytes else 0
            out.append((key.hex(), off, size))
            p += entry_size
        remaining -= take
        pos += block_size
    meta = dict(version=version, entry_size=entry_size, num=num_elements,
                offset_bytes=offset_bytes, size_bytes=size_bytes, parsed=len(out))
    return meta, out


CDN_CFG = pathlib.Path(
    r"C:\Warcraft III\Data\config\ef\e6\efe632141fdb1c7388bb77959cc1851b")


def real_archives():
    """Only the hashes listed under the archives key. The archive-group index is a virtual
    concatenation whose offsets run past 600 GB and whose hash is not a fetchable archive,
    so including it resolves keys to a URL that answers 403. The file-index files carry no
    offset at all and describe loose files, which the CDN does not serve for content."""
    names = set()
    for line in CDN_CFG.read_text(encoding="utf-8").splitlines():
        if line.startswith("archives = "):
            names.update(line.split(" = ", 1)[1].split())
    return names


def build_map(verbose=True):
    """ekey -> (archive_hash, offset, size) across every real archive index on disk."""
    table = {}
    stats = []
    keep = real_archives()
    for path in sorted(IDX_DIR.glob("*.index")):
        archive = path.stem
        if archive not in keep:
            continue
        meta, entries = parse_index(path)
        stats.append((archive, meta, len(entries)))
        for ekey, off, size in entries:
            table.setdefault(ekey, (archive, off, size))
    if verbose:
        good = [s for s in stats if s[2] > 0]
        print(f"parsed {len(good)} of {len(stats)} index files, "
              f"{len(table):,} distinct encoding keys", file=sys.stderr)
        for archive, meta, n in stats[:3]:
            print(f"   sample {archive[:12]} {meta}", file=sys.stderr)
    return table


if __name__ == "__main__":
    t = build_map()
    print(f"{len(t):,} keys resolvable")
