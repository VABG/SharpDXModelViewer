import os, struct

SHADERS = r"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders"


def parse(path):
    with open(path, "rb") as f:
        data = f.read()
    assert data[:4] == b"DDS ", path
    hsize, = struct.unpack_from("<I", data, 4)
    height, = struct.unpack_from("<I", data, 12)
    width, = struct.unpack_from("<I", data, 16)
    head = 128 + (4 if hsize == 128 else 0)
    return width, height, data[head:]


def pairs(px, stride, ch):
    i, j = ch
    return [(px[stride * k + i], px[stride * k + j]) for k in range(len(px) // stride)]


def flip_h(p, w, h):
    out = []
    for y in range(h):
        out.extend(reversed(p[y * w:(y + 1) * w]))
    return out


def flip_v(p, w, h):
    out = []
    for y in reversed(range(h)):
        out.extend(p[y * w:(y + 1) * w])
    return out


wa, ha, pa = parse(os.path.join(SHADERS, "AreaTexDX9.dds"))
wb, hb, pb = parse(os.path.join(SHADERS, "AreaTexDX10.dds"))
A = pairs(pa, 2, (0, 1))
B = pairs(pb, 3, (1, 2))
print(f"A nonzero={sum(1 for x, y in A if x or y)}  "
      f"B nonzero={sum(1 for x, y in B if x or y)}")

samezero = sum(1 for x, y in zip(A, B) if (x == 0 and y == 0) or (x != 0 and y != 0))
print(f"zero-pattern agreement: {samezero}/{len(A)}")

Aswapped = [(y, x) for x, y in A]
for label, cand in (
    ("B", B),
    ("swap(B)", [(y, x) for x, y in B]),
    ("flipH(B)", flip_h(B, wb, hb)),
    ("flipV(B)", flip_v(B, wb, hb)),
    ("flipHV(B)", flip_v(flip_h(B, wb, hb), wb, hb)),
    ("flipH(swap(B))", flip_h([(y, x) for x, y in B], wb, hb)),
    ("flipV(swap(B))", flip_v([(y, x) for x, y in B], wb, hb)),
    ("flipHV(swap(B))", flip_v(flip_h([(y, x) for x, y in B], wb, hb), wb, hb)),
):
    same = sum(1 for x, y in zip(A, cand) if x == y)
    print(f"A vs {label}: equal = {same}/{len(A)}")

print("multiset equal (A vs B)?", sorted(A) == sorted(B))
print("multiset equal (A vs swap(B))?", sorted(A) == sorted([(y, x) for x, y in B]))

# Block structure: 16x16 blocks, 10 across x 35 down.
def blocks(p, w, h):
    res = []
    for by in range(h // 16):
        row = ""
        for bx in range(w // 16):
            nz = 0
            for y in range(by * 16, by * 16 + 16):
                for x in range(bx * 16, bx * 16 + 16):
                    a, b = p[y * w + x]
                    if a or b:
                        nz += 1
            row += f"{nz:4d}"
        res.append(row)
    return res

print("DX9 blocks:")
for i, r in enumerate(blocks(A, wa, ha)):
    print(f"  row {i:02d}: {r}")
print("DX10 blocks:")
for i, r in enumerate(blocks(B, wb, hb)):
    print(f"  row {i:02d}: {r}")
