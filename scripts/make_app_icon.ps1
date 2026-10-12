# 앱 아이콘(.ico) 생성기 — 작업표시줄·트레이·실행 파일용.
# 회사 CI 마크(img_ci_mark.png)는 색·모양을 바꾸지 않는다. 기본(-Style Plain)은 기존 원캡과 같은 마크 그대로이고
# (2026-10-12 사용자 확정 — 원본 KFTCOneCAP.ico는 32x32 한 장뿐이라 같은 마크로 16~256 여러 크기를 담아 선명하게 만든다),
# 원캡과 구분이 필요해지면 -Style Tile(둥근 사각 타일 + 흰 원판 위에 마크, 타일 색은 -TileColor)을 쓴다.
# 홈 화면 로고는 이 아이콘이 아니라 CI 마크 PNG를 그대로 쓴다.
#
# 사용: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\make_app_icon.ps1
#        [-Style Plain|Tile] [-TileColor 1E3A8A] [-Out src\KFTCTaxCAP\Assets\Icons\KFTCTaxCAP.ico] [-PreviewDir <폴더>]
# Windows PowerShell 5.1은 BOM 없는 스크립트의 한글을 깨뜨리므로 이 파일은 UTF-8 BOM으로 저장한다.
param(
    [string]$Mark = (Join-Path $PSScriptRoot '..\src\KFTCTaxCAP\Assets\Images\img_ci_mark.png'),
    [string]$Out = (Join-Path $PSScriptRoot '..\src\KFTCTaxCAP\Assets\Icons\KFTCTaxCAP.ico'),
    [ValidateSet('Plain', 'Tile')][string]$Style = 'Plain',
    [string]$TileColor = '1E3A8A',
    [string]$PreviewDir
)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Mark))
$tileBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#' + $TileColor))

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$s) {
    $b = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    if ($Style -eq 'Plain') {
        $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $s, $s))
        $g.Dispose()
        return $b
    }

    # 16px은 타일을 꽉 채우고, 그 이상은 둘레에 3% 여백(다른 아이콘과 나란히 놓일 때 숨 쉴 틈).
    $inset = if ($s -le 16) { 0 } else { [Math]::Round($s * 0.03) }
    $tw = $s - 2 * $inset
    $g.FillPath($tileBrush, (New-RoundRect $inset $inset $tw $tw ([Math]::Max(2, $s * 0.22))))

    $plate = $tw * 0.80; $po = $inset + ($tw - $plate) / 2
    $g.FillEllipse([System.Drawing.Brushes]::White, [float]$po, [float]$po, [float]$plate, [float]$plate)

    $ms = $tw * 0.66; $mo = $inset + ($tw - $ms) / 2
    $g.DrawImage($src, (New-Object System.Drawing.RectangleF ([float]$mo), ([float]$mo), ([float]$ms), ([float]$ms)))
    $g.Dispose()
    return $b
}

# ICO: 256은 PNG 압축, 나머지는 32bpp DIB + AND 마스크(.NET Framework System.Drawing.Icon·Win7 호환).
function Write-Ico([string]$path, $bitmaps) {
    $entries = @()
    foreach ($bmp in $bitmaps) {
        $s = $bmp.Width
        $ms = New-Object IO.MemoryStream
        if ($s -ge 256) {
            $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        } else {
            $w = New-Object IO.BinaryWriter $ms
            $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
            1..6 | ForEach-Object { $w.Write([int]0) }
            for ($y = $s - 1; $y -ge 0; $y--) {
                for ($x = 0; $x -lt $s; $x++) {
                    $c = $bmp.GetPixel($x, $y)
                    $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
                }
            }
            $rowBytes = [Math]::Ceiling($s / 32) * 4
            for ($y = $s - 1; $y -ge 0; $y--) {
                $row = New-Object byte[] $rowBytes
                for ($x = 0; $x -lt $s; $x++) {
                    if ($bmp.GetPixel($x, $y).A -eq 0) { $row[[Math]::Floor($x / 8)] = $row[[Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
                }
                $w.Write($row)
            }
            $w.Flush()
        }
        $entries += , @($s, $ms.ToArray())
    }

    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $fs = New-Object IO.FileStream $path, 'Create'
    $w = New-Object IO.BinaryWriter $fs
    $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$entries.Count)
    $offset = 6 + 16 * $entries.Count
    foreach ($e in $entries) {
        $dim = if ($e[0] -ge 256) { 0 } else { $e[0] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$e[1].Length); $w.Write([int]$offset)
        $offset += $e[1].Length
    }
    foreach ($e in $entries) { $w.Write($e[1]) }
    $w.Close()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$bitmaps = $sizes | ForEach-Object { New-IconBitmap $_ }
Write-Ico $Out $bitmaps
if ($PreviewDir) {
    New-Item -ItemType Directory -Force $PreviewDir | Out-Null
    foreach ($b in $bitmaps) { $b.Save((Join-Path $PreviewDir "icon_$($b.Width).png")) }
}
"written: $Out"
