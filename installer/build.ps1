# Gera dist\DBML-Editor-Setup.exe (instalador único). Usa só o csc.exe que já vem no Windows.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$web  = (Resolve-Path (Join-Path $root '..\dbml-viewer')).Path
$dist = Join-Path $root '..\dist'
$obj  = Join-Path $root 'obj'
New-Item -ItemType Directory -Force $dist, $obj | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

# ícone (duas tabelas ligadas) gerado na hora: PNG 256 dentro de um .ico
Add-Type -AssemblyName System.Drawing
$bmp = New-Object Drawing.Bitmap 256, 256
$g = [Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::Transparent)
$blue = [Drawing.Color]::FromArgb(37, 99, 235)
function Box($x, $y) {
    $g.FillRectangle((New-Object Drawing.SolidBrush ([Drawing.Color]::White)), $x, $y, 104, 88)
    $g.FillRectangle((New-Object Drawing.SolidBrush $blue), $x, $y, 104, 28)
    $g.DrawRectangle((New-Object Drawing.Pen $blue, 6), $x, $y, 104, 88)
    $line = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(160, 174, 192)), 8
    $g.DrawLine($line, $x + 14, $y + 48, $x + 88, $y + 48); $g.DrawLine($line, $x + 14, $y + 68, $x + 66, $y + 68)
}
$g.DrawLine((New-Object Drawing.Pen $blue, 8), 110, 70, 146, 186)
Box 14 26; Box 138 142
$ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $png = $ms.ToArray()
$ico = Join-Path $obj 'app.ico'
$fs = [IO.File]::Create($ico); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)
$bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$png.Length); $bw.Write([uint32]22)
$bw.Write($png); $bw.Close()

# 1) o app (servidor + páginas embutidas)
$res = @('/res:' + (Join-Path $web 'viewer.html') + ',viewer.html')
$jsRoot = (Resolve-Path (Join-Path $web 'js')).Path
# recursivo: js\*.js e js\drizzle\*.js (o nome do recurso mantém a subpasta, ex.: js/drizzle/ts-parser.js)
Get-ChildItem $jsRoot -Filter *.js -Recurse | ForEach-Object { $res += '/res:' + $_.FullName + ',js/' + $_.FullName.Substring($jsRoot.Length + 1).Replace('\', '/') }
$appExe = Join-Path $obj 'DbmlEditor.exe'
& $csc /nologo /target:winexe /optimize "/win32icon:$ico" "/out:$appExe" /r:System.Windows.Forms.dll @res (Join-Path $root 'App.cs')
if ($LASTEXITCODE) { throw 'Falha ao compilar o app' }

# 2) o instalador (embute o app)
$setup = Join-Path $dist 'DBML-Editor-Setup.exe'
& $csc /nologo /target:winexe /optimize "/win32icon:$ico" "/out:$setup" /r:System.Windows.Forms.dll "/res:$appExe,DbmlEditor.exe" (Join-Path $root 'Setup.cs')
if ($LASTEXITCODE) { throw 'Falha ao compilar o instalador' }
Write-Host "Gerado: $setup ($([math]::Round((Get-Item $setup).Length/1KB)) KB)"
