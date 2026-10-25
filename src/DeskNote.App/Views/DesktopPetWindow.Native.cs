using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using WinRT;

namespace DeskNote.App.Views;

/// <summary>The window styles, DWM calls and message queue that make the window see-through.</summary>
public sealed partial class DesktopPetWindow
{
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x0008_0000;
    private const long WsExToolWindow = 0x0000_0080;
    private const uint DwmBbEnable = 0x0000_0001;
    private const uint DwmBbBlurRegion = 0x0000_0002;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpDoNotRound = 1;
    private const uint SwpRefreshFrame = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;

    [ThreadStatic]
    private static object? _systemDispatcherQueueController;
    private Windows.UI.Composition.Compositor? _backdropCompositor;
    private Windows.UI.Composition.CompositionColorBrush? _transparentBackdrop;

    private void MakeBackgroundTransparent()
    {
        var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        _ = SetWindowLongPtr(window, GwlExStyle, new nint(style | WsExLayered | WsExToolWindow));

        var cornerPreference = DwmwcpDoNotRound;
        _ = DwmSetWindowAttribute(
            window,
            DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
        _ = SetWindowPos(window, 0, 0, 0, 0, 0, SwpRefreshFrame);

        var margins = new Margins();
        _ = DwmExtendFrameIntoClientArea(window, ref margins);

        // WinUI owns a DirectComposition surface, so a layered-window colour key cannot remove
        // its background. Give DWM a transparent system backdrop instead; XAML alpha then reaches
        // the desktop while the PNG and speech bubble keep their own per-pixel opacity.
        var blurRegion = CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new DwmBlurBehind
            {
                Flags = DwmBbEnable | DwmBbBlurRegion,
                Enable = true,
                BlurRegion = blurRegion,
            };
            _ = DwmEnableBlurBehindWindow(window, ref blur);
        }
        finally
        {
            _ = DeleteObject(blurRegion);
        }

        EnsureSystemDispatcherQueue();
        _backdropCompositor = new Windows.UI.Composition.Compositor();
        _transparentBackdrop = _backdropCompositor.CreateColorBrush(
            Windows.UI.Color.FromArgb(0, 0, 0, 0));
        this.As<ICompositionSupportsSystemBackdrop>().SystemBackdrop = _transparentBackdrop;
    }

    private static void EnsureSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = 2, // DQTYPE_THREAD_CURRENT
            ApartmentType = 2, // DQTAT_COM_STA
        };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(
            options,
            out _systemDispatcherQueueController));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Enable;

        public nint BlurRegion;

        [MarshalAs(UnmanagedType.Bool)]
        public bool TransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        [MarshalAs(UnmanagedType.IUnknown)] out object? dispatcherQueueController);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint newStyle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(nint window, ref DwmBlurBehind blur);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
