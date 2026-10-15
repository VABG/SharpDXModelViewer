"""Dump ASCII/UTF-16 identifier strings from the SharpDX D3DCompiler DLL.

Used to discover the exact enum/type names SharpDX exposes for D3D compile
flags (the C# enum names live in the DLL's type metadata, so grepping the
binary finds them).
"""

import re
import sys

path = sys.argv[1] if len(sys.argv) > 1 else (
    r"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\probe\bin\Debug\net9.0\SharpDX.D3DCompiler.dll")

data = open(path, "rb").read()

# ASCII identifiers.
names = set(item.decode("ascii") for item in re.findall(rb"[A-Za-z_][A-Za-z_0-9]{3,64}", data))

# UTF-16LE identifiers (type names for reflection are stored wide).
names.update(item.decode("utf-16-le") for item in re.findall(rb"(?:[ -~]\x00){4,80}", data))

interesting = sorted(n for n in names if re.search(r"Flag|Compile|Include|Option", n, re.I))

out = r"c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\enumnames.txt"
with open(out, "w", encoding="utf-8") as handle:
    handle.write("%d candidate names\n" % len(interesting))
    for name in interesting:
        handle.write(name + "\n")
