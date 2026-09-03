<#
.SYNOPSIS
    Moves the pointer inside one window of a running DeskNote instance, optionally clicking.

.DESCRIPTION
    For the screen photographs: a note's chrome only appears once the pointer is actually inside
    the window, and the pet dashboard only opens when the pet is clicked.

    Deliberately narrow. The target point is computed from the window's own rect and is refused if
    it falls outside it, so this cannot click anything but the window it was pointed at. The
    pointer is put back where it was when the script finishes.

.EXAMPLE
    ./tools/point-window.ps1 -ProcessId 1234 -TitleLike '회의' -FractionX 0.5 -FractionY 0.5 -Click
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $ProcessId,
    [Parameter(Mandatory)] [string] $TitleLike,

    # The pet window and the dashboard share a title, so the caller can name the exact window.
    [long] $Handle = 0,
    [double] $FractionX = 0.5,
    [double] $FractionY = 0.5,
    [switch] $Click,
    [switch] $KeepPointer,
    [int] $SettleMilliseconds = 900
)

$ErrorActionPreference = 'Stop'

# focus-window.ps1 is dot-sourced for its window helpers; it also brings the target to the front,
# which is what keeps the click from landing on whatever was above it.
. "$PSScriptRoot\focus-window.ps1" -ProcessId $ProcessId -TitleLike $TitleLike | Out-Null

if ($Handle -ne 0) {
    [void][DeskNote.Windows]::BringWindowToTop([IntPtr] $Handle)
    [void][DeskNote.Windows]::SetForegroundWindow([IntPtr] $Handle)
    Start-Sleep -Milliseconds 500
}

if (-not ('DeskNote.Pointer' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace DeskNote
{
    public static class Pointer
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

        /// <summary>
        /// Moves by injecting an input event rather than SetCursorPos.
        /// </summary>
        /// <remarks>
        /// WinUI reacts to the input queue, not to the cursor simply being somewhere: warping the
        /// pointer with SetCursorPos moves it on screen without the window ever seeing a pointer
        /// enter, so the note chrome never appears.
        /// </remarks>
        public static void MoveTo(int x, int y)
        {
            var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            var dx = (uint)Math.Round((x - left) * 65535.0 / Math.Max(1, width - 1));
            var dy = (uint)Math.Round((y - top) * 65535.0 / Math.Max(1, height - 1));

            mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, dx, dy, 0, UIntPtr.Zero);
        }

        public static void Click()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(60);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }
    }
}
'@
}

# Named $target, not $handle: PowerShell variable names are case-insensitive, so $handle and
# the [long] $Handle parameter are one variable and the assignment silently round-trips
# through long.
$target = if ($Handle -ne 0) { [IntPtr] $Handle } else { [DeskNote.Windows]::Find($ProcessId, $TitleLike) }
Write-Host ("target handle: {0}" -f $target)
if ($target -eq [IntPtr]::Zero) {
    Write-Host "no window matched '$TitleLike' in process $ProcessId"
    exit 1
}

$rect = New-Object DeskNote.Windows+RECT
[void][DeskNote.Windows]::GetWindowRect($target, [ref] $rect)

$x = [int] ($rect.Left + (($rect.Right - $rect.Left) * $FractionX))
$y = [int] ($rect.Top + (($rect.Bottom - $rect.Top) * $FractionY))

if ($x -lt $rect.Left -or $x -ge $rect.Right -or $y -lt $rect.Top -or $y -ge $rect.Bottom) {
    throw "refusing to point outside the target window: ($x,$y) is not inside $($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)"
}

$origin = New-Object DeskNote.Pointer+POINT
[void][DeskNote.Pointer]::GetCursorPos([ref] $origin)

try {
    # A short travel first: WinUI wants a real move across the boundary before it treats the
    # pointer as having entered the window.
    [DeskNote.Pointer]::MoveTo($rect.Left - 12, $rect.Top - 12)
    Start-Sleep -Milliseconds 200
    foreach ($step in 1..4) {
        $stepX = [int] ($rect.Left - 12 + ((($x - ($rect.Left - 12)) * $step) / 4))
        $stepY = [int] ($rect.Top - 12 + ((($y - ($rect.Top - 12)) * $step) / 4))
        [DeskNote.Pointer]::MoveTo($stepX, $stepY)
        Start-Sleep -Milliseconds 90
    }
    Start-Sleep -Milliseconds $SettleMilliseconds

    if ($Click) {
        [DeskNote.Pointer]::Click()
        Start-Sleep -Milliseconds $SettleMilliseconds
    }

    Write-Host ("pointed at {0},{1} inside {2}" -f $x, $y, $TitleLike)
}
finally {
    if (-not $KeepPointer) {
        [DeskNote.Pointer]::MoveTo($origin.X, $origin.Y)
    }
}
