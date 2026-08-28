using DeskNote.App.Services;
using DeskNote.App.Theming;
using DeskNote.Core.Models;
using DeskNote.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// One sticky note, hosted in its own top-level window (report p1).
/// </summary>
/// <remarks>
/// The window owns presentation and input only. It never touches the database directly: text
/// changes go to <see cref="AutosaveScheduler"/>, and geometry and appearance changes are raised
/// as events for <see cref="NoteWindowManager"/> to persist. That keeps a note window disposable
/// and makes the save policy testable without a UI.
/// </remarks>
public sealed partial class NoteWindow : Window
{
    private readonly OverlappedPresenter _presenter;
    private NoteGeometry _geometry;
    private string _colorKey;
    private double _opacity;
    private bool _suppressChangeEvents;
    /// <summary>Pointer travel, in pixels, before a press on the strip becomes a drag.</summary>
    private const int DragThreshold = 4;

    private bool _isDragging;
    private bool _dragPending;
    private Windows.Graphics.PointInt32 _dragWindowOrigin;
    private (int X, int Y) _dragCursorOrigin;

    public NoteWindow(Note note)
    {
        InitializeComponent();

        NoteId = note.Id;
        _geometry = note.Geometry;
        _colorKey = NoteColors.Normalize(note.ColorKey);
        _opacity = note.Opacity;

        _presenter = OverlappedPresenter.Create();
        _presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        _presenter.IsResizable = true;

        // Below this the editor stops being usable, so the window itself refuses to go smaller
        // rather than leaving the clamp to fix it up on the next restore.
        _presenter.PreferredMinimumWidth = NoteGeometry.MinWidth;
        _presenter.PreferredMinimumHeight = NoteGeometry.MinHeight;
        AppWindow.SetPresenter(_presenter);

        AppWindow.Title = "DeskNote";

        // Report p4 treats Mica and Acrylic as the system backdrops to reach for. Acrylic is the
        // right one here: at the default opacity of 1.0 the note's own surface covers it entirely,
        // and as the user lowers opacity the acrylic behind shows through, which is the "배경 표면
        // opacity는 조절하되 텍스트 대비는 항상 유지" behaviour the report asks for.
        SystemBackdrop = new DesktopAcrylicBackdrop();

        // Windows rounds top-level windows itself; without asking DWM for it, a borderless note
        // would render as a hard-edged rectangle behind the rounded surface.
        WindowCorners.Round(this);

        _suppressChangeEvents = true;
        TitleBox.Text = note.Title;
        ContentBox.Text = note.Content;
        PinButton.IsChecked = note.AlwaysOnTop;

        // Range is set here rather than in XAML: the parser assigns attributes in declaration
        // order and rejects a Minimum written before the Maximum it has to fit inside. Deriving
        // the floor from Note.MinOpacity also keeps it from drifting away from the value the
        // repository clamps to.
        OpacitySlider.Maximum = 100;
        OpacitySlider.Minimum = Note.MinOpacity * 100;
        OpacitySlider.Value = Math.Clamp(note.Opacity, Note.MinOpacity, 1.0) * 100;
        _suppressChangeEvents = false;

        BuildColorChoices();
        BuildMoreMenu();
        ApplySurface();

        // The initial placement runs before the Changed handler is attached, so a clamp applied
        // here is not written back. That is deliberate: a note pulled onto the primary display
        // because its own monitor is unplugged should return to that monitor once it is
        // reconnected, rather than having its original position overwritten on first sight.
        ApplyGeometry(note.Geometry);

        AppWindow.Changed += OnAppWindowChanged;
        Activated += OnFirstActivated;
        Closed += OnClosed;
    }

    public Guid NoteId { get; }

    /// <summary>Raised when the window has been moved or resized and the new geometry should be stored.</summary>
    public event EventHandler<NoteGeometry>? GeometryChanged;

    /// <summary>Raised when color, opacity or always-on-top changed.</summary>
    public event EventHandler<NoteAppearance>? AppearanceChanged;

    /// <summary>Raised on every keystroke, for the debounced autosave.</summary>
    public event EventHandler<NoteText>? TextChanged;

    /// <summary>Raised when the user closes the note window; the note is hidden, not deleted.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the user asks to delete the note.</summary>
    public event EventHandler? DeleteRequested;

    public string CurrentTitle => TitleBox.Text;

    public string CurrentContent => ContentBox.Text;

    public void FocusEditor()
    {
        AppWindow.Show();
        _ = ContentBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Applies geometry to the window, clamping it onto a display that actually exists.</summary>
    public void ApplyGeometry(NoteGeometry geometry)
    {
        var safe = MonitorLayout.ClampToVisibleArea(geometry);
        _geometry = safe;
        AppWindow.MoveAndResize(new RectInt32(safe.X, safe.Y, safe.Width, safe.Height));
    }

    public void SetSizePreset(NoteSizePreset preset)
    {
        var (width, height) = NoteGeometry.SizeOf(preset);
        ApplyGeometry(_geometry with { Width = width, Height = height, Preset = preset });
        RaiseGeometryChanged();
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;

        // Always-on-top set before the window is shown does not stick, leaving WS_EX_TOPMOST clear.
        // Re-asserting once the window is live is what actually makes the note float.
        _presenter.IsAlwaysOnTop = PinButton.IsChecked ?? false;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        var position = sender.Position;
        var size = sender.Size;
        var monitorKey = MonitorLayout.DeviceNameForWindow(
            Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id));

        _geometry = _geometry with
        {
            X = position.X,
            Y = position.Y,
            MonitorKey = string.IsNullOrEmpty(monitorKey) ? _geometry.MonitorKey : monitorKey,
        };
        _geometry = _geometry.WithSize(size.Width, size.Height);

        RaiseGeometryChanged();
    }

    private void RaiseGeometryChanged() => GeometryChanged?.Invoke(this, _geometry);

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChangeEvents)
        {
            return;
        }

        TextChanged?.Invoke(this, new NoteText(TitleBox.Text, ContentBox.Text));
    }

    private void OnPinClicked(object sender, RoutedEventArgs e)
    {
        var pinned = PinButton.IsChecked ?? false;
        _presenter.IsAlwaysOnTop = pinned;
        AppearanceChanged?.Invoke(this, new NoteAppearance(_colorKey, _opacity, pinned));
    }

    private void OnColorChosen(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string colorKey })
        {
            return;
        }

        _colorKey = colorKey;
        ApplySurface();
        ColorFlyout.Hide();
        AppearanceChanged?.Invoke(this, new NoteAppearance(_colorKey, _opacity, PinButton.IsChecked ?? false));
    }

    private void OnOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressChangeEvents)
        {
            return;
        }

        _opacity = e.NewValue / 100.0;
        ApplySurface();
        AppearanceChanged?.Invoke(this, new NoteAppearance(_colorKey, _opacity, PinButton.IsChecked ?? false));
    }

    /// <summary>
    /// Records where a drag would start from. The pointer is deliberately not captured yet.
    /// </summary>
    /// <remarks>
    /// Capturing on press swallows the tap and double-tap gestures, which is what silently broke
    /// double-click-to-edit on the title. Capture is deferred until the pointer has actually moved
    /// past <see cref="DragThreshold"/>, so a plain click stays a click and only a real drag moves
    /// the note.
    /// </remarks>
    private void OnDragStripPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(DragStrip).Properties.IsLeftButtonPressed
            || !MonitorLayout.TryGetCursorPosition(out var cursorX, out var cursorY))
        {
            return;
        }

        _dragCursorOrigin = (cursorX, cursorY);
        _dragWindowOrigin = AppWindow.Position;
        _dragPending = true;
    }

    /// <remarks>
    /// The offset is measured from the OS cursor position rather than XAML pointer coordinates,
    /// because <see cref="AppWindow.Move"/> works in physical screen pixels; mixing the two spaces
    /// makes the note drift away from the cursor on a display that is not at 100% scaling.
    /// </remarks>
    private void OnDragStripPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if ((!_dragPending && !_isDragging) || !MonitorLayout.TryGetCursorPosition(out var cursorX, out var cursorY))
        {
            return;
        }

        var dx = cursorX - _dragCursorOrigin.X;
        var dy = cursorY - _dragCursorOrigin.Y;

        if (!_isDragging)
        {
            if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
            {
                return;
            }

            _isDragging = DragStrip.CapturePointer(e.Pointer);
            _dragPending = false;
            if (!_isDragging)
            {
                return;
            }
        }

        AppWindow.Move(new Windows.Graphics.PointInt32(
            _dragWindowOrigin.X + dx,
            _dragWindowOrigin.Y + dy));
    }

    private void OnDragStripPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragPending = false;
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        DragStrip.ReleasePointerCapture(e.Pointer);

        // AppWindow.Changed persisted each intermediate position; this records the final one.
        RaiseGeometryChanged();
    }

    /// <summary>
    /// Turns the title into an editable field.
    /// </summary>
    /// <remarks>
    /// The title box is inert at rest so the whole strip stays a drag handle — a text field
    /// spanning the strip would leave nowhere to grab the note. Double-click is the deliberate
    /// gesture that switches it into edit mode, and it goes back to inert on blur.
    /// </remarks>
    private void OnDragStripDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _dragPending = false;
        TitleBox.IsHitTestVisible = true;
        _ = TitleBox.Focus(FocusState.Programmatic);
        TitleBox.SelectAll();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        CommandBar.Opacity = 1;

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Keep the commands up while the user is typing, so they do not flicker away mid-edit.
        if (!TitleBox.FocusState.Equals(FocusState.Unfocused) || !ContentBox.FocusState.Equals(FocusState.Unfocused))
        {
            return;
        }

        CommandBar.Opacity = 0;
    }

    private void OnEditorFocusChanged(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, TitleBox) && TitleBox.FocusState == FocusState.Unfocused)
        {
            TitleBox.IsHitTestVisible = false;
        }

        var focused = TitleBox.FocusState != FocusState.Unfocused || ContentBox.FocusState != FocusState.Unfocused;
        CommandBar.Opacity = focused ? 1 : 0;
    }

    private void OnClosed(object sender, WindowEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void ApplySurface()
    {
        var isDark = Surface.ActualTheme == ElementTheme.Dark
            || (Surface.ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        var surface = NotePalette.Resolve(_colorKey, isDark);

        Surface.Background = new SolidColorBrush(NotePalette.WithOpacity(surface.Background, _opacity));
        Surface.BorderBrush = new SolidColorBrush(surface.Border);

        ApplyPaperTextBoxBrushes(surface.Ink);
    }

    /// <summary>
    /// Strips the default TextBox chrome so the editors read as paper rather than form fields.
    /// </summary>
    /// <remarks>
    /// A local <c>Background="Transparent"</c> is not enough: the TextBox template rebinds
    /// background, border and foreground from theme resources on every visual state, so a resting
    /// note would still show a white input box and a focused one would repaint its text in the
    /// system foreground — unreadable on a dark note. Overriding the brushes on the surface, which
    /// both editors inherit from, is what actually holds across Normal, PointerOver and Focused.
    /// </remarks>
    private void ApplyPaperTextBoxBrushes(Windows.UI.Color ink)
    {
        var transparent = Colors.Transparent;
        var placeholder = Windows.UI.Color.FromArgb(0x8A, ink.R, ink.G, ink.B);

        foreach (var key in new[]
                 {
                     "TextControlBackground",
                     "TextControlBackgroundPointerOver",
                     "TextControlBackgroundFocused",
                     "TextControlBackgroundDisabled",
                     "TextControlBorderBrush",
                     "TextControlBorderBrushPointerOver",
                     "TextControlBorderBrushFocused",
                     "TextControlBorderBrushDisabled",
                     "TextControlButtonBackground",
                     "TextControlButtonBackgroundPointerOver",
                     "TextControlButtonBackgroundPressed",
                 })
        {
            Surface.Resources[key] = new SolidColorBrush(transparent);
        }

        foreach (var key in new[]
                 {
                     "TextControlForeground",
                     "TextControlForegroundPointerOver",
                     "TextControlForegroundFocused",
                     "TextControlButtonForeground",
                     "TextControlButtonForegroundPointerOver",
                 })
        {
            Surface.Resources[key] = new SolidColorBrush(ink);
        }

        foreach (var key in new[]
                 {
                     "TextControlPlaceholderForeground",
                     "TextControlPlaceholderForegroundPointerOver",
                     "TextControlPlaceholderForegroundFocused",
                 })
        {
            Surface.Resources[key] = new SolidColorBrush(placeholder);
        }

        var inkBrush = new SolidColorBrush(ink);
        TitleBox.Foreground = inkBrush;
        ContentBox.Foreground = inkBrush;
    }

    private void BuildColorChoices()
    {
        var isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;

        ColorChoices.ItemsSource = NoteColors.All
            .Select(key => new NoteColorChoice(key, new SolidColorBrush(NotePalette.Resolve(key, isDark).Background)))
            .ToList();
    }

    private void BuildMoreMenu()
    {
        foreach (var preset in new[] { NoteSizePreset.Small, NoteSizePreset.Medium, NoteSizePreset.Large })
        {
            var (width, height) = NoteGeometry.SizeOf(preset);
            var item = new MenuFlyoutItem { Text = $"{preset} · {width}×{height}" };
            item.Click += (_, _) => SetSizePreset(preset);
            MoreMenu.Items.Add(item);
        }

        MoreMenu.Items.Add(new MenuFlyoutSeparator());

        var delete = new MenuFlyoutItem { Text = "Delete" };
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        MoreMenu.Items.Add(delete);
    }

}

/// <summary>One color swatch in a note's color picker.</summary>
public sealed record NoteColorChoice(string Key, SolidColorBrush Brush);

/// <summary>Appearance values a note window reports back for persistence.</summary>
public readonly record struct NoteAppearance(string ColorKey, double Opacity, bool AlwaysOnTop);

/// <summary>Editor text a note window reports back for autosave.</summary>
public readonly record struct NoteText(string Title, string Content);
