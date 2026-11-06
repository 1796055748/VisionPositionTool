Add-Type -AssemblyName System.Drawing
$src = 'D:\cSharp\资料\项目\tool\VisionPositionTool\testdata\text.bmp'
$dst = 'D:\cSharp\资料\项目\tool\.testout\text.png'
$img = [System.Drawing.Image]::FromFile($src)
Write-Output ("size: " + $img.Width + "x" + $img.Height + " format: " + $img.PixelFormat)
$img.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png)
$img.Dispose()
Write-Output "saved"
