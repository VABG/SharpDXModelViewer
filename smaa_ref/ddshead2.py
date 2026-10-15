import struct

for name in ('AreaTexDX10.dds', 'AreaTexDX9.dds', 'SearchTex.dds'):
    with open(name, 'rb') as f:
        data = f.read()
    print(name, 'file=', len(data), 'magic=', data[:4])
    print('  @4 ddsSize =', struct.unpack_from('<I', data, 4)[0])
    print('  @8 version = 0x%08X' % struct.unpack_from('<I', data, 8)[0])
    print('  @12 =', struct.unpack_from('<I', data, 12)[0])
    print('  @16 =', struct.unpack_from('<I', data, 16)[0])
    print('  @20 =', struct.unpack_from('<I', data, 20)[0])
    print('  @24 =', struct.unpack_from('<I', data, 24)[0])
    print('  @28 mip =', struct.unpack_from('<HH', data, 28))
    print('  DX10 @128 fmt =', struct.unpack_from('<i', data, 128)[0])
    print('  DX10 @160 w,h =', struct.unpack_from('<ii', data, 160))
    print('  payload =', len(data) - 128)
