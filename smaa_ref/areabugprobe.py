"""Which byte-layout bug in the DDS loader would make SMAAArea() return
(non-zero, zero) for BOTH area texels this pattern hits?

Pattern (isolated dark pixel / axis-aligned rect):
  centre pixel D  -> area texel (64,64)  (e1=e2=1, dist=0)
  right/below px  -> area texel (0,0)   (e1=e2=0, dist=0)
Reference data says both are (0,0) -> reference SMAA writes NO weights here.
"""
import struct

PATH = r"c:\Users\\VABG\\source\\repos\\SharpDXModelViewer\\src\\ModelViewer\\Shaders\\AreaTexDX9.dds"
W, H = 160, 560
with open(PATH, "rb") as f:
    raw = f.read()

TEXELS = ((64, 64), (0, 0))


def read(blob, stride, x, y):
    i = stride * (y * W + x)
    return blob[i], blob[i + 1]


def variants():
    # (label, payload, stride)
    yield ("file[128:], stride 2 (correct)", raw[128:], 2)
    yield ("file[0:],   stride 2 (header not skipped)", raw, 2)
    yield ("file[128:], stride 3 (DX10 pitch used for DX9)", raw[128:], 3)
    yield ("file[128:], stride 4", raw[128:], 4)
    yield ("file[256:], stride 2", raw[256:], 2)
    # transposed indexing: texel (x,y) read as (y,x)
    yield ("transposed (x<->y), stride 2", raw[128:], 2)


for label, blob, stride in variants():
    out = []
    for x, y in TEXELS:
        tx, ty = (y, x) if label.startswith("transposed") else (x, y)
        if stride * (ty * W + tx) + 1 < len(blob):
            out.append(read(blob, stride, tx, ty))
        else:
            out.append(("oob", "oob"))
    print(f"{label}: (64,64)={out[0]}  (0,0)={out[1]}")
