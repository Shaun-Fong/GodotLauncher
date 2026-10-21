# Scrolls the window under the cursor with the mouse wheel.
param(
    [string]$ProcessName = "GodotLauncher",
    [int]$X = 600,
    [int]$Y = 400,
    [int]$Clicks = -3
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WheelInput
{
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$process = Get-Process -Name $ProcessName -ErrorAction Stop | Select-Object -First 1
$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) {
    throw "No main window for $ProcessName"
}

$rect = New-Object WheelInput+RECT
[WheelInput]::GetWindowRect($handle, [ref]$rect) | Out-Null
$screenX = $rect.Left + $X
$screenY = $rect.Top + $Y

[WheelInput]::SetCursorPos($screenX, $screenY) | Out-Null
Start-Sleep -Milliseconds 150
for ($i = 0; $i -lt [Math]::Abs($Clicks); $i++) {
    [WheelInput]::mouse_event(0x0800, 0, 0, [Math]::Sign($Clicks) * 120, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
}

Write-Host "Scrolled $Clicks at $screenX,$screenY"
