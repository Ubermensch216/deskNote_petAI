using DeskNote.App.Services;
using DeskNote.Companion.Core;
using DeskNote.Core.Abstractions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>The explicit opt-in and interruption controls for the desktop companion.</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly ISettingsStore _settings;
    private bool _loaded;
    private bool _dragonUnlocked;
    private int? _petPositionX;
    private int? _petPositionY;

    public SettingsWindow(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();
        _settings = settings;

        AppWindow.Title = Strings.Get("Settings_Title");
        AppIcon.Apply(this);
        ApplyStrings();
        AppWindow.Resize(new SizeInt32(560, 760));

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
        _dragonUnlocked = value.DragonUnlocked;
        _petPositionX = value.PetPositionX;
        _petPositionY = value.PetPositionY;
        PetNameInput.Text = value.PetName;
        PopulatePetChoices(value.SelectedPet);
        PetSizePicker.SelectedIndex = value.PetSize switch
        {
            CompanionPetSize.Small => 0,
            CompanionPetSize.Medium => 1,
            _ => 2,
        };
        CompanionEnabled.IsOn = value.Enabled;
        ProactiveEnabled.IsOn = value.ProactiveEnabled;
        AlwaysVisible.IsOn = value.AlwaysVisible;
        PetMovementEnabled.IsOn = !value.ReduceMotion;
        QuietStart.Time = value.QuietStart.ToTimeSpan();
        QuietEnd.Time = value.QuietEnd.ToTimeSpan();
    }

    private CompanionSettings Read()
    {
        var selectedPet = PetPicker.SelectedItem is Microsoft.UI.Xaml.Controls.ComboBoxItem
            { Tag: CompanionPetKind pet }
                ? pet
                : CompanionPetKind.Rabbit;

        return new CompanionSettings
        {
            Enabled = CompanionEnabled.IsOn,
            ProactiveEnabled = ProactiveEnabled.IsOn,
            AlwaysVisible = AlwaysVisible.IsOn,
            ReduceMotion = !PetMovementEnabled.IsOn,
            SelectedPet = selectedPet,
            PetName = CompanionSettings.NormalizePetName(PetNameInput.Text),
            PetSize = PetSizePicker.SelectedIndex switch
            {
                0 => CompanionPetSize.Small,
                1 => CompanionPetSize.Medium,
                _ => CompanionPetSize.Large,
            },
            PetPositionX = _petPositionX,
            PetPositionY = _petPositionY,
            DragonUnlocked = _dragonUnlocked,
            QuietStart = TimeOnly.FromTimeSpan(QuietStart.Time),
            QuietEnd = TimeOnly.FromTimeSpan(QuietEnd.Time),
            RuleVersion = CompanionSettings.CurrentRuleVersion,
        };
    }

    private void PopulatePetChoices(CompanionPetKind selected)
    {
        PetPicker.Items.Clear();
        foreach (var pet in CompanionPetCatalog.All)
        {
            var locked = pet == CompanionPetKind.Dragon && !_dragonUnlocked;
            var item = new ComboBoxItem
            {
                Content = BuildPetChoice(pet, locked),
                Tag = pet,
                IsEnabled = !locked,
            };
            PetPicker.Items.Add(item);
            if (pet == selected && !locked)
            {
                PetPicker.SelectedItem = item;
            }
        }

        PetPicker.SelectedIndex = PetPicker.SelectedIndex < 0 ? 0 : PetPicker.SelectedIndex;
        DragonLockHint.Visibility = _dragonUnlocked ? Visibility.Collapsed : Visibility.Visible;
    }

    private static UIElement BuildPetChoice(CompanionPetKind pet, bool locked)
    {
        var preview = new Grid
        {
            Width = 45,
            Height = 60,
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 45, 60) },
            Opacity = locked ? 0.45 : 1,
        };
        preview.Children.Add(new Image
        {
            Width = 180,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Left,
            Stretch = Stretch.Fill,
            Source = new BitmapImage(new Uri(
                $"ms-appx:///Assets/Companion/{pet.AssetKey()}-walk.png")),
        });

        var name = Strings.Get($"Settings_Pet{pet}");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(preview);
        row.Children.Add(new TextBlock
        {
            Text = locked ? Strings.Format("Settings_PetLockedFormat", name) : name,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private void ApplyStrings()
    {
        PageTitle.Text = Strings.Get("Settings_Title");
        LocalOnlyLabel.Text = Strings.Get("Settings_LocalOnly");
        CompanionHeader.Text = Strings.Get("Settings_CompanionHeader");
        CompanionDescription.Text = Strings.Get("Settings_CompanionDescription");
        PetNameInput.Header = Strings.Get("Settings_PetName");
        PetNameInput.PlaceholderText = Strings.Get("Settings_PetNamePlaceholder");
        PetPicker.Header = Strings.Get("Settings_PetPicker");
        PetSizePicker.Header = Strings.Get("Settings_PetSize");
        PetSizeSmallItem.Content = Strings.Get("Settings_PetSizeSmall");
        PetSizeMediumItem.Content = Strings.Get("Settings_PetSizeMedium");
        PetSizeLargeItem.Content = Strings.Get("Settings_PetSizeLarge");
        DragonLockHint.Text = Strings.Get("Settings_DragonUnlockHint");
        CompanionEnabled.Header = Strings.Get("Settings_CompanionEnabled");
        ProactiveEnabled.Header = Strings.Get("Settings_Proactive");
        AlwaysVisible.Header = Strings.Get("Settings_AlwaysVisible");
        PetMovementEnabled.Header = Strings.Get("Settings_ReduceMotion");
        QuietHoursLabel.Text = Strings.Get("Settings_QuietHours");
        QuietStartLabel.Text = Strings.Get("Settings_QuietStart");
        QuietEndLabel.Text = Strings.Get("Settings_QuietEnd");
        SaveButton.Content = Strings.Get("Settings_Save");
        CloseButton.Content = Strings.Get("Ai_Close");
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
