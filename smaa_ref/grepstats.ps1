$src = 'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\render_error.log'
$dst = 'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\stats.log'
Select-String -Path $src -Pattern 'STATS' | ForEach-Object { $_.Line } | Set-Content $dst
