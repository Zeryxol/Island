# .\build.ps1                 -> bin\Island.exe
# .\build.ps1 -Version 1.2.0  -> same, with the version stamped into the exe
param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force bin, obj | Out-Null

# icon: light rounded tile with a black pill and a green dot
$png = New-Object System.IO.MemoryStream
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, $r * 2, $r * 2, 180, 90)
    $p.AddArc($x + $w - $r * 2, $y, $r * 2, $r * 2, 270, 90)
    $p.AddArc($x + $w - $r * 2, $y + $h - $r * 2, $r * 2, $r * 2, 0, 90)
    $p.AddArc($x, $y + $h - $r * 2, $r * 2, $r * 2, 90, 90)
    $p.CloseFigure(); $p
}
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 8), (New-Object System.Drawing.Point 0, 248), ([System.Drawing.Color]::FromArgb(242, 242, 247)), ([System.Drawing.Color]::FromArgb(199, 199, 204))
$g.FillPath($grad, (RoundRect 8 8 240 240 56))
$g.FillPath([System.Drawing.Brushes]::Black, (RoundRect 44 98 168 60 30))
$g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(48, 209, 88))), 170, 118, 20, 20)
$g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(58, 58, 60))), 68, 120, 16, 16)
$g.Dispose()
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bytes = $png.ToArray()
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]1)
$w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)
$w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$bytes.Length); $w.Write([uint32]22)
$w.Write($bytes); $w.Flush()
[System.IO.File]::WriteAllBytes("$PSScriptRoot\obj\island.ico", $ico.ToArray())

Set-Content -Encoding UTF8 obj\AssemblyInfo.cs @"
using System.Reflection;
[assembly: AssemblyTitle("Island")]
[assembly: AssemblyProduct("Island")]
[assembly: AssemblyVersion("$Version")]
[assembly: AssemblyFileVersion("$Version")]
"@

$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wm = 'C:\Windows\System32\WinMetadata'
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /out:bin\Island.exe /win32icon:obj\island.ico /r:System.Core.dll `
    /r:"$fw\WPF\PresentationFramework.dll" /r:"$fw\WPF\PresentationCore.dll" /r:"$fw\WPF\WindowsBase.dll" `
    /r:"$fw\System.Xaml.dll" /r:"$fw\System.Runtime.WindowsRuntime.dll" /r:"$fw\System.Runtime.dll" `
    /r:$wm\Windows.UI.winmd /r:$wm\Windows.Foundation.winmd /r:$wm\Windows.ApplicationModel.winmd `
    /r:$wm\Windows.Media.winmd /r:$wm\Windows.Storage.winmd /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    obj\AssemblyInfo.cs src\*.cs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "bin\Island.exe $Version"
