<#
.SYNOPSIS
    Brings one window of a running DeskNote instance to the front, and optionally reports its rect.

.DESCRIPTION
    A note reveals its chrome when the pointer is inside it OR when the editor holds keyboard
    focus, so activating the window is enough to photograph the chrome without moving the user's
    pointer at all.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $ProcessId,
    [Parameter(Mandatory)] [string] $TitleLike
)

$ErrorActionPreference = 'Stop'

if (-not ('DeskNote.Windows' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskNote
{
    public static class Windows
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);

        public static IntPtr Find(int processId, string titleLike)
        {
            var match = IntPtr.Zero;

            EnumWindows((handle, _) =>
            {
                if (!IsWindowVisible(handle))
                {
                    return true;
                }

                uint owner;
                GetWindowThreadProcessId(handle, out owner);
                if (owner != (uint)processId)
                {
                    return true;
                }

                var length = GetWindowTextLength(handle);
                if (length == 0)
                {
                    return true;
                }

                var text = new StringBuilder(length + 1);
                GetWindowText(handle, text, text.Capacity);
                if (text.ToString().IndexOf(titleLike, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    match = handle;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return match;
        }
    }
}
'@
}

# $found, not $handle. This script is dot-sourced by point-window.ps1, which runs it in the
# caller's scope, and PowerShell variable names are case-insensitive: a local $handle here
# silently overwrites the caller's -Handle parameter, which then points every click at
# whichever window this script happened to find.
$found = [DeskNote.Windows]::Find($ProcessId, $TitleLike)
if ($found -eq [IntPtr]::Zero) {
    Write-Host "no window matched '$TitleLike' in process $ProcessId"
    exit 1
}

[void][DeskNote.Windows]::ShowWindow($found, 5)
[void][DeskNote.Windows]::BringWindowToTop($found)
[void][DeskNote.Windows]::SetForegroundWindow($found)
Start-Sleep -Milliseconds 700

$foundRect = New-Object DeskNote.Windows+RECT
[void][DeskNote.Windows]::GetWindowRect($found, [ref] $foundRect)
Write-Host ("handle={0} rect={1},{2},{3},{4}" -f $found, $foundRect.Left, $foundRect.Top, $foundRect.Right, $foundRect.Bottom)
