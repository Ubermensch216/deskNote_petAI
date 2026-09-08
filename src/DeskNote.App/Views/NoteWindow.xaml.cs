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
/// and what keeps a 240×180 note from spending a third of its height on furniture. The title is
/// the first line (<see cref="NoteContent.DeriveTitle"/>) and appears with that chrome, so the
/// name the library shows is discoverable without costing the note a row.
/// </para>
/// </remarks>
public sealed partial class NoteWindow : Window
{
    private readonly OverlappedPresenter _presenter;
    private readonly NoteEditor _editor;
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
    private readonly NoteNeighbourhood? _neighbourhood;
    private AiCapability _aiCapability;

    public NoteWindow(Note note, LocalAiHost? ai = null, NoteNeighbourhood? neighbourhood = null)
    {
        InitializeComponent();

        // Built before anything else, because the body's own events are already attached: the
        // editor raises TextChanged and SelectionChanged while it is being set up, and every
        // handler for those goes through this.
        _editor = new NoteEditor(ContentBox);

        // Fill the non-client title-bar band with the note surface. Without this, WinUI keeps a
        // caption-height strip above the XAML content; the acrylic backdrop showing through that
        // strip looks like a second sheet of paper stacked behind the note.
        ExtendsContentIntoTitleBar = true;

        _ai = ai;
        _neighbourhood = neighbourhood;
        _aiCapability = ai?.Capability ?? AiCapability.Unavailable(AiAvailability.Disabled);

        NoteId = note.Id;
        _geometry = note.Geometry;
        _colorKey = NoteColors.Normalize(note.ColorKey);
        _opacity = note.Opacity;
        _alwaysOnTop = note.AlwaysOnTop;

        _presenter = OverlappedPresenter.Create();
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        _presenter.IsResizable = true;

        // Below this the editor stops being usable, so the window itself refuses to go smaller
        // rather than leaving the clamp to fix it up on the next restore.
        _presenter.PreferredMinimumWidth = NoteGeometry.MinWidth;
        _presenter.PreferredMinimumHeight = NoteGeometry.MinHeight;
        AppWindow.SetPresenter(_presenter);
        _presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);

        AppWindow.Title = "DeskNote";
        AppIcon.Apply(this);

        // Report p4 treats Mica and Acrylic as the system backdrops to reach for. Acrylic is the
        // right one here: at the default opacity of 1.0 the note's own surface covers it entirely,
        // and as the user lowers opacity the acrylic behind shows through, which is the "배경 표면
        // opacity는 조절하되 텍스트 대비는 항상 유지" behaviour the report asks for.
        SystemBackdrop = new DesktopAcrylicBackdrop();

        // Windows rounds top-level windows itself; without asking DWM for it, a borderless note
        // would render as a hard-edged rectangle behind the rounded surface.
        WindowCorners.Round(this);

        _suppressChangeEvents = true;
        _editor.Load(note.Content);

        // Range is set here rather than in XAML: the parser assigns attributes in declaration
        // order and rejects a Minimum written before the Maximum it has to fit inside. Deriving
        // the floor from Note.MinOpacity also keeps it from drifting away from the value the
        // repository clamps to.
        OpacitySlider.Maximum = 100;
        OpacitySlider.Minimum = Note.MinOpacity * 100;
        OpacitySlider.Value = Math.Clamp(note.Opacity, Note.MinOpacity, 1.0) * 100;
        _suppressChangeEvents = false;

        LocalizeChrome();
        ShowTitle();
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

    /// <summary>Raised when the user asks to see this note's stored versions.</summary>
    public event EventHandler? HistoryRequested;

    /// <summary>
    /// Raised when an AI proposal was accepted, carrying the whole new body.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="TextChanged"/> because the two need different storage rules. A
    /// keystroke checkpoints on an interval; a model replacing the note is exactly the edit report
    /// p15 says must always be recoverable, and it has to be recorded as the model's doing rather
    /// than the user's — otherwise the history cannot answer "where did my wording go".
    /// </remarks>
    public event EventHandler<AiEdit>? AiApplied;

    /// <summary>Raised when the user asks to set a reminder, with how far ahead it should fire.</summary>
    public event EventHandler<TimeSpan>? ReminderRequested;

    /// <summary>Raised when a confirmed task carries a deadline, with the moment it should fire.</summary>
    public event EventHandler<DateTimeOffset>? ReminderAtRequested;

    /// <summary>Raised when the user wants to ask a question about this note.</summary>
    public event EventHandler? AskRequested;

    /// <summary>Raised when the user wants to see the notes that read like this one.</summary>
    public event EventHandler? RelatedRequested;

    /// <summary>Raised when the user wants to describe a reminder in their own words.</summary>
    public event EventHandler? CustomReminderRequested;

    /// <summary>Raised when the user asks for another note from this one's <c>+</c> button.</summary>
    public event EventHandler<NoteSeed>? NewNoteRequested;

    /// <summary>Raised when files or a pasted image should be attached to this note.</summary>
    public event EventHandler<AttachmentRequest>? AttachmentRequested;

    /// <summary>The note's title, taken from its first line — there is no separate title field.</summary>
    public string CurrentTitle => NoteContent.DeriveTitle(_editor.Markdown);

    public string CurrentContent => _editor.Markdown;

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

    /// <summary>
    /// Replaces the whole body, for a restore that has already been persisted.
    /// </summary>
    /// <remarks>
    /// Change events are suppressed: the pipeline has written this text already, and letting the
    /// editor announce it would schedule a save of what was just saved — and, worse, checkpoint
    /// the restored version on top of the one it replaced.
    /// </remarks>
    public void ReplaceContent(string content)
    {
        _suppressChangeEvents = true;
        _editor.Load(content);
        _editor.Select(_editor.Text.Length, 0);
        _suppressChangeEvents = false;
        ShowTitle();
    }

    /// <summary>Inserts text at the caret, as an ordinary undoable edit.</summary>
    /// <remarks>
    /// Line endings are normalized on the way in because the note's text is LF throughout — that
    /// is what storage, checklist parsing and the derived title are all written against. Text
    /// arriving from outside the app carries CRLF, and a CR that reaches the rich edit document
    /// unconverted becomes a paragraph mark of its own, so every pasted line came in followed by
    /// a blank one.
    /// </remarks>
    public void InsertAtCaret(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ApplyEditorState(EditorState().ReplacingSelection(NoteContent.NormalizeLineEndings(text)));
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

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DragStrip, Strings.Get("Note_DragLabel"));

        Describe(NewNoteButton, "Note_NewNote");
        Describe(AiButton, "Note_Ai");
        Describe(MoreButton, "Note_MoreLabel");
        Describe(CloseButton, "Note_Close");

        Describe(BoldButton, "Note_Bold");
        Describe(ItalicButton, "Note_Italic");
        Describe(UnderlineButton, "Note_Underline");
        Describe(StrikethroughButton, "Note_Strikethrough");
        Describe(BulletButton, "Note_BulletList");
        Describe(SelectionSummarize, "Note_AiSummarize");
        Describe(SelectionConcise, "Note_AiRewriteConcise");
        Describe(ChecklistButton, "Note_Checklist");
        Describe(ImageButton, "Note_InsertImage");

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ColorChoices, Strings.Get("Note_ColorLabel"));

        PinRowLabel.Text = Strings.Get("Note_PinLabel");
        LibraryRowLabel.Text = Strings.Get("Note_Library");
        HistoryRowLabel.Text = Strings.Get("History_Title");
        DeleteRowLabel.Text = Strings.Get("Note_Delete");

        // The section captions became glyphs, so the words they used to carry live on the glyph as
        // a tooltip and as the accessible name. A screen reader still hears "크기" before the three
        // size chips; a sighted user gets it by resting on the icon.
        Describe(OpacityIcon, "Note_Opacity");
        Describe(SizeSectionIcon, "Note_Size");
        Describe(RemindSectionIcon, "Note_Remind");

        Describe(AiTidySectionIcon, "Note_AiTidy");
        Describe(AiRewriteSectionIcon, "Note_AiRewrite");
        Describe(AiExtractSectionIcon, "Note_AiExtract");

        AiSummarize.Tag = AiTextAction.Summarize;
        DescribeTile(AiSummarize, AiSummarizeLabel, "Note_AiSummarize");
        AiOrganize.Tag = AiTextAction.Organize;
        DescribeTile(AiOrganize, AiOrganizeLabel, "Note_AiOrganize");
        AiExtractTasks.Tag = AiListAction.ExtractTasks;
        DescribeTile(AiExtractTasks, AiExtractTasksLabel, "Note_AiExtractTasks");
        AiSuggestTags.Tag = AiListAction.SuggestTags;
        DescribeTile(AiSuggestTags, AiSuggestTagsLabel, "Note_AiSuggestTags");
        AiAskLabel.Text = Strings.Get("Note_AiAsk");
        AiRelatedLabel.Text = Strings.Get("Related_Title");
        AiTitleLabel.Text = Strings.Get("Note_AiSuggestTitle");
        RemindCustomLabel.Text = Strings.Get("Remind_Chip");
        Describe(RemindCustom, "Remind_Title");

        foreach (var (button, label, style) in new[]
                 {
                     (AiRewriteConcise, AiRewriteConciseLabel, RewriteStyle.Concise),
                     (AiRewriteFormal, AiRewriteFormalLabel, RewriteStyle.Formal),
                     (AiRewriteFriendly, AiRewriteFriendlyLabel, RewriteStyle.Friendly),
                     (AiRewriteReport, AiRewriteReportLabel, RewriteStyle.Report),
                 })
        {
            button.Tag = style;
            DescribeTile(button, label, $"Note_AiRewrite{style}");
        }

        UpdateAiState();

        // Chips carry the value they stand for, so the click handlers do not have to map a button
        // back to a preset by name.
        foreach (var (button, label, preset) in new[]
                 {
                     (SizeSmall, SizeSmallLabel, NoteSizePreset.Small),
                     (SizeMedium, SizeMediumLabel, NoteSizePreset.Medium),
                     (SizeLarge, SizeLargeLabel, NoteSizePreset.Large),
                 })
        {
            var (width, height) = NoteGeometry.SizeOf(preset);
            var name = Strings.Get($"Note_Size{preset}");

            button.Tag = preset;
            label.Text = name;

            // The tooltip is the one that carries the pixels, so the chip stays a rectangle and a
            // word while still answering "how large is large".
            var described = Strings.Format("Note_SizeFormat", name, width, height);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, described);
            ToolTipService.SetToolTip(button, described);
        }

        // Relative offsets rather than a date picker: a sticky note reminder is almost always
        // "later today" or "tomorrow morning", and a full scheduling dialog on a note this small
        // would cost more attention than the reminder is worth. Zero means tomorrow morning, the
        // one offset that is a clock time rather than a duration.
        foreach (var (button, label, key, offset) in new (Button, TextBlock, string, TimeSpan)[]
                 {
                     (Remind10Minutes, Remind10MinutesLabel, "Remind10Minutes", TimeSpan.FromMinutes(10)),
                     (Remind1Hour, Remind1HourLabel, "Remind1Hour", TimeSpan.FromHours(1)),
                     (RemindTomorrow, RemindTomorrowLabel, "RemindTomorrow", TimeSpan.Zero),
                 })
        {
            button.Tag = offset;
            label.Text = Strings.Get($"Note_{key}Short");

            var described = Strings.Get($"Note_{key}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, described);
            ToolTipService.SetToolTip(button, described);
        }
    }

    private static void Describe(FrameworkElement element, string key)
    {
        var text = Strings.Get(key);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, text);
        ToolTipService.SetToolTip(element, text);
    }

    /// <summary>
    /// Names an icon tile: the caption under the glyph, the tooltip, and the accessible name.
    /// </summary>
    /// <remarks>
    /// The caption is deliberately not dropped in favour of the glyph alone. A funnel reads as
    /// "요약" once you know, and never before — the word is what teaches the icon, and after that
    /// the icon is what makes the menu scannable.
    /// </remarks>
    private static void DescribeTile(Button button, TextBlock caption, string key)
    {
        var text = Strings.Get(key);
        caption.Text = text;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        ToolTipService.SetToolTip(button, text);
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

    private void RaiseTextChanged()
    {
        ShowTitle();
        TextChanged?.Invoke(this, new NoteText(CurrentTitle, _editor.Markdown));
    }

    /// <summary>
    /// Puts the note's derived title where it can be seen.
    /// </summary>
    /// <remarks>
    /// A note has no title field: the title is its first line, stripped of markup
    /// (<see cref="NoteContent.DeriveTitle"/>), and that is what the library, search results and
    /// reminders show. Until now nothing on the note itself said so, which left "how do I set the
    /// title?" as a question the app never answered. The strip shows it on hover, and the window
    /// name carries it into Alt+Tab and the taskbar.
    /// </remarks>
    private void ShowTitle()
    {
        var title = CurrentTitle;

        TitleLabel.Text = title;

        var windowTitle = title.Length > 0 ? title : "DeskNote";
        if (!string.Equals(AppWindow.Title, windowTitle, StringComparison.Ordinal))
        {
            AppWindow.Title = windowTitle;
        }
    }

    private void OnTextChanged(object sender, RoutedEventArgs e)
    {
        _editor.Invalidate();

        if (_suppressChangeEvents)
        {
            return;
        }

        RaiseTextChanged();
    }

    private NoteTextState EditorState() => _editor.State;

    /// <summary>
    /// Applies a transformed state to the editor as a selection replacement.
    /// </summary>
    /// <remarks>
    /// Replacing the whole body would clear the editor's undo history, so Ctrl+Z after bolding a
    /// word would do nothing, and it would repaint every run on the note. Replacing only the span
    /// that actually changed keeps the edit on the editor's own undo stack — which is what report
    /// p5 asks for when it pairs Undo/Redo with revisions — and leaves emphasis elsewhere alone.
    /// </remarks>
    private void ApplyEditorState(NoteTextState next) => _editor.Apply(next);

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

    /// <summary>
    /// Turns one emphasis on or off across the selection.
    /// </summary>
    /// <remarks>
    /// Emphasis no longer goes through <see cref="MarkdownEditing"/>: the editor draws it, so the
    /// command is the editor's own character formatting and the <c>~~</c> only reappears on the way
    /// to storage. Focus still moves first for the same reason as any other command from the format
    /// bar — clicking the button took focus off the body, and a format applied to an unfocused
    /// selection lands nowhere.
    /// </remarks>
    private void RunInlineCommand(InlineStyle style)
    {
        _ = ContentBox.Focus(FocusState.Programmatic);
        _editor.ToggleInline(style);
        RaiseTextChanged();
    }

    private void RunInlineCommand(InlineStyle style, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RunInlineCommand(style);
    }

    private void OnBoldInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunInlineCommand(InlineStyle.Bold, args);

    private void OnItalicInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunInlineCommand(InlineStyle.Italic, args);

    private void OnUnderlineInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) =>
        RunInlineCommand(InlineStyle.Underline, args);

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
        RunInlineCommand(InlineStyle.Bold);

    private void OnItalicClicked(object sender, RoutedEventArgs e) =>
        RunInlineCommand(InlineStyle.Italic);

    private void OnUnderlineClicked(object sender, RoutedEventArgs e) =>
        RunInlineCommand(InlineStyle.Underline);

    private void OnStrikethroughClicked(object sender, RoutedEventArgs e) =>
        RunInlineCommand(InlineStyle.Strikethrough);

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
        if (TryRunSwallowedShortcut(e))
        {
            return;
        }

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

    /// <summary>
    /// Runs the two format commands whose chord never reaches its accelerator.
    /// </summary>
    /// <remarks>
    /// The edit control inside the TextBox reads Ctrl+X and Ctrl+C without looking at Shift, so it
    /// claimed Ctrl+Shift+X and Ctrl+Shift+C as Cut and Copy and marked the key handled before
    /// accelerator processing ran: pressing the documented strikethrough chord cut the selection
    /// instead of striking it through. PreviewKeyDown tunnels ahead of that — the same reason Enter
    /// is handled here — so the command runs and the edit control never sees the key.
    /// </remarks>
    private bool TryRunSwallowedShortcut(KeyRoutedEventArgs e)
    {
        if (!IsDown(VirtualKey.Control) || !IsDown(VirtualKey.Shift) || IsDown(VirtualKey.Menu))
        {
            return false;
        }

        switch (e.Key)
        {
            case VirtualKey.X:
                e.Handled = true;
                RunInlineCommand(InlineStyle.Strikethrough);
                return true;

            case VirtualKey.C:
                e.Handled = true;
                RunEditorCommand(state => MarkdownEditing.ApplyLineStyle(state, LineStyle.Checklist));
                return true;

            default:
                return false;
        }
    }

    private static bool IsModifierDown() =>
        IsDown(VirtualKey.Control) || IsDown(VirtualKey.Shift) || IsDown(VirtualKey.Menu);

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource
            .GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

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
    /// Attaches a pasted image, and strips the formatting off pasted text.
    /// </summary>
    /// <remarks>
    /// A rich edit control pastes what it is given, so text copied out of a browser would arrive
    /// carrying that page's font, size and colour and land on the note as a foreign object. The
    /// note's emphasis is its own — bold, italic, underline, strikethrough, in the note's ink — so
    /// a paste is taken as characters and nothing else. It still goes in through the document, so
    /// Ctrl+Z takes it back in one press.
    /// </remarks>
    private async void OnPasteRequested(object sender, TextControlPasteEventArgs args)
    {
        var clipboard = Clipboard.GetContent();

        if (!clipboard.Contains(StandardDataFormats.Bitmap))
        {
            if (clipboard.Contains(StandardDataFormats.Text))
            {
                args.Handled = true;
                await PastePlainTextAsync(clipboard).ConfigureAwait(true);
            }

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

    private async Task PastePlainTextAsync(Windows.ApplicationModel.DataTransfer.DataPackageView clipboard)
    {
        try
        {
            var text = await clipboard.GetTextAsync();
            if (text.Length > 0)
            {
                InsertAtCaret(text);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Services.CrashLog.Write("Pasting text failed", ex);
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

    private void OnHistoryRowClicked(object sender, RoutedEventArgs e)
    {
        HistoryRequested?.Invoke(this, EventArgs.Empty);
        MoreFlyout.Hide();
    }


    /// <summary>
    /// Asks before deleting, because deleting is now final.
    /// </summary>
    /// <remarks>
    /// The menu row used to delete on the click: the note went to the deleted view and could be
    /// restored from the library, so the click was recoverable. Deletion is immediate now, and the
    /// same click a hand's width from "Keep on top" would destroy the note and its history with no
    /// way back. The dialog is what replaces the deleted view as the safety net.
    /// </remarks>
    private async void OnDeleteRowClicked(object sender, RoutedEventArgs e)
    {
        MoreFlyout.Hide();

        var dialog = new ContentDialog
        {
            XamlRoot = Surface.XamlRoot,
            Title = Strings.Get("Note_DeleteConfirmTitle"),
            Content = Strings.Get("Note_DeleteConfirmBody"),
            PrimaryButtonText = Strings.Get("Note_Delete"),
            CloseButtonText = Strings.Get("Explorer_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                DeleteRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A dialog that cannot open must not delete the note by default.
            CrashLog.Write("Confirming a note deletion failed", ex);
        }
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

    private void UpdatePinState()
    {
        PinRowCheck.Visibility = _alwaysOnTop ? Visibility.Visible : Visibility.Collapsed;

        // A filled pin for a pinned note. The tick already says it, but the glyph is what the eye
        // reaches first, and a row that only answers in the far-right column reads as unanswered.
        PinRowIcon.Glyph = _alwaysOnTop ? "" : "";
    }

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

    /// <summary>
    /// Follows focus on and off the body, and makes sure a formatting-only edit is stored.
    /// </summary>
    /// <remarks>
    /// Emphasis can change without the text changing — undoing a strikethrough puts every
    /// character back exactly where it was — and the editor does not report that as a text change,
    /// so the debounced save is never scheduled. Leaving the body is the natural moment to settle
    /// it: the note is no longer being typed into, and what is on screen becomes what is stored.
    /// </remarks>
    private void OnEditorFocusChanged(object sender, RoutedEventArgs e)
    {
        UpdateChrome();

        if (ContentBox.FocusState == FocusState.Unfocused && !_suppressChangeEvents)
        {
            _editor.Invalidate();
            RaiseTextChanged();
        }
    }

    private void OnMoreFlyoutOpened(object? sender, object e)
    {
        _menuOpen = true;
        FitMenuToNote(MenuPanel, MenuScroll, preferredWidth: 248);
        UpdateChrome();
    }

    private void OnAiFlyoutOpened(object? sender, object e)
    {
        _menuOpen = true;
        FitMenuToNote(AiMenuPanel, AiMenuScroll, preferredWidth: 248);

        // Opening the menu is the earliest honest signal that an action is coming. Loading the
        // weights now overlaps the 60-second cold start with the seconds the user spends choosing
        // a tile, which is the only part of that minute anyone can get back.
        _ai?.Warm();

        UpdateChrome();
    }

    /// <summary>
    /// Sizes an open menu to the note it belongs to, on both axes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An unpackaged WinUI popup is clipped to its own window rather than repositioned, so on a
    /// small note anything past the note's edge is drawn where nobody can click it. Height turns
    /// into a scroll. Width cannot: there is no horizontal scroll to fall back on, so the menu
    /// gives up its preferred width and the rows reflow — the swatches wrap to a second line, the
    /// chips narrow — which is what keeps every command reachable on a 180pt note.
    /// </para>
    /// <para>
    /// Both floors sit below the smallest note on purpose. A menu is allowed to be cramped, but a
    /// menu whose right-hand column is missing is not a menu, and neither is one whose last row is
    /// scrolled to a place the window will not draw — which is what a floor above the note's own
    /// height produces: the viewport keeps scrolling into the part that was clipped away.
    /// </para>
    /// </remarks>
    private void FitMenuToNote(FrameworkElement panel, ScrollViewer scroll, double preferredWidth)
    {
        // The strip the menu hangs from, plus the presenter's own padding and border.
        const double ChromeAllowance = 56;

        panel.Width = Math.Clamp(Surface.ActualWidth - 26, 140, preferredWidth);
        scroll.MaxHeight = Math.Max(64, Surface.ActualHeight - ChromeAllowance);
    }

    /// <summary>
    /// Shared by both menus. Only one flyout can be open at a time — opening either dismisses the
    /// other — so a single flag is enough to say "the chrome is in use, keep it visible".
    /// </summary>
    private void OnMenuFlyoutClosed(object? sender, object e)
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

        foreach (var element in new UIElement[] { LeadingChrome, TitleLabel, TrailingChrome, FormatBar })
        {
            element.Opacity = visible ? 1 : 0;
            element.IsHitTestVisible = visible;
        }

        SetImageChromeVisible(visible);
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

        // A rich edit control keeps a colour on every character, so a note that changes colour
        // has to have its ink written onto the text as well as onto the control — otherwise the
        // words stay in the old note's ink on the new note's paper.
        ContentBox.Foreground = inkBrush;
        _editor.SetInk(ink);
        TitleLabel.Foreground = Tint(ink, 0x99);
        FormatSeparator.Background = Tint(ink, 0x33);

        // AI is the one command on the strip that is not about the note as an object, and it is
        // what the app is for. Full ink rather than the 0xA6 the rest of the chrome rests at is
        // how it reads as the primary action without needing a colour of its own.
        AiButtonIcon.Foreground = inkBrush;

        // The flyouts are hosted in the popup root, outside this window's tree, so their accents
        // are set here by hand rather than inherited.
        MenuSeparator1.Background = Tint(ink, 0x33);
        MenuSeparator2.Background = Tint(ink, 0x33);
        AiMenuSeparator.Background = Tint(ink, 0x33);

        // The size chips are drawn, not glyphed, so their fill has to come from the ink too.
        foreach (var mark in new[] { SizeSmallMark, SizeMediumMark, SizeLargeMark })
        {
            mark.Background = Tint(ink, 0xA6);
        }

        DeleteRowLabel.Foreground = new SolidColorBrush(NotePalette.Danger(IsDarkTheme));
        DeleteRowIcon.Foreground = new SolidColorBrush(NotePalette.Danger(IsDarkTheme));

        // The buttons over the attached images are built in code and are not in the strip that
        // ApplyPaperBrushes covers, so their plate is repainted here with the rest.
        ApplyImageChromeBrushes();
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

/// <summary>
/// A note body an AI action replaced, with the action that replaced it.
/// </summary>
/// <param name="Title">Title derived from the new body.</param>
/// <param name="Content">The whole new body, not a fragment.</param>
/// <param name="ActionName">
/// Names the action for the history list, in the form the UI strings use — "Summarize",
/// "RewriteFormal", "ExtractTasks".
/// </param>
public readonly record struct AiEdit(string Title, string Content, string ActionName);
