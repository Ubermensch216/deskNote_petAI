using DeskNote.App.Services;
using DeskNote.App.Theming;
using DeskNote.Core.Models;
using DeskNote.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.Graphics;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

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

        TitleBox.PlaceholderText = Strings.Get("Note_TitlePlaceholder");
        ContentBox.PlaceholderText = Strings.Get("Note_ContentPlaceholder");
        OpacitySlider.Header = Strings.Get("Note_Opacity");

        // The command buttons show a single glyph, which is all a screen reader would otherwise
        // have to announce — "pushpin", "circle with left half black". Naming them is what makes
        // the note operable without sight, and it is a release gate in report p18.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PinButton, Strings.Get("Note_PinLabel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ColorButton, Strings.Get("Note_ColorLabel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AiButton, Strings.Get("Note_AiLabel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MoreButton, Strings.Get("Note_MoreLabel"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DragStrip, Strings.Get("Note_DragLabel"));

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

    /// <summary>Raised when the user asks to open the note library.</summary>
    public event EventHandler? LibraryRequested;

    /// <summary>Raised when the user asks to set a reminder, with how far ahead it should fire.</summary>
    public event EventHandler<TimeSpan>? ReminderRequested;

    public string CurrentTitle => TitleBox.Text;

    public string CurrentContent => ContentBox.Text;

    /// <summary>Flips always-on-top, keeping the pin button in step. Used by the global hotkey.</summary>
    public void ToggleAlwaysOnTop()
    {
        var pinned = !(PinButton.IsChecked ?? false);
        PinButton.IsChecked = pinned;
        _presenter.IsAlwaysOnTop = pinned;
        AppearanceChanged?.Invoke(this, new NoteAppearance(_colorKey, _opacity, pinned));
    }

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

    private NoteTextState EditorState() =>
        new(ContentBox.Text, ContentBox.SelectionStart, ContentBox.SelectionLength);

    /// <summary>
    /// Applies a transformed state to the editor as a selection replacement.
    /// </summary>
    /// <remarks>
    /// Assigning <c>Text</c> directly would clear the TextBox's undo history, so Ctrl+Z after
    /// bolding a word would do nothing. Replacing only the span that actually changed keeps the
    /// edit on the editor's own undo stack, which is what report p5 asks for when it pairs
    /// Undo/Redo with revisions.
    /// </remarks>
    private void ApplyEditorState(NoteTextState next)
    {
        var edit = TextDiff.Minimal(ContentBox.Text, next.Text);

        if (!edit.IsEmpty)
        {
            ContentBox.Select(edit.Start, edit.Length);
            ContentBox.SelectedText = edit.Insert;
        }

        ContentBox.Select(next.SelectionStart, next.SelectionLength);
    }

    private void RunEditorCommand(Func<NoteTextState, NoteTextState> command, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ApplyEditorState(command(EditorState()));
    }

    private void OnBoldInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(MarkdownEditing.ToggleBold, args);

    private void OnItalicInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(MarkdownEditing.ToggleItalic, args);

    private void OnCodeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(MarkdownEditing.ToggleInlineCode, args);

    private void OnLinkInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(state => MarkdownEditing.InsertLink(state), args);

    private void OnHeading1Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(state => MarkdownEditing.ApplyLineStyle(state, LineStyle.Heading1), args);

    private void OnHeading2Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(state => MarkdownEditing.ApplyLineStyle(state, LineStyle.Heading2), args);

    private void OnHeading3Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(state => MarkdownEditing.ApplyLineStyle(state, LineStyle.Heading3), args);

    private void OnBulletInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(state => MarkdownEditing.ApplyLineStyle(state, LineStyle.Bullet), args);

    private void OnChecklistInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(state => MarkdownEditing.ApplyLineStyle(state, LineStyle.Checklist), args);

    private void OnToggleCheckInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (MarkdownEditing.ToggleChecklistItemAtCaret(EditorState()) is { } toggled)
        {
            ApplyEditorState(toggled);
        }
    }

    /// <summary>
    /// Continues a list when Enter is pressed inside one.
    /// </summary>
    /// <remarks>
    /// Handled in PreviewKeyDown so the TextBox never inserts its own newline first, which would
    /// otherwise leave the caret on a fresh line before the continuation is worked out.
    /// <para>
    /// Korean IME composition is the case to watch here, since Enter is also how a candidate is
    /// committed. Driving the Microsoft Korean IME through this path shows the syllable committed
    /// and the list continued from one Enter, with nothing lost; if that ever regresses, this
    /// handler is where composition state would have to be consulted before intercepting the key.
    /// </para>
    /// </remarks>
    private void OnContentPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || IsModifierDown())
        {
            return;
        }

        if (MarkdownEditing.ContinueListOnEnter(EditorState()) is not { } continued)
        {
            return;
        }

        e.Handled = true;
        ApplyEditorState(continued);
    }

    private static bool IsModifierDown()
    {
        var states = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            | InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            | InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);

        return states.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    /// <summary>Raised when files or a pasted image should be attached to this note.</summary>
    public event EventHandler<AttachmentRequest>? AttachmentRequested;

    /// <summary>Inserts text at the caret, as an ordinary undoable edit.</summary>
    public void InsertAtCaret(string text)
    {
        var state = EditorState();
        var updated = state.Text
            .Remove(state.SelectionStart, state.SelectionLength)
            .Insert(state.SelectionStart, text);

        ApplyEditorState(new NoteTextState(updated, state.SelectionStart + text.Length, 0));
        TextChanged?.Invoke(this, new NoteText(TitleBox.Text, ContentBox.Text));
    }

    private void OnSurfaceDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Strings.Get("Note_AttachDropCaption");
        }
    }

    private async void OnSurfaceDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<Windows.Storage.StorageFile>().Select(f => f.Path).ToList();

            if (paths.Count > 0)
            {
                AttachmentRequested?.Invoke(this, new AttachmentRequest(NoteId, paths, null, null));
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// Attaches an image pasted from the clipboard.
    /// </summary>
    /// <remarks>
    /// Handled before the TextBox sees the paste only when the clipboard actually holds a bitmap;
    /// otherwise the ordinary text paste is left completely alone, including its undo entry.
    /// </remarks>
    private async void OnPasteRequested(object sender, TextControlPasteEventArgs args)
    {
        var clipboard = Clipboard.GetContent();
        if (!clipboard.Contains(StandardDataFormats.Bitmap))
        {
            return;
        }

        args.Handled = true;

        try
        {
            var reference = await clipboard.GetBitmapAsync();
            using var stream = await reference.OpenReadAsync();
            var bytes = new byte[stream.Size];
            using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);

            AttachmentRequested?.Invoke(this, new AttachmentRequest(NoteId, [], bytes, "pasted-image.png"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Services.CrashLog.Write("Pasting an image failed", ex);
        }
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
            var item = new MenuFlyoutItem
            {
                Text = Strings.Format("Note_SizeFormat", Strings.Get($"Note_Size{preset}"), width, height),
            };
            item.Click += (_, _) => SetSizePreset(preset);
            MoreMenu.Items.Add(item);
        }

        MoreMenu.Items.Add(new MenuFlyoutSeparator());

        // Relative offsets rather than a date picker: a sticky note reminder is almost always
        // "later today" or "tomorrow morning", and a full scheduling dialog on a note this small
        // would cost more attention than the reminder is worth.
        var reminders = new MenuFlyoutSubItem { Text = Strings.Get("Note_Remind") };
        foreach (var (label, delay) in new (string, TimeSpan)[]
                 {
                     (Strings.Get("Note_Remind10Minutes"), TimeSpan.FromMinutes(10)),
                     (Strings.Get("Note_Remind1Hour"), TimeSpan.FromHours(1)),
                     (Strings.Get("Note_RemindTomorrow"), TimeSpan.Zero),
                 })
        {
            var item = new MenuFlyoutItem { Text = label };
            var offset = delay;
            item.Click += (_, _) => ReminderRequested?.Invoke(this, offset);
            reminders.Items.Add(item);
        }

        MoreMenu.Items.Add(reminders);

        var library = new MenuFlyoutItem { Text = Strings.Get("Note_Library") };
        library.Click += (_, _) => LibraryRequested?.Invoke(this, EventArgs.Empty);
        MoreMenu.Items.Add(library);

        MoreMenu.Items.Add(new MenuFlyoutSeparator());

        var delete = new MenuFlyoutItem { Text = Strings.Get("Note_Delete") };
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        MoreMenu.Items.Add(delete);
    }

}

/// <summary>One color swatch in a note's color picker.</summary>
public sealed record NoteColorChoice(string Key, SolidColorBrush Brush);

/// <summary>Files or image bytes the user wants attached to a note.</summary>
/// <param name="NoteId">The note receiving the attachment.</param>
/// <param name="FilePaths">Dropped files, if any.</param>
/// <param name="ImageBytes">Pasted image bytes, if any.</param>
/// <param name="ImageName">File name to give pasted bytes.</param>
public readonly record struct AttachmentRequest(
    Guid NoteId,
    IReadOnlyList<string> FilePaths,
    byte[]? ImageBytes,
    string? ImageName);

/// <summary>Appearance values a note window reports back for persistence.</summary>
public readonly record struct NoteAppearance(string ColorKey, double Opacity, bool AlwaysOnTop);

/// <summary>Editor text a note window reports back for autosave.</summary>
public readonly record struct NoteText(string Title, string Content);
