using DeskNote.Companion.Core;

namespace DeskNote.App.Views;

/// <summary>
/// Makes the pet's window take the pointer only where the pet actually is.
/// </summary>
/// <remarks>
/// <para>
/// The window is a fixed 240 by 250 and the animal fills a fraction of that. Nothing in it but the
/// animal is visible, yet every transparent pixel was still hit-testable: probing the shipped
/// window showed the whole rectangle answering for clicks, so whatever sat under the empty part of
/// it - a scroll bar, a button, a text field - could not be reached while the pet stood there.
/// That is tolerable along the bottom edge of the screen, and it would not be at the top of it,
/// where a pet crawling along the ceiling would sit on the minimise and close buttons.
/// </para>
/// <para>
/// <c>WS_EX_TRANSPARENT</c> on a layered window makes the whole window click-through, and it is the
/// only thing that does so for a WinUI window, whose input arrives through child windows of its
/// own that a subclass of the top-level window never sees. So the window is click-through by
/// default and is made solid only while the cursor is over the pet. The cursor is polled rather
/// than reported because a click-through window is, by definition, not told where the cursor is.
/// </para>
/// </remarks>
public sealed partial class DesktopPetWindow
{
    private const long WsExTransparent = 0x0000_0020;
    private const int HoverPollMilliseconds = 40;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _hoverTimer;
    private bool _clickThrough;

    private void StartHoverWatch()
    {
        _hoverTimer = DispatcherQueue.CreateTimer();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(HoverPollMilliseconds);
        _hoverTimer.Tick += (_, _) => UpdateClickThrough();
        _hoverTimer.Start();
    }

    private void StopHoverWatch()
    {
        _hoverTimer?.Stop();
        _hoverTimer = null;
    }

    private void UpdateClickThrough()
    {
        if (_placed)
        {
            SetClickThrough(!IsCursorOverPet());
        }
    }

    /// <summary>
    /// Whether the cursor is on the part of the window that is the pet.
    /// </summary>
    /// <remarks>
    /// Anything that cannot be worked out answers yes. A pet that stays solid when it should have
    /// let a click through is the old behaviour; one that lets a click through when the person
    /// meant to grab it cannot be picked up at all.
    /// </remarks>
    private bool IsCursorOverPet()
    {
        if (_pointerDown || _menuOpen || !GetCursorPos(out var cursor))
        {
            return true;
        }

        var layout = CurrentLayout();
        var box = _motion.Box(CurrentBodies(layout, CurrentInk()));

        return PetHitArea.Contains(cursor.X, cursor.Y, box);
    }

    private void SetClickThrough(bool on)
    {
        if (on == _clickThrough)
        {
            return;
        }

        var window = Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var style = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        var next = on ? style | WsExTransparent : style & ~WsExTransparent;
        if (next != style)
        {
            _ = SetWindowLongPtr(window, GwlExStyle, new nint(next));
        }

        _clickThrough = on;
    }
}
