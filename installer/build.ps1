# Gera setup.exe na raiz do repositório (instalador único). Usa só o csc.exe que já vem no Windows.
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

# ---- ícone: círculo escuro com um banco de dados e um "c" como expoente, desenhado em cada tamanho (16 a 256) ----
Add-Type -AssemblyName System.Drawing
function IconPng($size) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256, $size / 256)   # o desenho é feito numa grade de 256
    # círculo escuro, chapado (sem degradê nem efeitos)
    $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(15, 18, 22))), 4, 4, 248, 248)
    # ícone de banco de dados: três discos empilhados (cada um é elipse de cima + corpo + elipse de baixo), em dois tons de ciano
    $light = [Drawing.Color]::FromArgb(86, 204, 242); $mid = [Drawing.Color]::FromArgb(24, 150, 214)
    $darkBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(15, 18, 22))
    $x = 44; $w = 120; $ry = 15; $h = 34; $gap = if ($size -lt 32) { 12 } else { 8 }
    # desenha de baixo para cima: cada disco leva um contorno escuro (o corte) por cima do que está embaixo
    $discs = @(@(154, $mid), @(112, $mid), @(70, $light))
    foreach ($d in $discs) {
        foreach ($pass in @($gap, 0)) {
            $r = New-Object Drawing.Region (New-Object Drawing.RectangleF ($x - $pass), $d[0], ($w + 2 * $pass), $h)
            foreach ($cy in @($d[0], ($d[0] + $h))) {
                $p = New-Object Drawing.Drawing2D.GraphicsPath
                $p.AddEllipse(($x - $pass), ($cy - $ry - $pass), ($w + 2 * $pass), (2 * $ry + 2 * $pass)); $r.Union($p)
            }
            $g.FillRegion($(if ($pass) { $darkBrush } else { New-Object Drawing.SolidBrush $d[1] }), $r)
        }
    }
    # "c" pequeno no canto de cima, como um expoente (banco elevado a c): arco grosso aberto à direita; some nos tamanhos minúsculos
    if ($size -ge 32) {
        $g.DrawArc((New-Object Drawing.Pen $light, 10), 174, 46, 36, 36, 40, 280)
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
$setup = Join-Path $repo 'setup.exe'
& $csc /nologo /target:winexe /optimize "/win32icon:$ico" "/win32manifest:$manifest" "/out:$setup" /r:System.Windows.Forms.dll /r:System.Drawing.dll "/res:$appExe,App.exe" "/res:$obj\icon-256.png,icon.png" $product (Join-Path $root 'Setup.cs')
if ($LASTEXITCODE) { throw 'Falha ao compilar o instalador' }
Write-Host "Gerado: $setup ($([math]::Round((Get-Item $setup).Length/1KB)) KB)"
