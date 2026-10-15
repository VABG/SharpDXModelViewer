$paths = @(
  'c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\bin\Debug\net9.0-windows\ModelViewer.exe',
  'c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\bin\Debug\net9.0-windows\ModelViewer.dll',
  'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\diag.log',
  'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\renderlog.txt',
  'c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX10.dds',
  'c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\AreaTexDX9.dds',
  'c:\Users\VABG\source\repos\SharpDXModelViewer\src\ModelViewer\Shaders\SearchTex.dds',
  'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\AreaTexDX10.dds',
  'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\SearchTexDX10.dds'
)
$lines = @()
foreach ($p in $paths) {
  if (Test-Path $p) {
    $i = Get-Item $p
    $lines += ('{0}  size={1}  mtime={2}' -f $p, $i.Size, $i.LastModifiedDate)
  } else {
    $lines += ('MISSING: {0}' -f $p)
  }
}
$lines | Set-Content -Path 'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\stat.log' -Encoding utf8
