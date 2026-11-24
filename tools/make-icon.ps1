# Builds spacewise.ico from the Figma tile (assets/icon-figma.png).
# The Figma export has a light-grey square behind the rounded tile; this cuts the corners
# to transparent (anti-aliased), then packs 16-256 px sizes into one .ico.
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing, System.Drawing.Primitives -TypeDefinition @"
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class Tile {
  // coverage of a rounded square (side n, corner radius r) over pixel (x,y), 8x8 samples
  static double Cover(int x, int y, int n, double r) {
    int hit = 0;
    for (int i = 0; i < 8; i++) for (int j = 0; j < 8; j++) {
      double px = x + (i + 0.5) / 8, py = y + (j + 0.5) / 8;
      double cx = Math.Min(Math.Max(px, r), n - r), cy = Math.Min(Math.Max(py, r), n - r);
      double dx = px - cx, dy = py - cy;
      if (dx * dx + dy * dy <= r * r) hit++;
    }
    return hit / 64.0;
  }
  public static Bitmap Cut(Bitmap src, double r) {
    int n = src.Width;
    var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, n, n);
    var d = bmp.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var px = new int[n * n]; Marshal.Copy(d.Scan0, px, 0, px.Length);
    var outp = (int[])px.Clone();
    int band = (int)Math.Ceiling(r) + 1;
    for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) {
      bool corner = (x < band || x >= n - band) && (y < band || y >= n - band);
      if (!corner) continue;
      double c = Cover(x, y, n, r);
      if (c >= 1) continue;
      // edge pixels are mixed with the grey; take the colour from 3 px further inside
      int ix = x < n / 2 ? x + 3 : x - 3, iy = y < n / 2 ? y + 3 : y - 3;
      int rgb = px[iy * n + ix] & 0xFFFFFF;
      outp[y * n + x] = ((int)Math.Round(255 * c) << 24) | rgb;
    }
    Marshal.Copy(outp, 0, d.Scan0, outp.Length); bmp.UnlockBits(d);
    return bmp;
  }
}
"@
$dir = (Resolve-Path (Join-Path $PSScriptRoot '..\assets')).Path
$src = [System.Drawing.Bitmap]::FromFile((Join-Path $dir 'icon-figma.png'))
$radius = 57 * $src.Width / 256   # measured on the 256 px export
$tile = [Tile]::Cut($src, $radius)
$src.Dispose()
$tile.Save((Join-Path $dir 'icon-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)

function Png([System.Drawing.Bitmap]$img, [int]$s) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
  $g.Clear([System.Drawing.Color]::Transparent)
  $g.DrawImage($img, 0, 0, $s, $s); $g.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  return ,$ms.ToArray()
}
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @($sizes | ForEach-Object { ,(Png $tile $_) })
$tile.Dispose()
# ICO: header, one 16-byte entry per image, then the PNG data
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i] % 256
  $w.Write([byte]$s); $w.Write([byte]$s); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
  $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
[System.IO.File]::WriteAllBytes((Join-Path $dir 'spacewise.ico'), $out.ToArray())
"ico: $($sizes -join ', ') px"
