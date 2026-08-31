using System.Globalization;
using DeskNote.App.Services;
using DeskNote.Core.Abstractions;
using DeskNote.Core.Ai;
using DeskNote.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>
/// A reminder written the way a person says it.
/// </summary>
/// <remarks>
/// <para>
/// The three chips on the settings menu cover "soon" and "tomorrow", which is most of what a
/// sticky note needs and none of what a recurring one does. The scheduler has understood
/// <c>RRULE</c> since the first release and nothing could produce one, because a 240×180 note has
/// nowhere to put a recurrence editor — but it has room for a line of text.
/// </para>
/// <para>
/// This is the one AI action that is quick enough to sit in a dialog. Input is a phrase and output
/// is a timestamp, so the model answers in a second or two rather than the half minute a rewrite
/// takes.
/// </para>
/// <para>
/// Nothing is scheduled until the user has seen the date. "다음 주 화요일" resolving to the wrong
/// Tuesday is a reminder that fires confidently at the wrong time, and the only defence against
/// that is showing the answer in full before agreeing to it.
/// </para>
/// </remarks>
public sealed partial class ReminderWindow : Window
{
    private readonly LocalAiHost _ai;
    private readonly IClock _clock;
    private readonly Func<DateTimeOffset, string?, Task> _create;

    private CancellationTokenSource? _reading;
    private ParsedReminder? _parsed;

    public ReminderWindow(LocalAiHost ai, IClock clock, Func<DateTimeOffset, string?, Task> create)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(create);

        InitializeComponent();

        _ai = ai;
        _clock = clock;
        _create = create;

        AppWindow.Title = Strings.Format("Ai_PreviewTitleFormat", Strings.Get("Remind_Title"));
        AppIcon.Apply(this);
        ReminderTitle.Text = Strings.Get("Remind_Title");
        ReminderHint.Text = Strings.Get("Remind_Hint");
        PhraseBox.PlaceholderText = Strings.Get("Remind_Placeholder");
        ReadButton.Content = Strings.Get("Remind_Read");
        RunningLabel.Text = Strings.Get("Ai_Running");
        EmptyLabel.Text = Strings.Get("Remind_Empty");
        CreateButton.Content = Strings.Get("Remind_Create");
        CloseButton.Content = Strings.Get("Ai_Cancel");

        AppWindow.Resize(new SizeInt32(520, 380));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 420;
            presenter.PreferredMinimumHeight = 300;
        }

        Activated += OnFirstActivated;
        Closed += (_, _) => _reading?.Cancel();
    }

    /// <summary>Places the window beside the note it belongs to, rather than over it.</summary>
    public void PlaceNear(AppWindow owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        AppWindow.Move(new PointInt32(
            owner.Position.X + owner.Size.Width + 12,
            owner.Position.Y));
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivated;
        _ = PhraseBox.Focus(FocusState.Programmatic);
    }

    private void OnPhraseKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            Read();
        }
    }

    private void OnReadClicked(object sender, RoutedEventArgs e) => Read();

    private async void Read()
    {
        var phrase = PhraseBox.Text.Trim();

        if (phrase.Length == 0 || _reading is not null)
        {
            return;
        }

        if (!_ai.IsAvailable)
        {
            Status.Text = Strings.Get($"Note_AiUnavailable{_ai.Capability.Availability}");
            return;
        }

        _reading = new CancellationTokenSource();
        _parsed = null;

        EmptyLabel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        RunningPanel.Visibility = Visibility.Visible;
        Progress.IsActive = true;
        ReadButton.IsEnabled = false;
        CreateButton.IsEnabled = false;
        Status.Text = string.Empty;

        try
        {
            var parsed = await _ai.Service.ParseReminderAsync(
                phrase,
                _clock.Now,
                Strings.OverrideLocale ?? "ko-KR",
                _reading.Token);

            if (parsed is null)
            {
                // The model could not place the phrase, which is a real answer. Inventing a date
                // here would produce a reminder that fires confidently at the wrong time.
                EmptyLabel.Visibility = Visibility.Visible;
                return;
            }

            _parsed = parsed;
            Show(parsed);
        }
        catch (OperationCanceledException)
        {
            // The window is closing, or a newer phrase replaced this one.
        }
        catch (AiUnavailableException ex)
        {
            Status.Text = Strings.Get($"Note_AiUnavailable{ex.Reason}");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Reading a reminder phrase failed", ex);
            Status.Text = ex.Message;
        }
        finally
        {
            RunningPanel.Visibility = Visibility.Collapsed;
            Progress.IsActive = false;
            ReadButton.IsEnabled = true;

            _reading?.Dispose();
            _reading = null;
        }
    }

    /// <summary>
    /// States the resolved moment in full, weekday included.
    /// </summary>
    /// <remarks>
    /// The weekday is the check that matters. "다음 주 화요일" is verifiable at a glance only if
    /// the answer says which weekday it landed on; a bare date would have to be looked up before
    /// the user could tell whether the model got it right. The full date pattern already carries
    /// it in both languages, so nothing is appended.
    /// </remarks>
    private void Show(ParsedReminder parsed)
    {
        var local = parsed.DueAt.ToLocalTime();

        ResultWhen.Text = local.ToString("f", CultureInfo.CurrentCulture);

        // "1주마다" is what a format string says; "매주" is what a person says.
        ResultRepeat.Text = Recurrence.Parse(parsed.RecurrenceRule) is { } recurrence
            ? recurrence.Interval == 1
                ? Strings.Format(
                    "Remind_RepeatEveryFormat",
                    Strings.Get($"Remind_Freq{recurrence.Frequency}"))
                : Strings.Format(
                    "Remind_RepeatFormat",
                    Strings.Get($"Remind_Freq{recurrence.Frequency}"),
                    recurrence.Interval)
            : Strings.Get("Remind_Once");

        // A date already gone by is stored rather than shifted: the user said so, and silently
        // moving someone's date is worse than a reminder that fires at once.
        Status.Text = local < _clock.Now ? Strings.Get("Remind_AlreadyPast") : string.Empty;

        ResultPanel.Visibility = Visibility.Visible;
        CreateButton.IsEnabled = true;
    }

    private async void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        if (_parsed is not { } parsed)
        {
            return;
        }

        CreateButton.IsEnabled = false;

        try
        {
            await _create(parsed.DueAt, parsed.RecurrenceRule);
            Close();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Creating a reminder failed", ex);
            Status.Text = ex.Message;
            CreateButton.IsEnabled = true;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
