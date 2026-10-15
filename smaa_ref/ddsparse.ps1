function ParseDds($path, $label) {
    $bytes = Get-Content -Path $path -Encoding Byte -Read 512
    if (-not $bytes) { Write-Output "MISSING=$label"; return }
    function U32($arr, $o) { ($arr[$o] -bor ($arr[$o+1] -shl 8) -bor ($arr[$o+2] -shl 16) -bor ($arr[$o+3] -shl 24)) }
    $s = "FILE=$label size=" + $bytes.Count
    $s += " magic=" + (-join ($bytes[0..3] | ForEach-Object { [char]$_ }))
    $s += " f4=" + (U32 $bytes 4)
    $s += " h12=" + (U32 $bytes 12)
    $s += " w16=" + (U32 $bytes 16)
    $s += " mipw20=" + (U32 $bytes 20)
    $s += " miph24=" + (U32 $bytes 24)
    $s += " pitch28=" + (U32 $bytes 28)
    $s += " mips32=" + (U32 $bytes 32)
    $s += " dx9fmt72=" + (U32 $bytes 72)
    $s += " flags80=" + (U32 $bytes 80)
    $s += " b128=" + (U32 $bytes 128)
    $s += " payload=" + ($bytes.Count - 128)
    Write-Output $s
}
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\SearchTex.dds" "search"
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX9.dds" "areadx9"
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX10.dds" "areadx10"
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\SMAAAreaTexDX10.dds" "refareadx10"
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\SMAASearchTexDX10.dds" "refsearchdx10"
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\SearchTex.dds" "search"
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX9.dds" "areadx9"
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX10.dds" "areadx10"
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\SMAAAreaTexDX10.dds" "refareadx10"
ParseDds "c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\SMAASearchTexDX10.dds" "refsearchdx10"
