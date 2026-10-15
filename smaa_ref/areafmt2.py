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
sa = len(pa) // (wa * ha)
sb = len(pb) // (wb * hb)
print(f"DX9 {wa}x{ha} stride={sa}   DX10 {wb}x{hb} stride={sb}")

A = pairs(pa, sa, (0, 1))
print(f"DX9 nonzero = {sum(1 for x, y in A if x or y)}")

for ch in ((0, 1), (0, 2), (1, 2)):
    B = pairs(pb, sb, ch)
    nz = sum(1 for x, y in B if x or y)
    print(f"DX10 channels {ch}: nonzero={nz}")
    for label, cand in (
        ("identity", B),
        ("flipH", flip_h(B, wb, hb)),
        ("flipV", flip_v(B, wb, hb)),
        ("flipHV", flip_v(flip_h(B, wb, hb), wb, hb)),
    ):
        same = sum(1 for x, y in zip(A, cand) if x == y)
        print(f"   DX9 vs DX10{ch} {label}: equal = {same}/{len(A)}")
