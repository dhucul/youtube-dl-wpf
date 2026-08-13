$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class IconNativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr handle);
}
'@

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $repositoryRoot 'src\YoutubeDlModern\Assets'
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null

$bitmap = New-Object System.Drawing.Bitmap 64, 64
$graphics = $null
$shape = $null
$icon = $null
$stream = $null
$iconHandle = [IntPtr]::Zero
try {
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.Clear([System.Drawing.Color]::FromArgb(224, 57, 75))
    $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
    $points = [System.Drawing.Point[]]@(
        (New-Object System.Drawing.Point(24, 16)),
        (New-Object System.Drawing.Point(24, 48)),
        (New-Object System.Drawing.Point(46, 32))
    )
    $shape.AddPolygon($points)
    $graphics.FillPath([System.Drawing.Brushes]::White, $shape)

    $iconHandle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
    $outputPath = Join-Path $assetDirectory 'app.ico'
    $stream = [System.IO.File]::Create($outputPath)
    $icon.Save($stream)
    $stream.Flush($true)
}
finally {
    if ($null -ne $stream) { $stream.Dispose() }
    if ($null -ne $icon) { $icon.Dispose() }
    if ($iconHandle -ne [IntPtr]::Zero) { [void][IconNativeMethods]::DestroyIcon($iconHandle) }
    if ($null -ne $shape) { $shape.Dispose() }
    if ($null -ne $graphics) { $graphics.Dispose() }
    $bitmap.Dispose()
}

Write-Output ("icon written: " + (Get-Item -LiteralPath $outputPath).Length + " bytes")
