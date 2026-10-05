param([string]$Source, [int]$MinCount = 30)

Add-Type -AssemblyName System.Drawing
$b = New-Object System.Drawing.Bitmap $Source
$inBand = $false
$start = 0
for ($y = 0; $y -lt $b.Height; $y++) {
    $c = 0
    for ($x = 100; $x -lt $b.Width - 20; $x += 3) {
        $p = $b.GetPixel($x, $y)
        if ($p.R -lt 40 -and $p.G -lt 40 -and $p.B -lt 40) { $c++ }
    }
    $dark = $c -gt $MinCount
    if ($dark -and -not $inBand) { $inBand = $true; $start = $y }
    if (-not $dark -and $inBand) {
        $inBand = $false
        Write-Output ("dark band y={0}..{1} height={2}" -f $start, ($y - 1), ($y - $start))
    }
}
if ($inBand) { Write-Output ("dark band y={0}..{1}" -f $start, ($b.Height - 1)) }
$b.Dispose()