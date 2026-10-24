# Genera assets\app.ico (icono de la aplicación) con el mismo estilo que el de la bandeja:
# insignia verde con esquinas redondeadas y una pila con su nivel.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'assets\app.ico'
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

function New-IconPng([int]$size) {
  $k = $size / 16.0
  $bmp = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
  $green = [System.Drawing.ColorTranslator]::FromHtml('#32D74B')
  $dark = [System.Drawing.ColorTranslator]::FromHtml('#06280D')

  $d = 6 * $k
  $badge = New-Object System.Drawing.Drawing2D.GraphicsPath
  $badge.AddArc(0, 0, $d, $d, 180, 90); $badge.AddArc($size - $d, 0, $d, $d, 270, 90)
  $badge.AddArc($size - $d, $size - $d, $d, $d, 0, 90); $badge.AddArc(0, $size - $d, $d, $d, 90, 90); $badge.CloseFigure()
  $g.FillPath((New-Object System.Drawing.SolidBrush $green), $badge)

  # Pila horizontal: cuerpo 11x7 con borde, polo a la derecha y relleno al 75 %.
  $pen = New-Object System.Drawing.Pen $dark, ([math]::Max(1, 1.2 * $k))
  $g.DrawRectangle($pen, [float](2 * $k), [float](4.5 * $k), [float](10.5 * $k), [float](7 * $k))
  $g.FillRectangle((New-Object System.Drawing.SolidBrush $dark), [float](12.5 * $k), [float](6.5 * $k), [float](1.5 * $k), [float](3 * $k))
  $g.FillRectangle((New-Object System.Drawing.SolidBrush $dark), [float](3.5 * $k), [float](6 * $k), [float](7 * $k), [float](4 * $k))
  $g.Dispose()
  return $bmp
}

# Imagen de un ICO: PNG para 256 px y, para el resto, mapa de bits de 32 bits (BITMAPINFOHEADER + píxeles
# BGRA de abajo arriba + máscara AND vacía), que es lo que entienden todos los lectores de iconos.
function ConvertTo-IconImage([System.Drawing.Bitmap]$bmp) {
  $s = $bmp.Width
  $ms = New-Object System.IO.MemoryStream
  if ($s -ge 256) {
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
  }
  $w = New-Object System.IO.BinaryWriter $ms
  $w.Write([uint32]40); $w.Write([int32]$s); $w.Write([int32](2 * $s)); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]0); $w.Write([uint32]0); $w.Write([int32]0); $w.Write([int32]0); $w.Write([uint32]0); $w.Write([uint32]0)
  $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
  $data = $bmp.LockBits($rect, 'ReadOnly', [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $pixels = New-Object byte[] ($data.Stride * $s)
  [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
  $bmp.UnlockBits($data)
  for ($y = $s - 1; $y -ge 0; $y--) { $w.Write($pixels, $y * $data.Stride, $s * 4) }
  $maskRow = [math]::Ceiling($s / 32) * 4
  $w.Write((New-Object byte[] ($maskRow * $s)))
  $w.Flush()
  return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = @($sizes | ForEach-Object { $b = New-IconPng $_; , (ConvertTo-IconImage $b); $b.Dispose() })
$file = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $file
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $png = $images[$i]
  $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))   # 256 se escribe como 0
  $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$png.Length); $w.Write([uint32]$offset)
  $offset += $png.Length
}
foreach ($png in $images) { $w.Write($png) }
$w.Flush()
[System.IO.File]::WriteAllBytes($out, $file.ToArray())
"Icono creado: $out"
