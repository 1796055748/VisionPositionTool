Add-Type -AssemblyName System.Drawing
$src = 'D:\cSharp\资料\项目\tool\VisionPositionTool\testdata\text.bmp'
$bmp = New-Object System.Drawing.Bitmap($src)

# ROI 区域（验证程序的 ROI：row 824..1224, col 1024..1424，注意 System.Drawing 中 y=row, x=col）
$r1 = 824; $c1 = 1024; $r2 = 1224; $c2 = 1424

# LockBits 读整个位图（8bpp 索引色，直接读像素字节）
$rect = New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format8bppIndexed)
$stride = $data.Stride
$buf = New-Object byte[] ($stride * $bmp.Height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
$bmp.UnlockBits($data)

# 8bpp 索引色：取调色板亮度
$pal = $bmp.Palette.Entries

function Get-Gray($x, $y) {
    $idx = $buf[$y * $stride + $x]
    $e = $pal[$idx]
    return [int](0.3 * $e.R + 0.59 * $e.G + 0.11 * $e.B)
}

# downsample 到 100x50，输出 ASCII（亮=字符稠密）
$cols = 100; $rows = 50
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
        if ($g -gt 200) { $line += '@' }
        elseif ($g -gt 150) { $line += '#' }
        elseif ($g -gt 100) { $line += '+' }
        elseif ($g -gt 60) { $line += '.' }
        else { $line += ' ' }
    }
    Write-Output $line
}
$bmp.Dispose()
