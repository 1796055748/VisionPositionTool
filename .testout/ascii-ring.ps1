Add-Type -AssemblyName System.Drawing
$src = 'D:\cSharp\资料\项目\tool\VisionPositionTool\testdata\text.bmp'
$bmp = New-Object System.Drawing.Bitmap($src)

# 盘内圈区域（全图 ASCII 行 13-30 列 58-110 附近 → 像素：row 300..800, col 850..1700）
$r1 = 300; $c1 = 850; $r2 = 800; $c2 = 1700

$rect = New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format8bppIndexed)
$stride = $data.Stride
$buf = New-Object byte[] ($stride * $bmp.Height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
$bmp.UnlockBits($data)
$pal = $bmp.Palette.Entries

function Get-Gray($x, $y) {
    $e = $pal[$buf[$y * $stride + $x]]
    return [int](0.3 * $e.R + 0.59 * $e.G + 0.11 * $e.B)
}

$cols = 130; $rows = 60
$cellW = ($c2 - $c1) / $cols; $cellH = ($r2 - $r1) / $rows
for ($yy = 0; $yy -lt $rows; $yy++) {
    $line = ''
    for ($xx = 0; $xx -lt $cols; $xx++) {
        $sum = 0; $n = 0
        $sx = [int]($c1 + $xx * $cellW); $ex = [int]($c1 + ($xx + 1) * $cellW)
        $sy = [int]($r1 + $yy * $cellH); $ey = [int]($r1 + ($yy + 1) * $cellH)
        for ($y = $sy; $y -lt $ey; $y += 2) {
            for ($x = $sx; $x -lt $ex; $x += 2) {
                $sum += (Get-Gray $x $y); $n++
            }
        }
        $g = $sum / [Math]::Max(1, $n)
        if ($g -gt 230) { $line += '@' }
        elseif ($g -gt 190) { $line += '#' }
        elseif ($g -gt 150) { $line += '+' }
        elseif ($g -gt 100) { $line += '.' }
        elseif ($g -gt 50) { $line += ':' }
        else { $line += ' ' }
    }
    Write-Output $line
}
$bmp.Dispose()
