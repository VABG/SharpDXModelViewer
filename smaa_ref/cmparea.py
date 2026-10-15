import struct

d9 = open(r"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX9.dds", "rb").read()[128:]
d10 = open(r"c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX10.dds", "rb").read()[128:]

print("d9 len", len(d9), "d10 len", len(d10))
print("d10[:len(d9)] == d9 ?", d10[:len(d9)] == d9)
print("d10[:89600] == d9[:89600] ?", d10[:89600] == d9[:89600])

# Interpret d10 as stride-3 and deinterleave into 2-channel planes
a = d10[0::3]
b = d10[1::3]
c = d10[2::3]
print("plane2 all zero?", set(c) == {0})
print("first 16 texels stride3:", [(d10[3*i], d10[3*i+1], d10[3*i+2]) for i in range(16)])

# Compare stride-3 pairs (a,b) vs d9 pairs
d9pairs = [(d9[2*i], d9[2*i+1]) for i in range(16)]
d10pairs = [(d10[3*i], d10[3*i+1]) for i in range(16)]
print("d9 pairs:", d9pairs)
print("d10 pairs:", d10pairs)

# Also check whether d10 == d9 with each texel expanded 2->3 by inserting 0 at front
expanded = bytearray()
for i in range(len(d9) // 2):
    expanded += b"\x00" + d9[2*i:2*i+2]
print("expanded len", len(expanded))
print("expanded == d10 ?", bytes(expanded) == d10)
