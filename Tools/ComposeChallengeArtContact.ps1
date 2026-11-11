param(
    [string]$ProjectRoot = 'C:\Users\TokyoGhoul\Doodlik',
    [string]$QaName = 'MenuValidation-6e879ab6',
    [string]$OutputName = 'Challenges-Art-Contact-20261029.png'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$resolvedRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$capturesRoot = (Resolve-Path -LiteralPath (Join-Path $resolvedRoot ".utmp\$QaName\Logs\CosmeticsVisual")).Path
if (-not $capturesRoot.StartsWith($resolvedRoot + '\.utmp\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Contact source must be a disposable QA capture directory.' }
$outputPath = [IO.Path]::GetFullPath((Join-Path (Join-Path $resolvedRoot 'Logs') $OutputName))
if (-not $outputPath.StartsWith($resolvedRoot + '\Logs\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Contact output must stay in project Logs.' }
$files = @('35-accessory_target-Landscape.png', '36-accessory_patch-Landscape.png', '39-clothing_explorer-Landscape.png', '37-trail_ninelives-Landscape.png', '38-trail_meteor-Landscape.png')
$canvas = New-Object System.Drawing.Bitmap(1080, 840)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
try {
    $graphics.Clear([System.Drawing.Color]::FromArgb(246, 231, 190))
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    for ($index = 0; $index -lt $files.Length; $index++) {
        $image = [System.Drawing.Image]::FromFile((Join-Path $capturesRoot $files[$index]))
        try {
            $tile = [System.Drawing.Rectangle]::new([int](($index % 3) * 360 + 7), [int]([Math]::Floor($index / 3) * 420 + 7), 346, 405)
            $crop = [System.Drawing.Rectangle]::new(377, 58, 526, 606)
            $graphics.DrawImage($image, $tile, $crop, [System.Drawing.GraphicsUnit]::Pixel)
        } finally { $image.Dispose() }
    }
    $canvas.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $outputPath
} finally { $graphics.Dispose(); $canvas.Dispose() }
