Add-Type -AssemblyName System.Drawing
$src = 'D:\cSharp\资料\项目\tool\VisionPositionTool\testdata\text.bmp'
$full = [System.Drawing.Image]::FromFile($src)

# 裁切 ROI 区域（对应验证程序里的 ROI：r[824,1224] c[1024,1424]）
$crop = New-Object System.Drawing.Bitmap(400, 400)
$g = [System.Drawing.Graphics]::FromImage($crop)
$destRect = New-Object System.Drawing.Rectangle(0, 0, 400, 400)
$srcRect = New-Object System.Drawing.Rectangle(1024, 824, 400, 400)
$g.DrawImage($full, $destRect, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$crop.Save('D:\cSharp\资料\项目\tool\.testout\roi-crop.png', [System.Drawing.Imaging.ImageFormat]::Png)
$crop.Dispose()

# 全图缩略 1/2 另存
$half = New-Object System.Drawing.Bitmap(1224, 1024)
$g2 = [System.Drawing.Graphics]::FromImage($half)
$d2 = New-Object System.Drawing.Rectangle(0, 0, 1224, 1024)
$s2 = New-Object System.Drawing.Rectangle(0, 0, 2448, 2048)
$g2.DrawImage($full, $d2, $s2, [System.Drawing.GraphicsUnit]::Pixel)
$g2.Dispose()
$half.Save('D:\cSharp\资料\项目\tool\.testout\text-half.png', [System.Drawing.Imaging.ImageFormat]::Png)
$half.Dispose()
$full.Dispose()
Write-Output "cropped"
