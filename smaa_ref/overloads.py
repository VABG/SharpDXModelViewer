"""Extract the CompileFromFile/Compile overload errors from the last build log.

The C# compiler reveals the expected parameter types in CS1503 errors, which is
how the right CompileFromFile overload is discovered.
"""

import re

src = r"c:\Users\VABG\\source\\repos\\SharpDXModelViewer\\smaa_ref\\build7.log"
out = r"c:\Users\VABG\\source\\repos\\SharpDXModelViewer\\smaa_ref\\overloads.txt"

text = open(src, encoding="utf-8", errors="replace").read()
lines = [line for line in text.splitlines() if "ApiProbe2.cs" in line]

seen = set()
with open(out, "w", encoding="utf-8") as handle:
    for line in lines:
        if line not in seen:
            seen.add(line)
            handle.write(line + "\n")
