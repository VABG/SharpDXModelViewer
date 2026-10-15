import struct

for name in ('AreaTexDX9.dds', 'SearchTex.dds'):
    with open(name, 'rb') as f:
        data = f.read()
    print(name, 'file=', len(data))
    for off in range(0, 128, 4):
        v, = struct.unpack_from('<I', data, off)
        print('   @%3d = %d (0x%08X)' % (off, v, v))
    print('   payload =', len(data) - 128)
