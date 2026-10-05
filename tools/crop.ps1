param([string]$Source, [string]$Target, [int]$X, [int]$Y, [int]$W, [int]$H, [double]$Scale = 1.0)
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Bitmap]::FromFile($Source)
$rect = New-Object System.Drawing.Rectangle $X, $Y, $W, $H
$crop = $img.Clone($rect, $img.PixelFormat)
if ($Scale -ne 1.0) {
    $nw = [int]($W * $Scale); $nh = [int]($H * $Scale)
    $big = New-Object System.Drawing.Bitmap $nw, $nh
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.InterpolationMode = 'NearestNeighbor'
    $g.DrawImage($crop, 0, 0, $nw, $nh)
    $g.Dispose(); $crop.Dispose(); $crop = $big
}
$crop.Save($Target, [System.Drawing.Imaging.ImageFormat]::Png)
$crop.Dispose(); $img.Dispose()
Write-Output "done"
