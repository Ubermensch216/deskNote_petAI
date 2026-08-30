using System.Globalization;
using DeskNote.App.Services;
using DeskNote.App.Theming;
using DeskNote.Core.Ai;
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
using Windows.Storage.Pickers;
using Windows.System;

namespace DeskNote.App.Views;

/// <summary>
/// One sticky note, hosted in its own top-level window (report p1).
/// </summary>
/// <remarks>
/// <para>
/// The window owns presentation and input only. It never touches the database directly: text
/// changes go to <see cref="AutosaveScheduler"/>, and geometry and appearance changes are raised
/// as events for <see cref="NoteWindowManager"/> to persist. That keeps a note window disposable
/// and makes the save policy testable without a UI.
/// </para>
/// <para>
/// The note has no title field and no visible chrome at rest: what is on screen is the text and
/// the paper it sits on. The commands fade in on hover or focus, which is what report p4 asks for
/// and what keeps a 240×180 note from spending a third of its height on furniture.
/// </para>
/// </remarks>
public sealed partial class NoteWindow : Window
{
    private readonly OverlappedPresenter _presenter;
    private NoteGeometry _geometry;
    private string _colorKey;
    private double _opacity;
    private bool _alwaysOnTop;
    private bool _suppressChangeEvents;

    /// <summary>Pointer travel, in pixels, before a press on the strip becomes a drag.</summary>
    private const int DragThreshold = 4;

    private bool _isDragging;
    private bool _dragPending;
    private bool _pointerInside;
    private bool _menuOpen;
    private Windows.Graphics.PointInt32 _dragWindowOrigin;
    private (int X, int Y) _dragCursorOrigin;

    private readonly LocalAiHost? _ai;
    private AiCapability _aiCapability;

    public NoteWindow(Note note, LocalAiHost? ai = null)
    {
        InitializeComponent();

        _ai = ai;
        _aiCapability = ai?.Capability ?? AiCapability.Unavailable(AiAvailability.Disabled);

        NoteId = note.Id;
        _geometry = note.Geometry;
        _colorKey = NoteColors.Normalize(note.ColorKey);
        _opacity = note.Opacity;
        _alwaysOnTop = note.AlwaysOnTop;

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
        ContentBox.Text = note.Content;

        // Range is set here rather than in XAML: the parser assigns attributes in declaration
        // order and rejects a Minimum written before the Maximum it has to fit inside. Deriving
        // the floor from Note.MinOpacity also keeps it from drifting away from the value the
        // repository clamps to.
        OpacitySlider.Maximum = 100;
        OpacitySlider.Minimum = Note.MinOpacity * 100;
        OpacitySlider.Value = Math.Clamp(note.Opacity, Note.MinOpacity, 1.0) * 100;
        _suppressChangeEvents = false;

        LocalizeChrome();
        UpdatePinState();
        BuildColorChoices();
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

    /// <summary>Raised when a confirmed task carries a deadline, with the moment it should fire.</summary>
    public event EventHandler<DateTimeOffset>? ReminderAtRequested;

    /// <summary>Raised when the user wants to ask a question about this note.</summary>
    public event EventHandler? AskRequested;

    /// <summary>Raised when the user asks for another note from this one's <c>+</c> button.</summary>
    public event EventHandler<NoteSeed>? NewNoteRequested;

    /// <summary>Raised when files or a pasted image should be attached to this note.</summary>
    public event EventHandler<AttachmentRequest>? AttachmentRequested;

    /// <summary>The note's title, taken from its first line — there is no separate title field.</summary>
    public string CurrentTitle => NoteContent.DeriveTitle(ContentBox.Text);

    public string CurrentContent => ContentBox.Text;

    /// <summary>Flips always-on-top, keeping the menu in step. Used by the global hotkey.</summary>
    public void ToggleAlwaysOnTop()
    {
        _alwaysOnTop = !_alwaysOnTop;
        _presenter.IsAlwaysOnTop = _alwaysOnTop;
        UpdatePinState();
        RaiseAppearanceChanged();
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

    /// <summary>Inserts text at the caret, as an ordinary undoable edit.</summary>
    public void InsertAtCaret(string text)
    {
        var state = EditorState();
        var updated = state.Text
            .Remove(state.SelectionStart, state.SelectionLength)
            .Insert(state.SelectionStart, text);

        ApplyEditorState(new NoteTextState(updated, state.SelectionStart + text.Length, 0));
        RaiseTextChanged();
    }

    /// <summary>
    /// Puts every visible string on the chrome, in one place.
    /// </summary>
    /// <remarks>
    /// The commands show a glyph or a single letter, which is all a screen reader would otherwise
    /// have to announce — "pushpin", "photo", "B". Naming them is what makes the note operable
    /// without sight, and it is a release gate in report p18. The same string doubles as the
    /// tooltip, so a sighted user gets the name too rather than guessing at an icon.
    /// </remarks>
    private void LocalizeChrome()
    {
        ContentBox.PlaceholderText = Strings.Get("Note_ContentPlaceholder");
        OpacitySlider.Header = Strings.Get("Note_Opacity");

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DragStrip, Strings.Get("Note_DragLabel"));

        Describe(NewNoteButton, "Note_NewNote");
        Describe(MoreButton, "Note_MoreLabel");
        Describe(CloseButton, "Note_Close");

        Describe(BoldButton, "Note_Bold");
        Describe(ItalicButton, "Note_Italic");
        Describe(UnderlineButton, "Note_Underline");
        Describe(StrikethroughButton, "Note_Strikethrough");
        Describe(BulletButton, "Note_BulletList");
        Describe(ChecklistButton, "Note_Checklist");
        Describe(ImageButton, "Note_InsertImage");

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ColorChoices, Strings.Get("Note_ColorLabel"));

        PinRowLabel.Text = Strings.Get("Note_PinLabel");
        LibraryRowLabel.Text = Strings.Get("Note_Library");
        DeleteRowLabel.Text = Strings.Get("Note_Delete");
        SizeCaption.Text = Strings.Get("Note_Size");
        RemindCaption.Text = Strings.Get("Note_Remind");
        AiCaption.Text = Strings.Get("Note_Ai");
        AiRewriteCaption.Text = Strings.Get("Note_AiRewrite");

        AiSummarize.Tag = AiTextAction.Summarize;
        AiSummarize.Content = Strings.Get("Note_AiSummarize");
        AiOrganize.Tag = AiTextAction.Organize;
        AiOrganize.Content = Strings.Get("Note_AiOrganize");
        AiExtractTasks.Tag = AiListAction.ExtractTasks;
        AiExtractTasks.Content = Strings.Get("Note_AiExtractTasks");
        AiSuggestTags.Tag = AiListAction.SuggestTags;
        AiSuggestTags.Content = Strings.Get("Note_AiSuggestTags");
        AiAsk.Content = Strings.Get("Note_AiAsk");

        foreach (var (button, style) in new[]
                 {
                     (AiRewriteConcise, RewriteStyle.Concise),
                     (AiRewriteFormal, RewriteStyle.Formal),
                     (AiRewriteFriendly, RewriteStyle.Friendly),
                     (AiRewriteReport, RewriteStyle.Report),
                 })
        {
            button.Tag = style;
            button.Content = Strings.Get($"Note_AiRewrite{style}");
        }

        UpdateAiState();

        // Chips carry the value they stand for, so the click handlers do not have to map a button
        // back to a preset by name.
        foreach (var (button, preset) in new[]
                 {
                     (SizeSmall, NoteSizePreset.Small),
                     (SizeMedium, NoteSizePreset.Medium),
                     (SizeLarge, NoteSizePreset.Large),
                 })
        {
            var (width, height) = NoteGeometry.SizeOf(preset);
            var name = Strings.Get($"Note_Size{preset}");

            button.Tag = preset;
            button.Content = name;
            ToolTipService.SetToolTip(button, Strings.Format("Note_SizeFormat", name, width, height));
        }

        // Relative offsets rather than a date picker: a sticky note reminder is almost always
        // "later today" or "tomorrow morning", and a full scheduling dialog on a note this small
        // would cost more attention than the reminder is worth. Zero means tomorrow morning, the
        // one offset that is a clock time rather than a duration.
        foreach (var (button, key, offset) in new (Button, string, TimeSpan)[]
                 {
                     (Remind10Minutes, "Remind10Minutes", TimeSpan.FromMinutes(10)),
                     (Remind1Hour, "Remind1Hour", TimeSpan.FromHours(1)),
                     (RemindTomorrow, "RemindTomorrow", TimeSpan.Zero),
                 })
        {
            button.Tag = offset;
            button.Content = Strings.Get($"Note_{key}Short");
            ToolTipService.SetToolTip(button, Strings.Get($"Note_{key}"));
        }
    }

    private static void Describe(FrameworkElement element, string key)
    {
        var text = Strings.Get(key);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, text);
        ToolTipService.SetToolTip(element, text);
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;

        // Always-on-top set before the window is shown does not stick, leaving WS_EX_TOPMOST clear.
        // Re-asserting once the window is live is what actually makes the note float.
        _presenter.IsAlwaysOnTop = _alwaysOnTop;
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

    private void RaiseAppearanceChanged() =>
        AppearanceChanged?.Invoke(this, new NoteAppearance(_colorKey, _opacity, _alwaysOnTop));

    private void RaiseTextChanged() =>
        TextChanged?.Invoke(this, new NoteText(CurrentTitle, ContentBox.Text));

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressChangeEvents)
        {
            return;
        }

        RaiseTextChanged();
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

    /// <summary>
    /// Runs a formatting command against the editor and leaves the caret there.
    /// </summary>
    /// <remarks>
    /// The command is computed before focus moves, because clicking a toolbar button has already
    /// taken focus off the TextBox by the time this runs. Focus goes back first and the selection
    /// is restored after, so the user can bold a word and keep typing without reaching for the
    /// mouse again.
    /// </remarks>
    private void RunEditorCommand(Func<NoteTextState, NoteTextState> command)
    {
        var next = command(EditorState());
        _ = ContentBox.Focus(FocusState.Programmatic);
        ApplyEditorState(next);
    }

    private void RunEditorCommand(Func<NoteTextState, NoteTextState> command, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RunEditorCommand(command);
    }

    private void OnBoldInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(MarkdownEditing.ToggleBold, args);

    private void OnItalicInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(MarkdownEditing.ToggleItalic, args);

    private void OnUnderlineInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(MarkdownEditing.ToggleUnderline, args);

    private void OnStrikethroughInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunEditorCommand(MarkdownEditing.ToggleStrikethrough, args);

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
            _ = ContentBox.Focus(FocusState.Programmatic);
            ApplyEditorState(toggled);
        }
    }

    private void OnBoldClicked(object sender, RoutedEventArgs e) =>
        RunEditorCommand(MarkdownEditing.ToggleBold);

    private void OnItalicClicked(object sender, RoutedEventArgs e) =>
        RunEditorCommand(MarkdownEditing.ToggleItalic);

    private void OnUnderlineClicked(object sender, RoutedEventArgs e) =>
        RunEditorCommand(MarkdownEditing.ToggleUnderline);

    private void OnStrikethroughClicked(object sender, RoutedEventArgs e) =>
        RunEditorCommand(MarkdownEditing.ToggleStrikethrough);

    private void OnBulletClicked(object sender, RoutedEventArgs e) =>
        RunEditorCommand(state => MarkdownEditing.ApplyLineStyle(state, LineStyle.Bullet));

    private void OnChecklistClicked(object sender, RoutedEventArgs e) =>
        RunEditorCommand(state => MarkdownEditing.ApplyLineStyle(state, LineStyle.Checklist));

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

    /// <summary>
    /// Picks an image from disk and attaches it.
    /// </summary>
    /// <remarks>
    /// Paste and drop already cover the fast paths, but neither is reachable from the keyboard or
    /// obvious to someone who has not tried them; the picker is what makes attaching an image a
    /// visible command. The picker has to be told which window owns it — an unpackaged app has no
    /// implicit one, and without this the dialog never appears.
    /// </remarks>
    private async void OnInsertImageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                CommitButtonText = Strings.Get("Note_InsertImage"),
            };

            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" })
            {
                picker.FileTypeFilter.Add(extension);
            }

            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                Microsoft.UI.Win32Interop.GetWindowFromWindowId(AppWindow.Id));

            if (await picker.PickSingleFileAsync() is { } file)
            {
                AttachmentRequested?.Invoke(this, new AttachmentRequest(NoteId, [file.Path], null, null));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Choosing an image failed", ex);
        }
    }

    private void OnNewNoteClicked(object sender, RoutedEventArgs e)
    {
        // The new note inherits this one's colour and size: notes made from a note are almost
        // always part of the same thought, and re-picking the colour every time is friction.
        var preset = _geometry.Preset == NoteSizePreset.Custom ? NoteSizePreset.Medium : _geometry.Preset;
        NewNoteRequested?.Invoke(this, new NoteSeed(_colorKey, preset));
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnPinRowClicked(object sender, RoutedEventArgs e)
    {
        ToggleAlwaysOnTop();
        MoreFlyout.Hide();
    }

    private void OnSizeChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NoteSizePreset preset })
        {
            SetSizePreset(preset);
            MoreFlyout.Hide();
        }
    }

    private void OnReminderChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TimeSpan offset })
        {
            ReminderRequested?.Invoke(this, offset);
            MoreFlyout.Hide();
        }
    }

    private void OnLibraryRowClicked(object sender, RoutedEventArgs e)
    {
        LibraryRequested?.Invoke(this, EventArgs.Empty);
        MoreFlyout.Hide();
    }

    /// <summary>
    /// Updates what the AI section offers, from a probe the host ran in the background.
    /// </summary>
    /// <remarks>
    /// Called whenever availability changes, not only at startup: Ollama can be started or stopped
    /// while notes are open, and the menu is expected to follow rather than to be right only for
    /// as long as nothing outside the app moved.
    /// </remarks>
    public void SetAiCapability(AiCapability capability)
    {
        _aiCapability = capability;
        UpdateAiState();
    }

    private void UpdateAiState()
    {
        var ready = _ai is not null && _aiCapability.IsAvailable;

        foreach (var chip in new[]
                 {
                     AiSummarize, AiOrganize, AiExtractTasks, AiSuggestTags, AiAsk,
                     AiRewriteConcise, AiRewriteFormal, AiRewriteFriendly, AiRewriteReport,
                 })
        {
            chip.IsEnabled = ready;
        }

        // When AI works, the line names the model and where it runs — the note owner should be
        // able to tell local inference from a service without leaving the note. When it does not,
        // the line is the reason, because "greyed out with no explanation" is the state that makes
        // people think the app is broken.
        AiStatus.Text = ready
            ? Strings.Format(
                "Note_AiReadyFormat",
                _aiCapability.ModelId ?? string.Empty,
                Strings.Get($"Note_AiAccelerator{_aiCapability.Accelerator}"))
            : Strings.Get($"Note_AiUnavailable{_aiCapability.Availability}");
    }

    private void OnAiActionChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AiTextAction action })
        {
            MoreFlyout.Hide();
            RunAi(action, RewriteStyle.Concise);
        }
    }

    private void OnAiRewriteChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RewriteStyle style })
        {
            MoreFlyout.Hide();
            RunAi(AiTextAction.Rewrite, style);
        }
    }

    private void OnAiAskChipClicked(object sender, RoutedEventArgs e)
    {
        MoreFlyout.Hide();
        AskRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAiListChipClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AiListAction action })
        {
            MoreFlyout.Hide();
            RunAiList(action);
        }
    }

    /// <summary>
    /// Asks the model for a list of proposals — tasks or tags — and confirms them one by one.
    /// </summary>
    /// <remarks>
    /// Unlike the text actions this always reads the whole note. A tag describes the note, and a
    /// deadline mentioned three lines above the selection is still this note's deadline.
    /// </remarks>
    private async void RunAiList(AiListAction action)
    {
        if (_ai is null || !_aiCapability.IsAvailable || string.IsNullOrWhiteSpace(ContentBox.Text))
        {
            return;
        }

        var context = new NoteContext
        {
            NoteId = NoteId,
            Content = ContentBox.Text,
            Title = CurrentTitle,
            LanguageTag = Strings.OverrideLocale ?? "ko-KR",
        };

        var window = new AiProposalWindow(Strings.Get($"Note_Ai{action}"));
        window.Activate();
        window.PlaceNear(AppWindow);

        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            if (action == AiListAction.ExtractTasks)
            {
                var tasks = await _ai.Service.ExtractTasksAsync(context, window.CancellationToken);

                window.Applied += (_, selected) =>
                {
                    ApplyTasks(selected.Select(index => tasks[index]).ToList());
                    window.Close();
                };

                window.ShowProposals(
                    [.. tasks.Select(DescribeTask)],
                    _aiCapability.ModelId,
                    System.Diagnostics.Stopwatch.GetElapsedTime(started));
            }
            else
            {
                var tags = await _ai.Service.SuggestTagsAsync(context, window.CancellationToken);

                // A tag the note already carries is not a proposal; offering it would only invite
                // the user to accept a duplicate.
                var fresh = tags
                    .Where(tag => !TagParser.Parse(ContentBox.Text)
                        .Any(existing => string.Equals(
                            existing.NormalizedName,
                            Tag.Normalize(tag.Name),
                            StringComparison.Ordinal)))
                    .ToList();

                window.Applied += (_, selected) =>
                {
                    ApplyTags(selected.Select(index => fresh[index].Name).ToList());
                    window.Close();
                };

                window.ShowProposals(
                    [.. fresh.Select(tag => new AiProposal(
                        "#" + tag.Name,
                        Strings.Format("Ai_ConfidenceFormat", (int)Math.Round(tag.Confidence * 100))))],
                    _aiCapability.ModelId,
                    System.Diagnostics.Stopwatch.GetElapsedTime(started));
            }
        }
        catch (OperationCanceledException)
        {
            // The window closed; nothing left to show the result to.
        }
        catch (AiUnavailableException ex)
        {
            SetAiCapability(AiCapability.Unavailable(ex.Reason));
            window.ShowFailure(Strings.Get($"Note_AiUnavailable{ex.Reason}"));
        }
        catch (Exception ex)
        {
            CrashLog.Write($"AI action {action} failed", ex);
            window.ShowFailure(ex.Message);
        }
    }

    /// <summary>Renders a task the way the confirmation list should read it.</summary>
    private static AiProposal DescribeTask(ExtractedTask task)
    {
        var parts = new List<string>();

        if (task.DueAt is { } due)
        {
            parts.Add(Strings.Format("Ai_TaskDueFormat", due.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
        }

        if (!string.IsNullOrWhiteSpace(task.Assignee))
        {
            parts.Add(Strings.Format("Ai_TaskAssigneeFormat", task.Assignee));
        }

        if (task.Priority != TaskPriority.Normal)
        {
            parts.Add(Strings.Get($"Ai_Priority{task.Priority}"));
        }

        return new AiProposal(task.Title, parts.Count == 0 ? null : string.Join(" · ", parts));
    }

    /// <summary>
    /// Writes confirmed tasks into the note as checklist items, and schedules the ones with a date.
    /// </summary>
    /// <remarks>
    /// The body is where a checklist lives — <c>checklist_items</c> is only a projection of it — so
    /// appending Markdown is what actually creates the tasks. A deadline becomes a reminder on this
    /// note; it is not written into the line, because the line is the user's text and a timestamp
    /// pasted into it is not something they would have typed.
    /// </remarks>
    private void ApplyTasks(IReadOnlyList<ExtractedTask> tasks)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        AppendLines(tasks.Select(task => "- [ ] " + task.Title));

        foreach (var due in tasks.Select(task => task.DueAt).OfType<DateTimeOffset>())
        {
            ReminderAtRequested?.Invoke(this, due);
        }
    }

    /// <summary>Appends confirmed tags to the note, which is where tags actually live.</summary>
    private void ApplyTags(IReadOnlyList<string> names)
    {
        if (names.Count > 0)
        {
            AppendLines([string.Join(" ", names.Select(name => "#" + name))]);
        }
    }

    /// <summary>
    /// Adds lines to the end of the note as one undoable edit.
    /// </summary>
    /// <remarks>
    /// Goes through the editor rather than the text property so Ctrl+Z removes everything that was
    /// just applied in a single press, the same as any other block edit.
    /// </remarks>
    private void AppendLines(IEnumerable<string> lines)
    {
        var current = ContentBox.Text;
        var separator = current.Length == 0 || current.EndsWith('\n') ? string.Empty : "\n";
        var block = string.Join("\n", lines);

        if (block.Length == 0)
        {
            return;
        }

        var appended = current + separator + block;

        ApplyEditorState(new NoteTextState(appended, appended.Length, 0));
        RaiseTextChanged();
    }

    /// <summary>
    /// Runs one AI action against the selection, or the whole note when nothing is selected.
    /// </summary>
    /// <remarks>
    /// The span is captured now and re-checked at apply time. Inference takes tens of seconds on
    /// CPU, which is long enough for the user to keep typing, and text written meanwhile must not
    /// be overwritten by a proposal that never saw it.
    /// </remarks>
    private async void RunAi(AiTextAction action, RewriteStyle style)
    {
        if (_ai is null || !_aiCapability.IsAvailable)
        {
            return;
        }

        var text = ContentBox.Text;
        var start = ContentBox.SelectionLength > 0 ? ContentBox.SelectionStart : 0;
        var length = ContentBox.SelectionLength > 0 ? ContentBox.SelectionLength : text.Length;
        var target = text.Substring(start, length);

        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var preview = new AiPreviewWindow(Strings.Get($"Note_Ai{action}"));
        preview.Applied += (_, proposed) =>
        {
            if (TryApplyAiText(start, length, target, proposed))
            {
                preview.Close();
            }
            else
            {
                preview.ShowFailure(Strings.Get("Ai_NoteChanged"));
            }
        };

        preview.Activate();
        preview.PlaceNear(AppWindow);

        var context = new NoteContext
        {
            NoteId = NoteId,
            Content = text,
            SelectedText = ContentBox.SelectionLength > 0 ? target : null,
            Title = CurrentTitle,
            LanguageTag = Strings.OverrideLocale ?? "ko-KR",
        };

        try
        {
            var result = action switch
            {
                AiTextAction.Summarize => await _ai.Service.SummarizeAsync(context, preview.CancellationToken),
                AiTextAction.Organize => await _ai.Service.OrganizeAsync(context, preview.CancellationToken),
                _ => await _ai.Service.RewriteAsync(context, style, preview.CancellationToken),
            };

            preview.ShowResult(result);
        }
        catch (OperationCanceledException)
        {
            // The window is already gone: cancellation only happens when it closed.
        }
        catch (AiUnavailableException ex)
        {
            SetAiCapability(AiCapability.Unavailable(ex.Reason));
            preview.ShowFailure(Strings.Get($"Note_AiUnavailable{ex.Reason}"));
        }
        catch (Exception ex)
        {
            CrashLog.Write($"AI action {action} failed", ex);
            preview.ShowFailure(ex.Message);
        }
    }

    /// <summary>
    /// Writes an accepted proposal into the note, or refuses if the text it was made from is gone.
    /// </summary>
    /// <remarks>
    /// The edit goes through the same path as a formatting command, so Ctrl+Z undoes an applied
    /// suggestion in one press and the autosave pipeline records it as an ordinary revision — an
    /// AI edit is not a special kind of write, it is just a write the user approved.
    /// </remarks>
    private bool TryApplyAiText(int start, int length, string original, string proposed)
    {
        var current = ContentBox.Text;

        if (start + length > current.Length
            || !string.Equals(current.Substring(start, length), original, StringComparison.Ordinal))
        {
            return false;
        }

        ApplyEditorState(new NoteTextState(
            current.Remove(start, length).Insert(start, proposed),
            start,
            proposed.Length));

        RaiseTextChanged();
        return true;
    }

    private void OnDeleteRowClicked(object sender, RoutedEventArgs e)
    {
        MoreFlyout.Hide();
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnColorChosen(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string colorKey })
        {
            return;
        }

        _colorKey = colorKey;
        ApplySurface();
        BuildColorChoices();
        RaiseAppearanceChanged();
    }

    private void OnOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressChangeEvents)
        {
            return;
        }

        _opacity = e.NewValue / 100.0;
        ApplySurface();
        RaiseAppearanceChanged();
    }

    private void UpdatePinState() =>
        PinRowCheck.Visibility = _alwaysOnTop ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Records where a drag would start from. The pointer is deliberately not captured yet.
    /// </summary>
    /// <remarks>
    /// Capturing on press swallows the tap and double-tap gestures. Capture is deferred until the
    /// pointer has actually moved past <see cref="DragThreshold"/>, so a plain click stays a click
    /// and only a real drag moves the note.
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

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        UpdateChrome();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        UpdateChrome();
    }

    private void OnEditorFocusChanged(object sender, RoutedEventArgs e) => UpdateChrome();

    private void OnMoreFlyoutOpened(object? sender, object e)
    {
        _menuOpen = true;

        // An unpackaged WinUI popup is clipped to its own window, so on a small note the rows at
        // the bottom of this menu would be drawn where nobody can click them. Capping the menu to
        // what the note can actually show turns the overflow into a scroll instead.
        MenuScroll.MaxHeight = Math.Max(200, Surface.ActualHeight - 56);

        UpdateChrome();
    }

    private void OnMoreFlyoutClosed(object? sender, object e)
    {
        _menuOpen = false;
        UpdateChrome();
    }

    /// <summary>
    /// Shows or hides the note's chrome.
    /// </summary>
    /// <remarks>
    /// Hit testing follows opacity rather than being left on: an invisible button that still takes
    /// clicks is worse than no button, and with the chrome inert the whole top strip is a drag
    /// handle again. The menu counts as a reason to stay visible, because opening it moves the
    /// pointer off the note and the button it is anchored to should not vanish underneath it.
    /// </remarks>
    private void UpdateChrome()
    {
        var visible = _pointerInside || _menuOpen || ContentBox.FocusState != FocusState.Unfocused;

        foreach (var element in new UIElement[] { LeadingChrome, TrailingChrome, FormatBar })
        {
            element.Opacity = visible ? 1 : 0;
            element.IsHitTestVisible = visible;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private bool IsDarkTheme =>
        Surface.ActualTheme == ElementTheme.Dark
        || (Surface.ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

    private void ApplySurface()
    {
        var surface = NotePalette.Resolve(_colorKey, IsDarkTheme);

        Surface.Background = new SolidColorBrush(NotePalette.WithOpacity(surface.Background, _opacity));
        Surface.BorderBrush = new SolidColorBrush(surface.Border);

        ApplyPaperBrushes(surface.Ink);
    }

    /// <summary>
    /// Strips the default control chrome so the note reads as paper rather than a form.
    /// </summary>
    /// <remarks>
    /// A local <c>Background="Transparent"</c> is not enough: the TextBox and Button templates
    /// rebind background, border and foreground from theme resources on every visual state, so a
    /// resting note would still show a white input box and a hovered button would light up in the
    /// system accent grey. Overriding the brushes on the surface, which the whole note inherits
    /// from, is what actually holds across Normal, PointerOver and Focused.
    /// <para>
    /// Every override is made from the note's own ink, so the hover tint on a blue note is blue-ink
    /// grey rather than the system's — the chrome belongs to the paper it sits on.
    /// </para>
    /// </remarks>
    private void ApplyPaperBrushes(Windows.UI.Color ink)
    {
        var transparent = new SolidColorBrush(Colors.Transparent);
        var inkBrush = new SolidColorBrush(ink);

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
                     "ButtonBackground",
                     "ButtonBackgroundDisabled",
                     "ButtonBorderBrush",
                     "ButtonBorderBrushPointerOver",
                     "ButtonBorderBrushPressed",
                     "ButtonBorderBrushDisabled",
                 })
        {
            Surface.Resources[key] = transparent;
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
            Surface.Resources[key] = inkBrush;
        }

        foreach (var key in new[]
                 {
                     "TextControlPlaceholderForeground",
                     "TextControlPlaceholderForegroundPointerOver",
                     "TextControlPlaceholderForegroundFocused",
                 })
        {
            Surface.Resources[key] = Tint(ink, 0x8A);
        }

        // Resting chrome is deliberately quieter than the text: it is there to be found, not read.
        Surface.Resources["ButtonForeground"] = Tint(ink, 0xA6);
        Surface.Resources["ButtonForegroundPointerOver"] = inkBrush;
        Surface.Resources["ButtonForegroundPressed"] = inkBrush;
        Surface.Resources["ButtonForegroundDisabled"] = Tint(ink, 0x55);
        Surface.Resources["ButtonBackgroundPointerOver"] = Tint(ink, 0x18);
        Surface.Resources["ButtonBackgroundPressed"] = Tint(ink, 0x2A);

        ContentBox.Foreground = inkBrush;
        FormatSeparator.Background = Tint(ink, 0x33);

        // The flyout is hosted in the popup root, outside this window's tree, so its accents are
        // set here by hand rather than inherited.
        MenuSeparator1.Background = Tint(ink, 0x33);
        MenuSeparator2.Background = Tint(ink, 0x33);
        DeleteRowLabel.Foreground = new SolidColorBrush(NotePalette.Danger(IsDarkTheme));
        DeleteRowIcon.Foreground = new SolidColorBrush(NotePalette.Danger(IsDarkTheme));
    }

    private static SolidColorBrush Tint(Windows.UI.Color ink, byte alpha) =>
        new(Windows.UI.Color.FromArgb(alpha, ink.R, ink.G, ink.B));

    private void BuildColorChoices()
    {
        var isDark = IsDarkTheme;
        var ink = new SolidColorBrush(NotePalette.Resolve(_colorKey, isDark).Ink);

        ColorChoices.ItemsSource = NoteColors.All
            .Select(key =>
            {
                var surface = NotePalette.Resolve(key, isDark);
                return new NoteColorChoice(
                    key,
                    Strings.Get($"Note_Color{char.ToUpperInvariant(key[0])}{key[1..]}"),
                    new SolidColorBrush(surface.Background),
                    new SolidColorBrush(surface.Border),
                    ink,
                    key == _colorKey);
            })
            .ToList();
    }
}

/// <summary>One color swatch in a note's color picker.</summary>
/// <param name="Key">The stored <see cref="NoteColors"/> key.</param>
/// <param name="Label">Localized color name, for the tooltip and screen readers.</param>
/// <param name="Brush">The paper color.</param>
/// <param name="Outline">The same color's border, so a pale swatch still has an edge.</param>
/// <param name="CheckBrush">Ink for the tick on the selected swatch.</param>
/// <param name="IsSelected">Whether this is the note's current color.</param>
public sealed record NoteColorChoice(
    string Key,
    string Label,
    SolidColorBrush Brush,
    SolidColorBrush Outline,
    SolidColorBrush CheckBrush,
    bool IsSelected)
{
    public Visibility CheckVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>What a new note made from an existing one should inherit.</summary>
public readonly record struct NoteSeed(string ColorKey, NoteSizePreset Preset);

/// <summary>Files or image bytes the user wants attached to a note.</summary>
/// <param name="NoteId">The note receiving the attachment.</param>
/// <param name="FilePaths">Dropped or picked files, if any.</param>
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
