# Builds the game's app icon from the studio key art (Art/Source/title_keyart.png): the THE / 3:13 / MACHINE
# lockup at 48-256 px, just the red "3:13" at 16-32 px where the full lockup is unreadable.
# Writes Launcher/Assets/launcher.ico, Installer/setup.ico and Assets/_Project/Branding/AppIcon.png (Unity exe icon).
# Usage (Windows PowerShell): powershell -File Tools/ArtGen/make_app_icon.ps1 [repoRoot]
param([string]$Root = (Resolve-Path "$PSScriptRoot\..\..").Path)
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Join-Path $Root "Art\Source\title_keyart.png"))
# Regions in key-art pixels (art is 1672x941; scaled if the source is re-exported at another size).
$k = $src.Width / 1672.0
function Region($x, $y, $w, $h) { New-Object System.Drawing.Rectangle ([int]($x * $k)), ([int]($y * $k)), ([int]($w * $k)), ([int]($h * $k)) }
$lockup = Region 92 58 410 400   # THE / 3:13 / MACHINE, stops above the tagline
$digits = Region 128 158 280 148 # 3:13 only

function Frame($region, $size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    # Night-sky colour from the key art behind the logo, so aspect-fit padding is invisible.
    $g.Clear([System.Drawing.Color]::FromArgb(14, 13, 22))
    $scale = [Math]::Min($size / $region.Width, $size / $region.Height)
    $dw = [int]($region.Width * $scale); $dh = [int]($region.Height * $scale)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle ([int](($size - $dw) / 2)), ([int](($size - $dh) / 2)), $dw, $dh), $region, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$sizes = 16, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) { , (Frame ($(if ($s -le 32) { $digits } else { $lockup })) $s) }

# ICO container with PNG-compressed frames (Windows Vista+).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $frames[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
$ico = $out.ToArray()
[IO.File]::WriteAllBytes((Join-Path $Root "Launcher\Assets\launcher.ico"), $ico)
[IO.File]::WriteAllBytes((Join-Path $Root "Installer\setup.ico"), $ico)
[IO.File]::WriteAllBytes((Join-Path $Root "Assets\_Project\Branding\AppIcon.png"), $frames[$sizes.Count - 1])
$src.Dispose()
"Wrote icon ($($sizes -join ', ') px) to launcher.ico, setup.ico, AppIcon.png"
