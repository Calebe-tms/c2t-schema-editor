# Gera c2t-schema-db-editor-Setup.exe na raiz do repositório (instalador único). Usa só o csc.exe que já vem no Windows.
# Nome, versão e editor ficam em Product.cs; o manifesto (compatibilidade, DPI) em app.manifest.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$web  = (Resolve-Path (Join-Path $root '..\dbml-viewer')).Path
$repo = (Resolve-Path (Join-Path $root '..')).Path   # o instalador sai na raiz do repositório
$obj  = Join-Path $root 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$product  = Join-Path $root 'Product.cs'
$manifest = Join-Path $root 'app.manifest'
$name = [regex]::Match((Get-Content $product -Raw), 'Name\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $name) { throw 'Nome do produto não encontrado em Product.cs' }

# ---- ícone: círculo em degradê com três camadas empilhadas, desenhado em cada tamanho (16 a 256) ----
Add-Type -AssemblyName System.Drawing
function IconPng($size) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256, $size / 256)   # o desenho é feito numa grade de 256
    # círculo com degradê em diagonal (índigo -> ciano)
    $grad = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 20, 20), (New-Object Drawing.Point 236, 236), ([Drawing.Color]::FromArgb(79, 70, 229)), ([Drawing.Color]::FromArgb(6, 182, 212))
    $g.FillEllipse($grad, 4, 4, 248, 248)
    # três camadas empilhadas (de baixo para cima, da mais transparente para a branca)
    $layers = @(@(170, 110), @(132, 175), @(94, 255))
    foreach ($l in $layers) {
        $brush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb($l[1], 255, 255, 255))
        $g.FillEllipse($brush, 56, $l[0] - 30, 144, 60)
    }
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
    return ,$ms.ToArray()
}
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = @{}; foreach ($s in $sizes) { $pngs[$s] = IconPng $s }
foreach ($s in $sizes) { [IO.File]::WriteAllBytes((Join-Path $obj "icon-$s.png"), $pngs[$s]) }   # para conferir o desenho; o de 256 vai na janela do instalador
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
$setup = Join-Path $repo "$name-Setup.exe"
& $csc /nologo /target:winexe /optimize "/win32icon:$ico" "/win32manifest:$manifest" "/out:$setup" /r:System.Windows.Forms.dll /r:System.Drawing.dll "/res:$appExe,App.exe" "/res:$obj\icon-256.png,icon.png" $product (Join-Path $root 'Setup.cs')
if ($LASTEXITCODE) { throw 'Falha ao compilar o instalador' }
Write-Host "Gerado: $setup ($([math]::Round((Get-Item $setup).Length/1KB)) KB)"
