"""Verify the SMAARenderer.cs DDS-upload fix by simulating both byte layouts.

The area texture is 160x560 R8G8_UNORM (2 bytes/texel).  The shader's SMAAArea()
computes texel coords as:

    px = 16 * round(4*e1) + sqrt(d.x)      py = 16 * round(4*e2) + sqrt(d.y)

e1/e2 come from bilinear-sampling the edges RT 0.25 texels off a texel center
(SMAA.hlsl @CROSSING_OFFSET), so a present crossing edge reads 0.75 and an
"end" edge reads 0.25 -> round(4*e) lands on slots 3 and 1, matching the
generator's edgesortho table (slots {0,1,3,4}).
"""
import struct

DDS = r"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\official_AreaTexDX9.dds"
HEADER = 128
W, H = 160, 560

raw = open(DDS, "rb").read()
payload = raw[HEADER:]

# Buggy layout: copy started at byte 0, so the header is uploaded as texels.
buggy = raw
# Fixed layout: copy starts at byte 128.
fixed = payload


def tex(buf, x, y):
    i = 2 * (y * W + x)
    return buf[i], buf[i + 1]


def slot(e):
    return round(4.0 * e)


print("DDS header bytes as texels (what the buggy upload puts at the top-left):")
print("   buggy texel (0,0) =", tex(buggy, 0, 0), "->", repr(raw[0:2]))
print("   fixed texel (0,0) =", tex(fixed, 0, 0))

print()
print("Shader lookups (band 0 = SMAA 1x, subsample offset 0):")
cases = [
    ("both crossing edges (e=0.75)", 0.75, 0.75, 1.0, 1.0),
    ("left crossing / right end",    0.75, 0.25, 1.0, 1.0),
    ("left end / right crossing",    0.25, 0.75, 1.0, 1.0),
    ("no crossing edges (e=0)",      0.0,  0.0,  1.0, 1.0),
]
for label, e1, e2, dx, dy in cases:
    px = 16 * slot(e1) + round(dx ** 0.5)
    py = 16 * slot(e2) + round(dy ** 0.5)
    print(f"  {label}: texel ({px},{py})")
    print(f"      fixed = {tex(fixed, px, py)}   -> weights "
          f"({tex(fixed, px, py)[0]/255:.3f}, {tex(fixed, px, py)[1]/255:.3f})")
    print(f"      buggy = {tex(buggy, px, py)}   "
          f"(reads file texel ({(px - 64) % W}, {py + (1 if px < 64 else 0)})")

print()
print("Slot occupancy of the official texture (matches AreaTex.py edgesortho):")
used = {(0, 0), (3, 0), (0, 3), (3, 3), (1, 0), (4, 0), (1, 3), (4, 3),
        (0, 1), (3, 1), (0, 4), (3, 4), (1, 1), (4, 1), (1, 4), (4, 4)}
for sy in range(5):
    row = []
    for sx in range(5):
        mx = 0
        for yy in range(16):
            for xx in range(16):
                a, b = tex(fixed, sx * 16 + xx, sy * 16 + yy)
                mx = max(mx, a, b)
        row.append((sx, sy, mx, (sx, sy) in used))
    print("  y-slot", sy, "->", [(sx, sy, mx, u) for sx, sy, mx, u in row])
