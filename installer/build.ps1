# Gera dist\c2t-schema-db-editor-Setup.exe (instalador único). Usa só o csc.exe que já vem no Windows.
# Nome, versão e editor ficam em Product.cs; o manifesto (compatibilidade, DPI) em app.manifest.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$web  = (Resolve-Path (Join-Path $root '..\dbml-viewer')).Path
$dist = Join-Path $root '..\dist'
$obj  = Join-Path $root 'obj'
New-Item -ItemType Directory -Force $dist, $obj | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$product  = Join-Path $root 'Product.cs'
$manifest = Join-Path $root 'app.manifest'
$name = [regex]::Match((Get-Content $product -Raw), 'Name\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $name) { throw 'Nome do produto não encontrado em Product.cs' }

# ---- ícone: duas tabelas ligadas sobre um quadrado azul, desenhado em cada tamanho (16 a 256) ----
Add-Type -AssemblyName System.Drawing
function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath; $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}
function IconPng($size) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256, $size / 256)   # o desenho é feito numa grade de 256
    $bg = RoundRect 8 8 240 240 52
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0, 0), (New-Object Drawing.Point 0, 256), ([Drawing.Color]::FromArgb(59, 130, 246)), ([Drawing.Color]::FromArgb(29, 58, 138))
    $g.FillPath($grad, $bg)
    $white = New-Object Drawing.SolidBrush ([Drawing.Color]::White)
    $head  = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(191, 219, 254))
    $row   = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(148, 163, 184)), 7
    $row.StartCap = 'Round'; $row.EndCap = 'Round'
    # ligação em "L" entre as duas tabelas
    $link = New-Object Drawing.Pen ([Drawing.Color]::White), 12
    $link.LineJoin = 'Round'
    $g.DrawLines($link, [Drawing.Point[]]@((New-Object Drawing.Point 70, 124), (New-Object Drawing.Point 70, 176), (New-Object Drawing.Point 116, 176)))
    foreach ($t in @(@(36, 42), @(112, 134))) {
        $x = $t[0]; $y = $t[1]
        $g.FillPath($white, (RoundRect $x $y 108 84 10))
        $clip = $g.Clip; $g.SetClip((RoundRect $x $y 108 84 10)); $g.FillRectangle($head, $x, $y, 108, 26); $g.Clip = $clip
        if ($size -ge 40) {   # as linhas internas somem nos tamanhos pequenos, onde só virariam borrão
            $g.DrawLine($row, $x + 16, $y + 46, $x + 90, $y + 46); $g.DrawLine($row, $x + 16, $y + 66, $x + 68, $y + 66)
        }
    }
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
    return ,$ms.ToArray()
}
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = @{}; foreach ($s in $sizes) { $pngs[$s] = IconPng $s }
[IO.File]::WriteAllBytes((Join-Path $obj 'icon-256.png'), $pngs[256])   # só para conferir o desenho
$ico = Join-Path $obj 'app.ico'
$fs = [IO.File]::Create($ico); $bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $b = if ($s -eq 256) { 0 } else { $s }   # no .ico, 0 significa 256
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$s].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$s].Length
}
foreach ($s in $sizes) { $bw.Write([byte[]]$pngs[$s]) }
$bw.Close()

# ---- 1) o app (servidor + páginas embutidas) ----
$res = @('/res:' + (Join-Path $web 'viewer.html') + ',viewer.html')
$jsRoot = (Resolve-Path (Join-Path $web 'js')).Path
# recursivo: js\*.js e js\drizzle\*.js (o nome do recurso mantém a subpasta, ex.: js/drizzle/ts-parser.js)
Get-ChildItem $jsRoot -Filter *.js -Recurse | ForEach-Object { $res += '/res:' + $_.FullName + ',js/' + $_.FullName.Substring($jsRoot.Length + 1).Replace('\', '/') }
$appExe = Join-Path $obj "$name.exe"
& $csc /nologo /target:winexe /optimize "/win32icon:$ico" "/win32manifest:$manifest" "/out:$appExe" /r:System.Windows.Forms.dll @res $product (Join-Path $root 'App.cs')
if ($LASTEXITCODE) { throw 'Falha ao compilar o app' }

# ---- 2) o instalador (embute o app e o ícone, que ele mostra na própria janela) ----
$setup = Join-Path $dist "$name-Setup.exe"
& $csc /nologo /target:winexe /optimize "/win32icon:$ico" "/win32manifest:$manifest" "/out:$setup" /r:System.Windows.Forms.dll /r:System.Drawing.dll "/res:$appExe,App.exe" "/res:$obj\icon-256.png,icon.png" $product (Join-Path $root 'Setup.cs')
if ($LASTEXITCODE) { throw 'Falha ao compilar o instalador' }
Write-Host "Gerado: $setup ($([math]::Round((Get-Item $setup).Length/1KB)) KB)"
