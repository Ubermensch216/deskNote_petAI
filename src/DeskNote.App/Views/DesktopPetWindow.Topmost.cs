using DeskNote.App.Services;
using Microsoft.UI.Windowing;

namespace DeskNote.App.Views;

public sealed partial class DesktopPetWindow
{
    /// <summary>
    /// Puts the pet window into the topmost band and keeps it there.
    /// </summary>
    /// <remarks>
    /// The presenter flag alone is not enough on two counts. It is ignored while the window has not
    /// been shown yet, and re-assigning the value the presenter already holds is not guaranteed to
    /// reach the window manager, so the explicit HWND_TOPMOST call is what actually does the work.
    /// It is sent without activating: the pet must never steal focus from the document underneath
    /// it, which is the whole reason the window is a tool window in the first place.
    /// </remarks>
    private void AssertAlwaysOnTop()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }

        var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        _ = SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpKeepTopmost);
    }

    /// <summary>
    /// Runs the z-order check without letting it end the process.
    /// </summary>
    /// <remarks>
    /// A timer tick is the same trap <see cref="SafeHandler"/> exists for: nothing is left to catch
    /// what it throws, so a window that vanishes between two of these calls would take the app down
    /// with it. The timer stops on the first failure rather than logging once a second — the pet
    /// falls back to the promotion it already got, which is where it was before this check existed.
    /// </remarks>
    private void EnsureStillTopmostSafely()
    {
        try
        {
            EnsureStillTopmost();
        }
        catch (Exception ex)
        {
            _topmostTimer.Stop();
            CrashLog.Write("Keeping the desktop pet on top failed", ex);
        }
    }

    /// <summary>
    /// Re-promotes the pet whenever something has come to sit above it.
    /// </summary>
    /// <remarks>
    /// Reading WS_EX_TOPMOST alone was not enough, and that is what let the pet disappear under
    /// other windows. Windows only clears that bit in the rarer cases; the ordinary way a topmost
    /// window gets buried is that another topmost window — a media player pinned on top, a meeting
    /// or capture toolbar, an installer, a second monitor's overlay — is shown after it and lands
    /// higher inside the same band. The pet keeps its bit throughout, so the old check saw nothing
    /// wrong while the pet was plainly covered.
    ///
    /// So the question asked is the one the user actually cares about: is anything drawn over me?
    /// The z-order above the window is walked instead, and the promotion happens only when a real
    /// window overlaps the pet. A pet that nothing covers is still left alone, which keeps the
    /// original promise that the ordering of the user's own pinned windows is not reshuffled.
    /// </remarks>
    private void EnsureStillTopmost()
    {
        var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var covering = DesktopSurfaces.FindCoveringWindow(window);

        // A full-screen app owns the screen, and a pet drawn over a game or a slideshow is a bug
        // in the other direction. Standing down also avoids the promotion war that would otherwise
        // flip such an app out of its exclusive mode every second; the pet comes back on top by
        // itself on the first tick after the window shrinks.
        if (covering != 0 && DesktopSurfaces.FillsItsDisplay(covering))
        {
            return;
        }

        if (covering == 0 && (GetWindowLongPtr(window, GwlExStyle).ToInt64() & WsExTopmost) != 0)
        {
            return;
        }

        AssertAlwaysOnTop();
    }
}
