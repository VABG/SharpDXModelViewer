"""Probe the AreaTexDX9 payload under several possible byte-order conventions and
report what the shader's SMAAArea() would read for the isolated-pixel patterns.

SMAAArea() texel index (SMAA.hlsl:1090):
    px = 16 * round(4*e1) + sqrt(d.x)      py = 16 * round(4*e2) + sqrt(d.y)
    py += 80 * subsample_offset            (SMAA_AREATEX_SUBTEX_SIZE = 1/7, 560/7 = 80)
"""
import struct

PATH = r"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX9.dds"
W, H = 160, 560

with open(PATH, "rb") as f:
    raw = f.read()
blob = raw[128:]


def get(x, y):
    i = 2 * (y * W + x)
    return blob[i], blob[i + 1]


def px(e1, e2, dx=0.0, dy=0.0):
    return int(16.0 * round(4.0 * e1) + dx), int(16.0 * round(4.0 * e2) + dy)


# Candidate byte-order conventions: how a shader texel (u, v) maps to file (x, y).
CONVENTIONS = {
    "identity":      lambda u, v: (u, v),
    "flip-y":        lambda u, v: (u, H - 1 - v),
    "flip-x":        lambda u, v: (W - 1 - u, v),
    "flip-xy":       lambda u, v: (W - 1 - u, H - 1 - v),
    "transpose":     lambda u, v: (v, u),
    "trans+flip-y":  lambda u, v: (v, W - 1 - u),
}

# Patterns of interest: (label, e1, e2)
PATTERNS = (
    ("no crossing edges  (e1=0,e2=0)", 0.0, 0.0),
    ("crossing at left   (e1=1,e2=0)", 1.0, 0.0),
    ("crossing at right  (e1=0,e2=1)", 0.0, 1.0),
    ("both ends          (e1=1,e2=1)", 1.0, 1.0),
)

for name, f in CONVENTIONS.items():
    print(f"--- {name}")
    for label, e1, e2 in PATTERNS:
        u, v = px(e1, e2)
        x, y = f(u, v)
        inside = 0 <= x < W and 0 <= y < H
        vals = get(x, y) if inside else ("oob", "oob")
        print(f"   {label}: shader texel ({u},{v}) -> file ({x},{y}) = {vals}")
