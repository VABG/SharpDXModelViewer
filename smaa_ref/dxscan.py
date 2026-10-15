import re, sys

p = r"c:\Users\VABG\.nuget\packages\sharpdx.direct3d11\4.2.0\lib\net45\SharpDX.Direct3d11.dll"
try:
    data = open(p, "rb").read()
except FileNotFoundError:
    print("MISSING", p)
    raise SystemExit

found = set()
for m in re.finditer(rb"[ -~]{4,}", data):
    s = m.group().decode()
    if "Copy" in s:
        found.add(s)

for s in sorted(found):
    print(s)
