<#
.SYNOPSIS
    Photographs one window of a running DeskNote instance into docs/images/.

.DESCRIPTION
    Captures per window rather than the screen, for two reasons: nothing else that happens to be
    on the desktop can end up in the repository, and a window that is partly covered still comes
    out whole. PrintWindow is given PW_RENDERFULLCONTENT so DirectComposition surfaces — which is
    what WinUI draws into — are included.

    The bitmap is taken at GetWindowRect size and then trimmed to the DWM extended frame bounds,
    otherwise the invisible resize border is baked into the picture as a transparent margin and the
    window's right and bottom edges look cut off.

.EXAMPLE
    ./tools/capture-window.ps1 -ProcessId 1234 -TitleLike 'DeskNote PetAI' -Out docs/images/companion-dashboard.png
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $ProcessId,
    [Parameter(Mandatory)] [string] $TitleLike,
    [Parameter(Mandatory)] [string] $Out,

    # The pet window and the dashboard share a title, so the caller can name the exact window.
    [long] $Handle = 0
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not ('DeskNote.Capture' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskNote
{
    public static class Capture
    {
        private const int PW_RENDERFULLCONTENT = 2;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, int flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);

        public static List<IntPtr> Find(int processId, string titleLike)
        {
            var found = new List<IntPtr>();

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
                    found.Add(handle);
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        public static string Titles(int processId)
        {
            var titles = new StringBuilder();

            EnumWindows((handle, _) =>
            {
                uint owner;
                GetWindowThreadProcessId(handle, out owner);
                if (owner != (uint)processId || !IsWindowVisible(handle))
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
                titles.AppendLine(handle.ToInt64() + "\t" + text);
                return true;
            }, IntPtr.Zero);

            return titles.ToString();
        }

        public static void Save(IntPtr handle, string path)
        {
            RECT window;
            if (!GetWindowRect(handle, out window))
            {
                throw new InvalidOperationException("GetWindowRect failed.");
            }

            var width = window.Right - window.Left;
            var height = window.Bottom - window.Top;

            using (var bitmap = new Bitmap(width, height))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var hdc = graphics.GetHdc();
                    try
                    {
                        if (!PrintWindow(handle, hdc, PW_RENDERFULLCONTENT))
                        {
                            throw new InvalidOperationException("PrintWindow failed.");
                        }
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }

                // The window rect includes the invisible resize border; the DWM frame is what a
                // person sees as the edge of the window.
                RECT frame;
                var crop = new Rectangle(0, 0, width, height);
                if (DwmGetWindowAttribute(handle, DWMWA_EXTENDED_FRAME_BOUNDS, out frame, Marshal.SizeOf(typeof(RECT))) == 0)
                {
                    var left = Math.Max(0, frame.Left - window.Left);
                    var top = Math.Max(0, frame.Top - window.Top);
                    var right = Math.Min(width, frame.Right - window.Left);
                    var bottom = Math.Min(height, frame.Bottom - window.Top);
                    if (right - left > 0 && bottom - top > 0)
                    {
                        crop = new Rectangle(left, top, right - left, bottom - top);
                    }
                }

                using (var trimmed = bitmap.Clone(crop, bitmap.PixelFormat))
                {
                    trimmed.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
    }
}
'@ -ReferencedAssemblies System.Drawing, System.Drawing.Primitives
}

$handles = if ($Handle -ne 0) { @([IntPtr] $Handle) } else { [DeskNote.Capture]::Find($ProcessId, $TitleLike) }

if ($handles.Count -eq 0) {
    Write-Host "no window matched '$TitleLike' in process $ProcessId. Visible windows:"
    Write-Host ([DeskNote.Capture]::Titles($ProcessId))
    exit 1
}

$target = [System.IO.Path]::GetFullPath($Out)
$directory = [System.IO.Path]::GetDirectoryName($target)
if (-not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

[DeskNote.Capture]::Save($handles[0], $target)
$size = (Get-Item -LiteralPath $target).Length
Write-Host "saved $target ($size bytes)"
