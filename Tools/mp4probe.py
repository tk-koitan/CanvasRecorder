import struct, sys, os

def boxes(f, start, end, depth=0, out=None):
    f.seek(start)
    while f.tell() < end:
        pos = f.tell()
        hdr = f.read(8)
        if len(hdr) < 8: break
        size, typ = struct.unpack(">I4s", hdr)
        typ = typ.decode("latin1")
        hs = 8
        if size == 1:
            size = struct.unpack(">Q", f.read(8))[0]; hs = 16
        elif size == 0:
            size = end - pos
        out.append((depth, typ, size, pos))
        if typ in ("moov", "trak", "mdia", "minf", "stbl", "moof", "traf", "mvex", "edts"):
            boxes(f, pos + hs, pos + size, depth + 1, out)
        elif typ == "mvhd":
            f.seek(pos + hs)
            ver = f.read(1)[0]; f.read(3)
            if ver == 1:
                f.read(16); ts = struct.unpack(">I", f.read(4))[0]; dur = struct.unpack(">Q", f.read(8))[0]
            else:
                f.read(8); ts = struct.unpack(">I", f.read(4))[0]; dur = struct.unpack(">I", f.read(4))[0]
            out[-1] = (depth, f"mvhd timescale={ts} duration={dur} -> {dur/ts if ts else 0:.2f}s", size, pos)
        elif typ == "mehd":
            f.seek(pos + hs)
            ver = f.read(1)[0]; f.read(3)
            dur = struct.unpack(">Q" if ver == 1 else ">I", f.read(8 if ver == 1 else 4))[0]
            out[-1] = (depth, f"mehd fragmentDuration={dur}", size, pos)
        f.seek(pos + size)

for path in sys.argv[1:]:
    print(f"=== {os.path.basename(path)}  ({os.path.getsize(path)} bytes) ===")
    out = []
    with open(path, "rb") as f:
        boxes(f, 0, os.path.getsize(path), 0, out)
    counts = {}
    for d, t, s, p in out:
        key = t.split()[0]
        counts[key] = counts.get(key, 0) + 1
    for d, t, s, p in out:
        if t.split()[0] in ("moof", "mdat", "sidx", "mfra") and counts.get(t.split()[0], 0) > 3:
            continue
        print("  " * d + f"{t}  (size={s})")
    print("  box counts:", {k: v for k, v in counts.items() if v > 1})
    print()
