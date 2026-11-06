Add-Type -AssemblyName System.Drawing
$src = 'D:\cSharp\资料\项目\tool\VisionPositionTool\testdata\text.bmp'
$bmp = New-Object System.Drawing.Bitmap($src)

# LockBits 读像素字节（8bpp 索引色）
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

# 全图 downsample 160x80
$cols = 160; $rows = 80
$cellW = $bmp.Width / $cols; $cellH = $bmp.Height / $rows
for ($yy = 0; $yy -lt $rows; $yy++) {
    $line = ''
    for ($xx = 0; $xx -lt $cols; $xx++) {
        $sum = 0; $n = 0
        $sx = [int]($xx * $cellW); $ex = [int](($xx + 1) * $cellW)
        $sy = [int]($yy * $cellH); $ey = [int](($yy + 1) * $cellH)
        for ($y = $sy; $y -lt $ey; $y += 4) {
            for ($x = $sx; $x -lt $ex; $x += 4) {
                $sum += (Get-Gray $x $y); $n++
            }
        }
        $g = $sum / [Math]::Max(1, $n)
        if ($g -gt 220) { $line += '@' }
        elseif ($g -gt 180) { $line += '#' }
        elseif ($g -gt 130) { $line += '+' }
        elseif ($g -gt 80) { $line += '.' }
        else { $line += ' ' }
    }
    Write-Output $line
}
$bmp.Dispose()
