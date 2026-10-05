param([string]$Source, [string]$At)

Add-Type -AssemblyName System.Drawing
$b = New-Object System.Drawing.Bitmap $Source
foreach ($pair in $At.Split(';')) {
    if ([string]::IsNullOrWhiteSpace($pair)) { continue }
    $parts = $pair.Split(',')
    $x = [int]$parts[0].Trim()
    $y = [int]$parts[1].Trim()
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $b.Width -or $y -ge $b.Height) { continue }
    $p = $b.GetPixel($x, $y)
    Write-Output ("({0},{1}) = {2},{3},{4}" -f $x, $y, $p.R, $p.G, $p.B)
}
$b.Dispose()