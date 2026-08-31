using DeskNote.App.Services;
using DeskNote.Companion.Core;
using DeskNote.Core.Abstractions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>The explicit opt-in and interruption controls for the desktop companion.</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly ISettingsStore _settings;
    private bool _loaded;

    public SettingsWindow(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();
        _settings = settings;

        AppWindow.Title = Strings.Get("Settings_Title");
        AppIcon.Apply(this);
        ApplyStrings();
        AppWindow.Resize(new SizeInt32(560, 650));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 480;
            presenter.PreferredMinimumHeight = 560;
        }

        Activated += OnFirstActivated;
    }

    public event Action<CompanionSettings>? SettingsChanged;

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            Apply(await CompanionSettings.LoadAsync(_settings).ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Loading companion settings failed", ex);
            SaveStatus.Text = ex.Message;
        }
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        SaveStatus.Text = string.Empty;

        var value = Read();
        try
        {
            await value.SaveAsync(_settings).ConfigureAwait(true);
            SaveStatus.Text = Strings.Get("Settings_Saved");
            SettingsChanged?.Invoke(value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Saving companion settings failed", ex);
            SaveStatus.Text = ex.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Apply(CompanionSettings value)
    {
        CompanionEnabled.IsOn = value.Enabled;
        ProactiveEnabled.IsOn = value.ProactiveEnabled;
        AlwaysVisible.IsOn = value.AlwaysVisible;
        ReduceMotion.IsOn = value.ReduceMotion;
        QuietStart.Time = value.QuietStart.ToTimeSpan();
        QuietEnd.Time = value.QuietEnd.ToTimeSpan();
    }

    private CompanionSettings Read() => new()
    {
        Enabled = CompanionEnabled.IsOn,
        ProactiveEnabled = ProactiveEnabled.IsOn,
        AlwaysVisible = AlwaysVisible.IsOn,
        ReduceMotion = ReduceMotion.IsOn,
        QuietStart = TimeOnly.FromTimeSpan(QuietStart.Time),
        QuietEnd = TimeOnly.FromTimeSpan(QuietEnd.Time),
        RuleVersion = CompanionSettings.CurrentRuleVersion,
    };

    private void ApplyStrings()
    {
        PageTitle.Text = Strings.Get("Settings_Title");
        LocalOnlyLabel.Text = Strings.Get("Settings_LocalOnly");
        CompanionHeader.Text = Strings.Get("Settings_CompanionHeader");
        CompanionDescription.Text = Strings.Get("Settings_CompanionDescription");
        CompanionEnabled.Header = Strings.Get("Settings_CompanionEnabled");
        ProactiveEnabled.Header = Strings.Get("Settings_Proactive");
        AlwaysVisible.Header = Strings.Get("Settings_AlwaysVisible");
        ReduceMotion.Header = Strings.Get("Settings_ReduceMotion");
        QuietHoursLabel.Text = Strings.Get("Settings_QuietHours");
        QuietStartLabel.Text = Strings.Get("Settings_QuietStart");
        QuietEndLabel.Text = Strings.Get("Settings_QuietEnd");
        SaveButton.Content = Strings.Get("Settings_Save");
        CloseButton.Content = Strings.Get("Ai_Close");
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
