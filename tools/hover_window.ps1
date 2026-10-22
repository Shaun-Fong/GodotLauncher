param(
    [string]$ProcessName = "GodotLauncher",
    [int]$X = 0,
    [int]$Y = 0
)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class HoverCursor {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$process = Get-Process -Name $ProcessName | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $process) { throw "Process $ProcessName has no window." }
$rect = New-Object HoverCursor+RECT
[HoverCursor]::GetWindowRect($process.MainWindowHandle, [ref]$rect) | Out-Null
[HoverCursor]::SetCursorPos($rect.Left + $X, $rect.Top + $Y) | Out-Null
"hovered $($rect.Left + $X),$($rect.Top + $Y)"
