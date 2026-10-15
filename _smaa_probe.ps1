$ErrorAction = false
$tree = Get-Content '.\smaa_tree.json' -Raw | ConvertFrom-Json
foreach ($item in $tree.tree) {
    if ($item.path -match '(hlsl|dds|png|txt)$') {
        Write-Output ("{0}  [{1} bytes]" -f $item.path, $item.size)
    }
}
