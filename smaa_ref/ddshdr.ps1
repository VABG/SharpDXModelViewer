$dir = 'c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders'
$out = 'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\ddshdr.log'
$lines = @()
foreach ($name in @('AreaTexDX10.dds','AreaTexDX9.dds','SearchTex.dds')) {
    $f = Join-Path $dir $name
    if (-not (Test-Path $f)) { $lines += "MISSING $name"; continue }
    $b = [byte[]](Get-Content $f -Encoding Byte -TotalCount 160)
    $magic = [string]::fromCharCode($b[0],$b[1],$b[2],$b[3])
    $pf = $b[12] + $b[13]*256
    $w = $b[128] + $b[129]*256
    $h = $b[132] + $b[133]*256
    $fmt = [string]::fromcharcode($b[136],$b[137],$b[138],$b[139])
    $mips = $b[140] + $b[141]*256 + $b[142]*65536 + $b[143]*16777216
    $fsz = $b[148] + $b[149]*256 + $b[150]*65536 + $b[151]*16777216
    $len = (Get-Content $f -Encoding Byte).Count
    $lines += "=== $name ==="
    $lines += "magic=$magic pixelFormat=$pf ($('0x{0:x}' -f $pf)) w=$w h=$h format='$fmt' mips=$mips fileSize=$len declaredDataSize=$fsz headerPlusData=$((128+$fsz))"
    $lines += "bytesPerPixelGuess=$([math]::Round(($len-128)/($w*$h), 4))"
}
$lines | Set-Content -Path $out -Encoding utf8
