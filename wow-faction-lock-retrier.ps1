# WoW character creation retrier
# Whenever the "This faction is currently unavailable..." popup appears, it clicks Okay and retries Finish.
# When the popup no longer appears (character created), it stops and beeps.
#
# Usage: double-click wow-faction-lock-retrier.bat  (or: powershell -ExecutionPolicy Bypass -File wow-faction-lock-retrier.ps1 -IntervalSec 30)
# Stop:  press Ctrl+C in this console window

param(
    [int]$IntervalSec = 30,     # wait between attempts (seconds)
    [int]$MaxAttempts = 0,      # 0 = unlimited
    [int]$Tolerance   = 40      # color match tolerance (per channel, 0-255)
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")]  public static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
}
"@
[W]::SetProcessDPIAware() | Out-Null

function Get-Pos { $p = New-Object W+POINT; [W]::GetCursorPos([ref]$p) | Out-Null; $p }
function Get-Color([int]$x, [int]$y) {
    $dc = [W]::GetDC([IntPtr]::Zero); $c = [W]::GetPixel($dc, $x, $y); [W]::ReleaseDC([IntPtr]::Zero, $dc) | Out-Null
    @{ R = $c -band 0xFF; G = ($c -shr 8) -band 0xFF; B = ($c -shr 16) -band 0xFF }
}
function Test-Color($a, $b) {
    [math]::Abs($a.R - $b.R) -le $Tolerance -and [math]::Abs($a.G - $b.G) -le $Tolerance -and [math]::Abs($a.B - $b.B) -le $Tolerance
}
function Click-At([int]$x, [int]$y) {
    [W]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 150
    [W]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 80   # left button down
    [W]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)                                  # left button up
}
function Countdown([string]$msg, [int]$sec = 6) {
    Write-Host $msg -ForegroundColor Yellow
    for ($i = $sec; $i -gt 0; $i--) { Write-Host "  $i..." -NoNewline; Start-Sleep 1 }
    Write-Host ""
}
function Focus-WoW {
    $p = Get-Process | Where-Object { $_.MainWindowTitle -like "*World of Warcraft*" } | Select-Object -First 1
    if ($p) { [W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 300 }
}

Write-Host "=== Calibration ===" -ForegroundColor Cyan
Write-Host "The game must be in WINDOWED or 'Windowed (Fullscreen)' mode (pixel colors may not be readable in exclusive fullscreen)."
Write-Host "Set up your character and click Finish once by hand so the popup stays open.`n"

Countdown "Move the mouse to the CENTER of the popup's 'Okay' button and leave it there (position will be saved)"
$okay = Get-Pos
# Move the cursor away so we read the normal color, not the hover highlight
[W]::SetCursorPos($okay.X, $okay.Y - 200) | Out-Null; Start-Sleep -Milliseconds 400
$okayColor = Get-Color $okay.X $okay.Y
Write-Host ("Okay: ({0},{1})  color R{2} G{3} B{4}" -f $okay.X, $okay.Y, $okayColor.R, $okayColor.G, $okayColor.B)

Countdown "Now click 'Okay' by hand to close the popup, then move the mouse to the CENTER of the 'Finish' button"
$finish = Get-Pos
Write-Host ("Finish: ({0},{1})" -f $finish.X, $finish.Y)

if (Test-Color (Get-Color $okay.X $okay.Y) $okayColor) {
    Write-Host "ERROR: The same color is read even with the popup closed; detection would be unreliable. Pick a point closer to the red part of the Okay button." -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Starting (every $IntervalSec s). Press Ctrl+C to stop ===`n" -ForegroundColor Cyan
$attempt = 0
while ($true) {
    $attempt++
    Focus-WoW
    Click-At $finish.X $finish.Y
    [W]::SetCursorPos($finish.X, $finish.Y - 200) | Out-Null   # move cursor off the buttons

    # Wait for the server response: does the popup appear within 10 s?
    $blocked = $false
    for ($t = 0; $t -lt 20; $t++) {
        Start-Sleep -Milliseconds 500
        if (Test-Color (Get-Color $okay.X $okay.Y) $okayColor) { $blocked = $true; break }
    }

    if (-not $blocked) {
        Write-Host ("[{0:HH:mm:ss}] Attempt #{1}: popup did NOT appear - the character may have been created! Stopping." -f (Get-Date), $attempt) -ForegroundColor Green
        1..5 | ForEach-Object { [console]::Beep(880, 300); Start-Sleep -Milliseconds 150 }
        break
    }

    Write-Host ("[{0:HH:mm:ss}] Attempt #{1}: still locked, retrying in {2} s." -f (Get-Date), $attempt, $IntervalSec)
    Click-At $okay.X $okay.Y
    [W]::SetCursorPos($okay.X, $okay.Y - 200) | Out-Null

    if ($MaxAttempts -gt 0 -and $attempt -ge $MaxAttempts) { Write-Host "Maximum number of attempts reached."; break }
    Start-Sleep -Seconds $IntervalSec
}
Read-Host "Press Enter to close"
