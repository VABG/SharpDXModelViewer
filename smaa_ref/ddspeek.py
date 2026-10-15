import struct

def peek(path, n):
    with open(path, 'rb') as f:
        data = f.read()
    body = data[128:]
    print(f"{path}: first {n} data bytes:")
    print('  as u8 :', list(body[:n]))
    print('  as u16:', [struct.unpack_from('<H', body, i)[0] for i in range(0, n, 2)])
    print('  as i8 :', [b - 256 if b > 127 else b for b in body[:n]])

peek('SearchTex.dds', 32)
peek('AreaTexDX9.dds', 16)
peek('AreaTexDX10.dds', 12)

# DXT5/BC2 blocks start with two R8 endpoint bytes then 6 bytes of 3-bit indices;
# plain R8 search data should be small integers (0..~15).
print()
print('SearchTex value histogram (top 12):')
with open('SearchTex.dds', 'rb') as f:
    body = f.read()[128:]
from collections import Counter
for v, c in Counter(body).most_common(12):
    print(f'  value={v} count={c}')
print(f'  min={min(body)} max={max(body)} nonzero={sum(1 for b in body if b)}')
