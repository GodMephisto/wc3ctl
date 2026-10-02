# Minimal TACT/NGDP reader, enough to walk a Warcraft III build from its build config
# down to a file list, for any build whose config is still on disk or on the CDN.
#
# Why this exists. The install caches the config of the build it replaced, so a machine that
# has taken the 3.0.0 patch still names 2.0.4.23745. Blizzard's CDN still serves that build's
# manifests. So a real before-and-after diff of a patch is recoverable after the fact, which
# had been written off as impossible.
#
# The chain is build config -> encoding table -> root -> file list. Every hop is addressed by
# an encoding key, and asking for a content key instead returns 403, which is the mistake that
# previously made this look impossible.
import hashlib
import io
import pathlib
import struct
import sys
import urllib.request
import zlib

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

CDN = "http://level3.blizzard.com/tpr/war3"
CACHE = pathlib.Path(__file__).with_name("tactcache")
CACHE.mkdir(exist_ok=True)


def fetch(ekey, kind="data"):
    """Fetch an ekey-addressed blob, cached on disk so a re-run costs nothing."""
    local = CACHE / f"{kind}_{ekey}"
    if local.exists():
        return local.read_bytes()
    url = f"{CDN}/{kind}/{ekey[0:2]}/{ekey[2:4]}/{ekey}"
    with urllib.request.urlopen(url, timeout=120) as r:
        body = r.read()
    local.write_bytes(body)
    return body


def blte_decode(raw):
    """Decode a BLTE container. Encrypted chunks are not supported and raise."""
    if raw[:4] != b"BLTE":
        raise ValueError("not BLTE")
    header_size, = struct.unpack_from(">I", raw, 4)
    if header_size == 0:
        return _chunk(raw[8:])
    chunk_count = int.from_bytes(raw[9:12], "big")
    table, off = [], 12
    for _ in range(chunk_count):
        csize, dsize = struct.unpack_from(">II", raw, off)
        table.append((csize, dsize, raw[off + 8:off + 24]))
        off += 24
    out, pos = bytearray(), header_size
    for csize, dsize, cmd5 in table:
        piece = raw[pos:pos + csize]
        if hashlib.md5(piece).digest() != cmd5:
            raise ValueError("chunk md5 mismatch")
        out += _chunk(piece)
        pos += csize
    return bytes(out)


def _chunk(piece):
    mode = piece[0:1]
    if mode == b"N":
        return piece[1:]
    if mode == b"Z":
        return zlib.decompress(piece[1:])
    if mode == b"F":
        return blte_decode(piece[1:])
    raise ValueError(f"unsupported BLTE chunk mode {mode!r}")


def parse_build_config(text):
    cfg = {}
    for line in text.splitlines():
        if not line or line.startswith("#") or " = " not in line:
            continue
        k, v = line.split(" = ", 1)
        cfg[k.strip()] = v.strip().split()
    return cfg


def parse_encoding(data):
    """Return a ckey(hex) -> ekey(hex) map from a decoded EN v1 table."""
    if data[:2] != b"EN":
        raise ValueError("not an encoding table")
    ckey_len, ekey_len = data[3], data[4]
    cpage_kb, = struct.unpack_from(">H", data, 5)
    epage_kb, = struct.unpack_from(">H", data, 7)
    cpage_count, = struct.unpack_from(">I", data, 9)
    epage_count, = struct.unpack_from(">I", data, 13)
    # The espec block size is a uint40 at byte 17, and the block itself starts at 22.
    # Reading it one byte later parses as 23,394 instead of 91, which puts the page area
    # 53 bytes off, still lands on plausible-looking data, and silently yields a partial
    # table rather than an error. Verified by locating a known ckey and confirming its
    # entry sits at an exact multiple of 38 bytes from the page start.
    espec_size = int.from_bytes(data[17:22], "big")

    pos = 22 + espec_size
    pos += cpage_count * (ckey_len + 16)          # page index, skipped
    page_size = cpage_kb * 1024
    out = {}
    for _ in range(cpage_count):
        page = data[pos:pos + page_size]
        pos += page_size
        p = 0
        while p + 6 + ckey_len <= len(page):
            key_count = page[p]
            if key_count == 0:
                break
            size = int.from_bytes(page[p + 1:p + 6], "big")
            ckey = page[p + 6:p + 6 + ckey_len]
            first_ekey = page[p + 6 + ckey_len:p + 6 + ckey_len + ekey_len]
            out[ckey.hex()] = (first_ekey.hex(), size)
            p += 6 + ckey_len + key_count * ekey_len
    return out


def load_build(label, build_cfg_text):
    cfg = parse_build_config(build_cfg_text)
    name = cfg["build-name"][0]
    print(f"[{label}] {name}")
    enc_ekey = cfg["encoding"][1]
    enc = blte_decode(fetch(enc_ekey))
    print(f"[{label}]   encoding decoded, {len(enc):,} bytes")
    table = parse_encoding(enc)
    print(f"[{label}]   encoding table holds {len(table):,} content keys")
    root_ckey = cfg["root"][0]
    root_ekey = table[root_ckey][0] if root_ckey in table else None
    print(f"[{label}]   root ckey {root_ckey} -> ekey {root_ekey}")
    if root_ekey is None:
        return cfg, table, None
    root = blte_decode(fetch(root_ekey))
    print(f"[{label}]   root decoded, {len(root):,} bytes, first bytes {root[:16].hex()}")
    return cfg, table, root
