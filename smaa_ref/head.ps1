$src = 'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\render_error.log'
$dst = 'c:\Users\VABG\source\repos\SharpDXModelViewer\smaa_ref\head.log'
Get-Content -Path $src -TotalCount 60 | Set-Content -Path $dst
