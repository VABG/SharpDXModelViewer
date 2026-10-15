import os, struct

SHADERS = r"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders"
REF = r"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref"


def parse(path):
    with open(path, "rb") as f:
        data = f.read()
    assert data[:4] == b"DDS ", path
    hsize, = struct.unpack_from("<I", data, 4)
    height, = struct.unpack_from("<I", data, 12)
    width, = struct.unpack_from("<I", data, 16)
    fourcc = data[0x4C:0x50]
    head = 128 + (4 if hsize == 128 else 0)
    px = data[head:]
    return width, height, fourcc, hsize, px


def planes2(px, stride):
    """Return list of (v0, v1) per texel using the first two bytes of each texel."""
    return [(px[stride * i], px[stride * i + 1]) for i in range(len(px) // stride)]


def show(name, w, h, fourcc, hsize, px, stride):
    n = w * h
    print(f"{name}: w={w} h={h} fourcc={fourcc!r} hsize={hsize} "
          f"pixelbytes={len(px)} expected@{stride}={n*stride} texels={n}")


def flip_h(pairs, w, h):
    out = []
    for y in range(h):
        row = pairs[y * w:(y + 1) * w]
        out.extend(reversed(row))
    return out


def flip_v(pairs, w, h):
    out = []
    for y in reversed(range(h)):
        out.extend(pairs[y * w:(y + 1) * w])
    return out


for base in (SHADERS, REF):
    for name in ("AreaTexDX9.dds", "AreaTexDX10.dds"):
        p = os.path.join(base, name)
        if not os.path.exists(p):
            print(f"MISSING: {p}")
            continue
        w, h, fourcc, hsize, px = parse(p)
        for stride in (2, 3, 4):
            if len(px) == w * h * stride:
                show(name, w, h, fourcc, hsize, px, stride)
                pairs = planes2(px, stride)
                nz = sum(1 for a, b in pairs if a or b)
                print(f"   stride={stride} nonzero texels={nz}/{len(pairs)} "
                      f"first8={pairs[:8]}")

# Compare the two area textures under the four axis-aligned orientations.
a = os.path.join(SHADERS, "AreaTexDX9.dds")
b = os.path.join(SHADERS, "AreaTexDX10.dds")
if os.path.exists(a) and os.path.exists(b):
    wa, ha, fa, sa, pa = parse(a)
    wb, hb, fb, sb, pb = parse(b)
    sa = len(pa) // (wa * ha)
    sb = len(pb) // (wb * hb)
    A = planes2(pa, sa)
    B = planes2(pb, sb)
    print(f"compare: A(DX9) {wa}x{ha}@{sa}  B(DX10) {wb}x{hb}@{sb}")
    if len(A) == len(B):
        for label, cand in (
            ("identity", B),
            ("flipH", flip_h(B, wb, hb)),
            ("flipV", flip_v(B, wb, hb)),
            ("flipHV", flip_v(flip_h(B, wb, hb), wb, hb)),
        ):
            same = sum(1 for x, y in zip(A, cand) if x == y)
            print(f"   A vs B {label}: equal texels = {same}/{len(A)}")
    else:
        # Try swapping the two channels of one of them too.
        print(f"   lengths differ: {len(A)} vs {len(B)}")
        Bswap = [(y, x) for x, y in B]
        for label, cand in (("swap", Bswap), ("swap+flipV", flip_v(Bswap, wb, hb))):
            same = sum(1 for x, y in zip(A, cand) if x == y)
            print(f"   A vs B {label}: equal texels = {same}/{len(A)}")
