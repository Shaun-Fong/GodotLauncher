# Captures the main window of a running process into a PNG file.
# Usage: pwsh -File tools/capture_window.ps1 -ProcessName GodotLauncher -Output artifacts/window.png
param(
    [string]$ProcessName = 'GodotLauncher',
    [string]$Output = 'artifacts/window.png',
    [int]$TimeoutMs = 15000
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class WindowCaptureNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
$process = $null
$handle = [IntPtr]::Zero

while ([DateTime]::UtcNow -lt $deadline) {
    $process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($process) {
        $handle = $process.MainWindowHandle
        break
    }

    Start-Sleep -Milliseconds 200
}

if ($handle -eq [IntPtr]::Zero) {
    throw "No window found for process '$ProcessName'."
}

[void][WindowCaptureNative]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds 600

$rect = New-Object WindowCaptureNative+RECT
if (-not [WindowCaptureNative]::GetWindowRect($handle, [ref]$rect)) {
    throw 'GetWindowRect failed.'
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) {
    throw "Invalid window size ${width}x${height}."
}

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
try {
    if (-not [WindowCaptureNative]::PrintWindow($handle, $hdc, 2)) {
        throw 'PrintWindow failed.'
    }
}
finally {
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
}

$directory = Split-Path -Parent $Output
if ($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$bitmap.Save((Resolve-Path -LiteralPath (Split-Path -Parent $Output)).Path + '\' + (Split-Path -Leaf $Output), [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Output "Saved $Output (${width}x${height})."
