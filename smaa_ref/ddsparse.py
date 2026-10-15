import struct, os

files = [
    r"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX9.dds",
    r"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX10.dds",
    r"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\SearchTex.dds",
]

for p in files:
    if not os.path.exists(p):
        print("MISSING", p)
        continue
    d = open(p, "rb").read()
    print("=== %s  file=%d" % (os.path.basename(p), len(d)))
    for off in (4, 8, 12, 16, 20, 24, 28, 76, 80):
        v, = struct.unpack_from("<I", d, off)
        print("   @%-3d = %d (0x%08X)" % (off, v, v))
    print("   @28 mip w,h =", struct.unpack_from("<HH", d, 28))
    fourcc, = struct.unpack_from("<I", d, 0x4C)
    print("   fourcc @0x4C =", fourcc)
    dx10 = struct.unpack_from("<4sBBB", d, 0x84)
    print("   DX10 @0x84 =", dx10)
    for hsize in (128,):
        body = d[hsize:]
        w, h = struct.unpack_from("<II", d, 16), None
        print("   data=%d  bpt=%.4f" % (len(body), len(body) / (560 * 160)))
    print("   first data bytes:", list(d[128:128 + 24]))
