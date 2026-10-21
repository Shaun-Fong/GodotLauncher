# Clicks inside a running window at window-relative coordinates.
# Usage: powershell -File tools/click_window.ps1 -ProcessName GodotLauncher -X 70 -Y 208
param(
    [string]$ProcessName = 'GodotLauncher',
    [int]$X = 0,
    [int]$Y = 0
)

$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class WindowClickNative
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
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$process = Get-Process -Name $ProcessName -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $process) {
    throw "No window found for process '$ProcessName'."
}

$handle = $process.MainWindowHandle
[void][WindowClickNative]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds 200

$rect = New-Object WindowClickNative+RECT
if (-not [WindowClickNative]::GetWindowRect($handle, [ref]$rect)) {
    throw 'GetWindowRect failed.'
}

$screenX = $rect.Left + $X
$screenY = $rect.Top + $Y
[void][WindowClickNative]::SetCursorPos($screenX, $screenY)
Start-Sleep -Milliseconds 120

[WindowClickNative]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[WindowClickNative]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)

Write-Output "Clicked ${screenX},${screenY}."
