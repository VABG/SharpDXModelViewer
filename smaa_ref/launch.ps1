$exe = 'c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\bin\Debug\net9.0-windows\ModelViewer.exe'
$out = 'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\launch_report.txt'
$lines = @()

$p = Start-Process -FilePath $exe -PassThru
$id = $p.Id
$lines += "launched pid=$id"

for ($i = 0; $i -lt 7; $i++) {
    Start-Sleep -Seconds 1
    $alive = Get-Process -Id $id -ErrorAction SilentlyContinue
    if (-not $alive) {
        $lines += "process gone after $($i + 1)s, exitcode=$($p.ExitCode)"
        break
    }
    $lines += "t=$($i + 1)s alive, cpu=$($alive.CPU)"
}

$still = Get-Process -Id $id -ErrorAction SilentlyContinue
if ($still) {
    $lines += "FINAL: still alive after 7s (SMAA path ran without exiting)"
    Stop-Process -Id $id -Force
} else {
    $lines += "FINAL: process exited"
}

$lines | Set-Content -Path $out -Encoding utf8
