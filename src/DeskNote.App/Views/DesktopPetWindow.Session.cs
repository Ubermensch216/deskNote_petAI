using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// What the pet does about the machine around it: a locked screen, a changed display layout, being
/// lost, and being asked to show what it can do.
/// </summary>
public sealed partial class DesktopPetWindow
{
    private const double RecallRestSeconds = 2;

    /// <summary>True while the session is locked or the machine is asleep.</summary>
    private bool _sessionPaused;

    /// <summary>True while the pet's own menu is open; the pet stands still for it.</summary>
    private bool _menuOpen;

    private SessionWatcher? _sessionWatcher;

    private bool IsPausedForSession => _sessionPaused || _menuOpen;

    /// <summary>
    /// Starts listening for the screen going unwatched: a locked session, or a sleeping machine.
    /// </summary>
    /// <remarks>
    /// The watcher owns a thread and a window, so <see cref="StopWatchingSession"/> has to run
    /// when this window closes or every pet the user ever turned off would leave one behind.
    /// </remarks>
    private void WatchSession()
    {
        _sessionWatcher = new SessionWatcher(SetSessionPaused);
        _sessionWatcher.Start();
    }

    private void StopWatchingSession()
    {
        _sessionWatcher?.Dispose();
        _sessionWatcher = null;
    }

    /// <summary>
    /// Stops the pet walking while nobody can see it, and picks the walk up again without
    /// charging it for the time that went by.
    /// </summary>
    /// <remarks>
    /// The animation tick already caps one step at fifty milliseconds, so a long sleep could not
    /// teleport the pet. What pausing buys is the timer itself: a locked workstation is not a
    /// reason to wake the dispatcher thirty times a second all night.
    /// </remarks>
    private void SetSessionPaused(bool paused) =>
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            _sessionPaused = paused;
            _lastTick = _clock.Elapsed;
        });

    /// <summary>
    /// Pulls a pet that is standing still back onto a display that still exists.
    /// </summary>
    /// <remarks>
    /// Unplugging a monitor or changing the resolution leaves the pet's position pointing at pixels
    /// that are no longer anywhere, and a pet nobody can see looks like an app that has stopped
    /// working. A pet that is moving needs no help: every tick it is placed on the display nearest
    /// to it, which is also where it ends up. One told to stand still is the case that does.
    /// </remarks>
    private void KeepOnScreen()
    {
        RefreshDisplays();
        if (!_placed || !_settings.ReduceMotion)
        {
            return;
        }

        var layout = CurrentLayout();
        var ink = CurrentInk();
        var bodies = CurrentBodies(layout, ink);
        var area = AreaAt(_motion.CenterX, _motion.CenterY);

        var halfWidth = bodies.FloorWidth / 2;
        var halfHeight = bodies.FloorHeight / 2;
        var x = Math.Clamp(_motion.CenterX, area.Left + halfWidth, Math.Max(area.Left + halfWidth, area.Right - halfWidth));
        var y = Math.Clamp(_motion.CenterY, area.Top + halfHeight, Math.Max(area.Top + halfHeight, area.Bottom - halfHeight));
        if (Math.Abs(x - _motion.CenterX) < 0.5 && Math.Abs(y - _motion.CenterY) < 0.5)
        {
            return;
        }

        _motion.CenterX = x;
        _motion.CenterY = y;
        PlaceWindow(layout, ink, bodies);
        _ = SavePositionSafelyAsync(AppWindow.Position);
    }

    /// <summary>
    /// Brings the pet to the middle of the screen the cursor is on.
    /// </summary>
    /// <remarks>
    /// The way back for a pet that has been dragged somewhere awkward, that is hiding behind
    /// something, or that is up on a wall it cannot be reached on. The screen under the cursor is
    /// the right one to choose: it is where the person asking is looking.
    /// </remarks>
    public void RecallToCenter()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(RecallToCenter);
            return;
        }

        if (!_placed)
        {
            return;
        }

        RefreshDisplays(force: true);

        var layout = CurrentLayout();
        var ink = CurrentInk();
        var hasCursor = MonitorLayout.TryGetCursorPosition(out var cursorX, out var cursorY);
        var anchor = hasCursor
            ? AreaAt(cursorX, cursorY)
            : AreaAt(DisplayArea.Primary.WorkArea.X, DisplayArea.Primary.WorkArea.Y);
        var world = BuildWorld(layout, ink, anchor);

        var area = world.Area;
        var now = _clock.Elapsed;
        _motion.Place(
            now,
            (area.Left + area.Right) / 2,
            area.Bottom - (world.Bodies.FloorHeight / 2),
            world);
        ApplyStateVisuals();
        ApplyFacingTransform();
        PlaceWindow(layout, ink, world.Bodies);

        AppWindow.Show(activateWindow: false);
        AssertAlwaysOnTop();

        // A moment sitting still, so the eye finds it before it wanders off again.
        if (!_settings.ReduceMotion)
        {
            BeginRest(now, seconds: RecallRestSeconds, pose: PetRestPose.Sit);
        }

        _lastTick = now;
        _ = SavePositionSafelyAsync(AppWindow.Position);
    }

    /// <summary>
    /// The pet's own menu: a way to see what it can do without waiting for it to happen.
    /// </summary>
    private void OnPetRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;

        var preview = new MenuFlyoutSubItem { Text = Strings.Get("Companion_MenuPreview") };
        AddPreview(preview, "Companion_MenuPoseSit", PetRestPose.Sit);
        AddPreview(preview, "Companion_MenuPoseSleep", PetRestPose.Sleep);
        AddPreview(preview, "Companion_MenuPoseDaydream", PetRestPose.Daydream);
        AddPreview(preview, "Companion_MenuPoseStretch", PetRestPose.Stretch);
        preview.Items.Add(new MenuFlyoutSeparator());
        AddDemo(preview, "Companion_MenuDemoWall", PetDemo.Wall, needsRoaming: true);
        AddDemo(preview, "Companion_MenuDemoCeiling", PetDemo.Ceiling, needsRoaming: true);
        AddDemo(preview, "Companion_MenuDemoFall", PetDemo.Fall, needsRoaming: false);
        preview.IsEnabled = !_settings.ReduceMotion;

        var recall = new MenuFlyoutItem { Text = Strings.Get("Tray_RecallPet") };
        recall.Click += (_, _) => RecallToCenter();

        var menu = new MenuFlyout();
        menu.Items.Add(preview);
        if (SignatureMenu() is { } signatures)
        {
            menu.Items.Add(signatures);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(recall);
        menu.Opened += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _lastTick = _clock.Elapsed;
        };

        menu.ShowAt(SpriteViewport, e.GetPosition(SpriteViewport));
    }

    /// <summary>
    /// Has the pet show a movement now.
    /// </summary>
    /// <remarks>
    /// Climbing is off when the setting is, so its entries are greyed rather than left to do
    /// nothing: the pet would start up the wall and the next tick would let it go.
    /// </remarks>
    private void AddDemo(MenuFlyoutSubItem parent, string key, PetDemo demo, bool needsRoaming)
    {
        var item = new MenuFlyoutItem
        {
            Text = Strings.Get(key),
            IsEnabled = !needsRoaming || _settings.RoamScreen,
        };
        item.Click += (_, _) => Demonstrate(demo);
        parent.Items.Add(item);
    }

    private void Demonstrate(PetDemo demo)
    {
        if (!_placed || _settings.ReduceMotion)
        {
            return;
        }

        var layout = CurrentLayout();
        var ink = CurrentInk();
        var world = CurrentWorld();
        if (!_motion.Demonstrate(demo, world))
        {
            return;
        }

        ApplyStateVisuals();
        ApplyFacingTransform();
        PlaceWindow(layout, ink, world.Bodies);
        _lastTick = _clock.Elapsed;
    }

    private void AddPreview(MenuFlyoutSubItem parent, string key, PetRestPose pose)
    {
        var item = new MenuFlyoutItem { Text = Strings.Get(key) };
        item.Click += (_, _) => BeginRest(_clock.Elapsed, pose: pose);
        parent.Items.Add(item);
    }
}
