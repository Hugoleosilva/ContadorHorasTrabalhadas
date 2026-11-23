$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$assetsDir = Join-Path $root 'assets'
$icoPath = Join-Path $assetsDir 'app.ico'
$outExe = Join-Path $distDir 'RelogioAcompanhamento.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) {
    throw "Compilador C# nativo nao encontrado em: $csc"
}

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}
if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir | Out-Null
}

# Gera icone .ico leve para embutir no executavel (Taskbar / Desktop)
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap(64, 64)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(24, 26, 32))
$ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(34, 197, 94), 6.0)
$handPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(248, 250, 252), 5.0)
$handPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$handPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(34, 197, 94))

$g.FillEllipse($bgBrush, 4, 4, 56, 56)
$g.DrawEllipse($ringPen, 6, 6, 52, 52)
$g.DrawLine($handPen, 32, 32, 32, 16)
$g.DrawLine($handPen, 32, 32, 46, 32)
$g.FillEllipse($dotBrush, 28, 28, 8, 8)

$msPng = New-Object System.IO.MemoryStream
$bmp.Save($msPng, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $msPng.ToArray()

$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
# ICONDIR header (6 bytes) + ICONDIRENTRY (16 bytes) + PNG payload
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]1)
$bw.Write([Byte]64)
$bw.Write([Byte]64)
$bw.Write([Byte]0)
$bw.Write([Byte]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]32)
$bw.Write([UInt32]$pngBytes.Length)
$bw.Write([UInt32]22)
$bw.Write($pngBytes)
$bw.Flush()
$bw.Close()
$fs.Close()
$msPng.Dispose()
$dotBrush.Dispose()
$handPen.Dispose()
$ringPen.Dispose()
$bgBrush.Dispose()
$g.Dispose()
$bmp.Dispose()

$sources = Get-ChildItem -Path $srcDir -Filter '*.cs' | Select-Object -ExpandProperty FullName

$argsList = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    '/warn:4',
    '/codepage:65001',
    "/win32icon:$icoPath",
    "/out:$outExe",
    '/reference:System.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
) + $sources

& $csc @argsList
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$sizeKb = [Math]::Round((Get-Item $outExe).Length / 1024, 1)
Write-Host "Build concluido com sucesso: $outExe ($sizeKb KB)" -ForegroundColor Green
