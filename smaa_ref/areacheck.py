"""Decode SMAA's AreaTex (160x560, 2 bytes/texel) and print exactly the texels
that SMAAArea() / SMAAAreaDiag() index for the axis-aligned test pattern.

Index math (SMAA.hlsl, unmodified reference - byte-identical to sma_ref/SMAA.hlsl):

  SMAAArea      texcoord_px = 16*round(4*e1) + sqrt(d.x) , 16*round(4*e2) + sqrt(d.y)
  SMAAAreaDiag  texcoord_px = 20*round(4*e1) + sqrt(d.x) , 20*round(4*e2) + sqrt(d.y)
  both:         y += (1/7)*subsample  -> +80 texels per subsample step (560/7 = 80)
  sample:       (px + 0.5) * (1/160, 1/560) with a POINT sampler -> texel floor(px)

d is clamped by SMAA_MAX_SEARCH_SAMPLES (16) / SMAA_AREATEX_MAX_DISTANCE_DIAG (20),
so sqrt(d) only ever spans [0,4] and [0,~4.5] -> the used texels are sparse.
"""
import hashlib
import struct

AREA_MAX = 16.0
DIAG_MAX = 20.0
W, H = 160, 560

FILES = [
    r"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX9.dds",
    r"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\AreaTexDX9.dds",
]


def payload(path):
    with open(path, "rb") as f:
        raw = f.read()
    assert raw[:4] == b"DDS ", "not a DDS"
    h, w = struct.unpack_from("<II", raw, 12)   # SURFACE_DESC: height first
    off = struct.unpack_from("<I", raw, 128)[0] or 128  # DX9 container leaves the field 0
    return w, h, raw[off:]


def tex(blob, x, y):
    i = 2 * (y * W + x)
    return (blob[i], blob[i + 1]) if i + 1 < len(blob) else ("oob", "oob")


def index(maxdist, e1, e2, dx=0.0, dy=0.0, subsample=0):
    x = maxdist * round(4.0 * e1) + dx
    y = maxdist * round(4.0 * e2) + dy + 80.0 * subsample
    return int(x), int(y)


for path in FILES:
    w, h, blob = payload(path)
    print(f"== {path}: {w}x{h}, payload={len(blob)} bytes, sha1={hashlib.sha1(blob).hexdigest()}")

    print("  SMAAArea (axis-aligned edges) - e/d combos this pattern produces:")
    for label, e1, e2 in (
        ("long run, no crossing edges", 0.0, 0.0),
        ("1-texel run, crossing both ends", 1.0, 1.0),
        ("crossing at near end only", 1.0, 0.0),
    ):
        for d in (0.0, 2.0, 4.0):  # sqrt(d) values for run lengths 0, 4, 16
            x, y = index(AREA_MAX, e1, e2, d, d)
            print(f"    {label}, sqrt(d)={d}: texel ({x},{y}) = {tex(blob, x, y)}")

    print("  SMAAAreaDiag (diagonal edges):")
    for label, e1, e2 in (
        ("no crossing diag edges", 0.0, 0.0),
        ("crossing at both ends", 1.0, 1.0),
    ):
        for d in (0.0, 2.0, 4.0):
            x, y = index(DIAG_MAX, e1, e2, d, d)
            print(f"    {label}, sqrt(d)={d}: texel ({x},{y}) = {tex(blob, x, y)}")
