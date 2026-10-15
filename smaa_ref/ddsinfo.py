import struct, os

def info(path):
    with open(path, 'rb') as f:
        data = f.read()
    assert data[:4] == b'DDS ', path
    hsize, = struct.unpack_from('<I', data, 4)
    flags, = struct.unpack_from('<I', data, 8)
    height, = struct.unpack_from('<I', data, 12)
    width, = struct.unpack_from('<I', data, 16)
    mipsz, = struct.unpack_from('<I', data, 20)
    mipcount, = struct.unpack_from('<I', data, 24)
    fourcc, = struct.unpack_from('<I', data, 0x4C)
    dx10 = None
    if hsize == 128:  # extended DX10 header present
        dx10, = struct.unpack_from('<I', data, 128)
    print(f"{os.path.basename(path)}: file={len(data)} hsize={hsize} "
          f"w={width} h={height} mips={mipcount or 1} "
          f"mipsize={mipsz} flags=0x{flags:08X} fourcc=0x{fourcc:08X} "
          f"dx10fmt={'0x%08X' % dx10 if dx10 is not None else '-'}")
    print(f"   bytes-per-texel = {(len(data) - (128 if hsize == 128 else 128)) / (width*height):.3f}")

for name in ('AreaTexDX10.dds', 'AreaTexDX9.dds', 'SearchTex.dds'):
    info(name)
