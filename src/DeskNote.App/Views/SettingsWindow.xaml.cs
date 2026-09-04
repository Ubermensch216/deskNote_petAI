using System.Globalization;
using DeskNote.App.Services;
using DeskNote.App.Theming;
using DeskNote.Companion.Core;
using DeskNote.Core.Abstractions;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.UI;

namespace DeskNote.App.Views;

/// <summary>
/// The explicit opt-in and interruption controls for the desktop companion.
/// </summary>
/// <remarks>
/// <para>
/// This is a character screen. It is drawn in the pet dashboard's language — the hero band, indigo
/// for the pet itself, teal for what it does, purple for when it stays quiet — because it is
/// opened from the dashboard's own corner, and a page in system greys would read as a different
/// application. The rule is the one in <see cref="CompanionPalette"/>: colour carries meaning, and
/// it is applied from code because most of it depends on runtime state.
/// </para>
/// <para>
/// Three choices are shown rather than stated. The pet is seven portraits instead of a dropdown
/// you had to open to see what you owned; its size is three sizes, and picking one resizes the pet
/// in the hero band; quiet hours are a band drawn across a whole day, which is the only form that
/// shows a window crossing midnight. Everything writes to the hero as it is changed, so a choice
/// can be judged before it is saved.
/// </para>
/// </remarks>
public sealed partial class SettingsWindow : Window
{
    /// <summary>One switch and the text that explains what turning it on does.</summary>
    private sealed record BehaviorRow(
        Grid Root,
        Border Icon,
        TextBlock Title,
        TextBlock Description,
        ToggleSwitch Switch);

    private readonly ISettingsStore _settings;
    private readonly List<BehaviorRow> _behaviorRows = [];
    private readonly List<Border> _separators = [];
    private readonly ToggleSwitch _companionEnabled = NewSwitch();
    private readonly ToggleSwitch _proactiveEnabled = NewSwitch();
    private readonly ToggleSwitch _alwaysVisible = NewSwitch();
    private readonly ToggleSwitch _petMovement = NewSwitch();

    private bool _loaded;
    private bool _dragonUnlocked;
    private int? _petPositionX;
    private int? _petPositionY;
    private CompanionPetKind _selectedPet = CompanionPetKind.Rabbit;
    private CompanionPetSize _petSize = CompanionPetSize.Large;

    public SettingsWindow(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        InitializeComponent();
        _settings = settings;

        AppWindow.Title = Strings.Get("Settings_Title");
        AppIcon.Apply(this);
        ApplyStrings();
        AppWindow.Resize(new SizeInt32(600, 880));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 520;
            presenter.PreferredMinimumHeight = 600;
        }

        BuildBehaviorRows();

        PetNameInput.TextChanged += (_, _) =>
        {
            UpdateHero();
            ClearStatus();
        };

        foreach (var toggle in new[] { _companionEnabled, _proactiveEnabled, _alwaysVisible, _petMovement })
        {
            toggle.Toggled += (_, _) =>
            {
                UpdateHero();
                UpdateAvailability();
                ClearStatus();
            };
        }

        foreach (var picker in new[] { QuietStart, QuietEnd })
        {
            picker.SelectedTimeChanged += (_, _) =>
            {
                UpdateQuietHours();
                ClearStatus();
            };
        }

        Render();

        // Tiles and rows are painted as they are built, so a theme switch rebuilds them. The state
        // this page holds lives in fields rather than in the controls it rebuilds.
        Board.ActualThemeChanged += (_, _) => Render();

        Activated += OnFirstActivated;
    }

    public event Action<CompanionSettings>? SettingsChanged;

    private bool IsDark =>
        Board.ActualTheme == ElementTheme.Dark
        || (Board.ActualTheme == ElementTheme.Default
            && Application.Current.RequestedTheme == ApplicationTheme.Dark);

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
            ShowError(ex.Message);
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
            SaveStatus.Text = "✓ " + Strings.Get("Settings_Saved");
            SaveStatus.Foreground = new SolidColorBrush(CompanionPalette.CareBudget(IsDark));
            SettingsChanged?.Invoke(value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write("Saving companion settings failed", ex);
            ShowError(ex.Message);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        SaveStatus.Text = message;
        SaveStatus.Foreground = new SolidColorBrush(
            CompanionPalette.Gauge(CompanionGaugeLevel.Low, IsDark));
    }

    /// <summary>A saved message that outlives the change it described is a lie.</summary>
    private void ClearStatus() => SaveStatus.Text = string.Empty;

    private void Apply(CompanionSettings value)
    {
        _dragonUnlocked = value.DragonUnlocked;
        _petPositionX = value.PetPositionX;
        _petPositionY = value.PetPositionY;
        _selectedPet = value.SelectedPet;
        _petSize = value.PetSize;
        PetNameInput.Text = value.PetName;
        _companionEnabled.IsOn = value.Enabled;
        _proactiveEnabled.IsOn = value.ProactiveEnabled;
        _alwaysVisible.IsOn = value.AlwaysVisible;
        _petMovement.IsOn = !value.ReduceMotion;
        QuietStart.Time = value.QuietStart.ToTimeSpan();
        QuietEnd.Time = value.QuietEnd.ToTimeSpan();

        Render();
        ClearStatus();
    }

    private CompanionSettings Read() => new()
    {
        Enabled = _companionEnabled.IsOn,
        ProactiveEnabled = _proactiveEnabled.IsOn,
        AlwaysVisible = _alwaysVisible.IsOn,
        ReduceMotion = !_petMovement.IsOn,
        SelectedPet = _selectedPet,
        PetName = CompanionSettings.NormalizePetName(PetNameInput.Text),
        PetSize = _petSize,
        PetPositionX = _petPositionX,
        PetPositionY = _petPositionY,
        DragonUnlocked = _dragonUnlocked,
        QuietStart = TimeOf(QuietStart),
        QuietEnd = TimeOf(QuietEnd),
        RuleVersion = CompanionSettings.CurrentRuleVersion,
    };

    private void Render()
    {
        ApplyPalette();
        BuildPetTiles();
        BuildSizeChoices();
        UpdateHero();
        UpdateQuietHours();
        UpdateAvailability();
    }

    private void ApplyPalette()
    {
        var dark = IsDark;

        Board.Background = new SolidColorBrush(CompanionPalette.Board(dark));

        var (from, to) = CompanionPalette.Hero(dark);
        HeroCard.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = from, Offset = 0 },
                new GradientStop { Color = to, Offset = 1 },
            },
        };

        var heroInk = new SolidColorBrush(CompanionPalette.HeroInk);
        var heroSubtle = new SolidColorBrush(CompanionPalette.HeroSubtle);
        var heroWell = new SolidColorBrush(CompanionPalette.HeroWell);

        AvatarWell.Background = heroWell;
        SpeciesPill.Background = heroWell;
        StatusPill.Background = heroWell;
        SpeciesPillText.Foreground = heroInk;
        HeroName.Foreground = heroInk;
        PageTitle.Foreground = heroSubtle;
        LocalOnlyLabel.Foreground = heroSubtle;

        var card = new SolidColorBrush(CompanionPalette.Card(dark));
        var cardBorder = new SolidColorBrush(CompanionPalette.CardBorder(dark));
        var ink = new SolidColorBrush(CompanionPalette.Ink(dark));
        var subtle = new SolidColorBrush(CompanionPalette.Subtle(dark));
        var character = CompanionPalette.AppBudget(dark);
        var behavior = CompanionPalette.CareBudget(dark);
        var quiet = CompanionPalette.Bond(dark);

        foreach (var panel in new[] { CharacterCard, BehaviorCard, QuietCard })
        {
            panel.Background = card;
            panel.BorderBrush = cardBorder;
        }

        foreach (var header in new[] { CharacterHeader, CompanionHeader, QuietHoursLabel })
        {
            header.Foreground = ink;
        }

        foreach (var label in new[]
                 {
                     CompanionDescription, PetPickerLabel, PetSizeLabel, QuietInactiveHint,
                     Tick0, Tick6, Tick12, Tick18, Tick24,
                 })
        {
            label.Foreground = subtle;
        }

        CharacterAccent.Background = new SolidColorBrush(character);
        BehaviorAccent.Background = new SolidColorBrush(behavior);
        QuietAccent.Background = new SolidColorBrush(quiet);
        QuietSummary.Foreground = new SolidColorBrush(quiet);
        QuietRail.Background = new SolidColorBrush(CompanionPalette.Track(dark));

        var note = CompanionPalette.Tile(CompanionTileState.Mission, dark);
        DragonLockBox.Background = new SolidColorBrush(note.Fill);
        DragonLockHint.Foreground = new SolidColorBrush(note.Ink);
        DragonLockBox.Visibility = _dragonUnlocked ? Visibility.Collapsed : Visibility.Visible;

        PaintNameInput(dark, character);
        PaintBehaviorRows(dark, behavior);
        PaintActionBar(dark, character);
    }

    /// <summary>
    /// The name field, in the card's colours rather than the system's.
    /// </summary>
    /// <remarks>
    /// A <see cref="TextBox"/> rebinds its background and border from theme resources on every
    /// visual state, so overriding those brushes on the control's own resources — the same trick
    /// the dashboard uses for its tiles — is what keeps it from turning system grey on hover.
    /// </remarks>
    private void PaintNameInput(bool dark, Color accent)
    {
        var field = new SolidColorBrush(CompanionPalette.Tile(CompanionTileState.Spent, dark).Fill);
        var border = new SolidColorBrush(CompanionPalette.CardBorder(dark));
        var focused = new SolidColorBrush(accent);
        var ink = new SolidColorBrush(CompanionPalette.Ink(dark));
        var subtle = new SolidColorBrush(CompanionPalette.Subtle(dark));

        PetNameInput.Background = field;
        PetNameInput.BorderBrush = border;
        PetNameInput.Foreground = ink;

        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver",
                     "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                 })
        {
            PetNameInput.Resources[key] = field;
        }

        PetNameInput.Resources["TextControlBorderBrush"] = border;
        PetNameInput.Resources["TextControlBorderBrushPointerOver"] = border;
        PetNameInput.Resources["TextControlBorderBrushDisabled"] = border;
        PetNameInput.Resources["TextControlBorderBrushFocused"] = focused;

        foreach (var key in new[]
                 {
                     "TextControlForeground", "TextControlForegroundPointerOver",
                     "TextControlForegroundFocused",
                 })
        {
            PetNameInput.Resources[key] = ink;
        }

        foreach (var key in new[]
                 {
                     "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
                     "TextControlPlaceholderForegroundFocused", "TextControlHeaderForeground",
                 })
        {
            PetNameInput.Resources[key] = subtle;
        }
    }

    private void PaintActionBar(bool dark, Color accent)
    {
        ActionBar.Background = new SolidColorBrush(CompanionPalette.Card(dark));
        ActionBar.BorderBrush = new SolidColorBrush(CompanionPalette.CardBorder(dark));
        SaveStatus.Foreground = new SolidColorBrush(CompanionPalette.Subtle(dark));

        // The one thing on the page that commits, so it is the one filled button on the page.
        var white = new SolidColorBrush(CompanionPalette.HeroInk);
        SaveButton.Background = new SolidColorBrush(accent);
        SaveButton.Foreground = white;
        SaveButton.BorderThickness = new Thickness(0);
        SaveButton.Resources["ButtonBackground"] = new SolidColorBrush(accent);
        SaveButton.Resources["ButtonBackgroundPointerOver"] =
            new SolidColorBrush(CompanionPalette.Lift(accent, dark));
        SaveButton.Resources["ButtonBackgroundPressed"] =
            new SolidColorBrush(CompanionPalette.Press(accent, dark));
        SaveButton.Resources["ButtonForeground"] = white;
        SaveButton.Resources["ButtonForegroundPointerOver"] = white;
        SaveButton.Resources["ButtonForegroundPressed"] = white;

        Paint(CloseButton, CompanionPalette.Tile(CompanionTileState.Ready, dark), dark);
    }

    /// <summary>
    /// The seven pets, as seven portraits.
    /// </summary>
    /// <remarks>
    /// The dropdown this replaces asked a user to open it before they could see what they had, and
    /// showed the locked dragon as one greyed line among six. A wall makes the collection the
    /// point: what is chosen is outlined in the card's own indigo, and what is still locked wears
    /// its lock on the portrait.
    /// </remarks>
    private void BuildPetTiles()
    {
        const int columns = 4;

        PetTileHost.Children.Clear();
        PetTileHost.RowDefinitions.Clear();

        var count = CompanionPetCatalog.All.Count;
        for (var row = 0; row < ((count + columns - 1) / columns); row++)
        {
            PetTileHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < count; index++)
        {
            var tile = PetTile(CompanionPetCatalog.All[index]);
            Grid.SetColumn(tile, index % columns);
            Grid.SetRow(tile, index / columns);
            PetTileHost.Children.Add(tile);
        }
    }

    private Button PetTile(CompanionPetKind pet)
    {
        var dark = IsDark;
        var locked = pet == CompanionPetKind.Dragon && !_dragonUnlocked;
        var selected = pet == _selectedPet && !locked;
        var accent = CompanionPalette.AppBudget(dark);

        var sprite = new Grid
        {
            Width = 45,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = locked ? 0.4 : 1,
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 45, 60) },
        };
        sprite.Children.Add(new Image
        {
            Width = 180,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Left,
            Stretch = Stretch.Fill,
            Source = new BitmapImage(new Uri(
                $"ms-appx:///Assets/Companion/{pet.AssetKey()}-walk.png")),
        });

        var portrait = new Grid();
        portrait.Children.Add(sprite);
        if (locked)
        {
            portrait.Children.Add(new TextBlock
            {
                Text = "\U0001F512",
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var surface = selected
            ? new CompanionTileSurface(Wash(accent, dark), accent, accent, accent)
            : CompanionPalette.Tile(
                locked ? CompanionTileState.Spent : CompanionTileState.Ready,
                dark);

        var name = new TextBlock
        {
            Text = Strings.Get($"Settings_Pet{pet}"),
            FontSize = 11.5,
            FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = new SolidColorBrush(surface.Ink),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var content = new StackPanel { Spacing = 3 };
        content.Children.Add(portrait);
        content.Children.Add(name);

        // The selected tile is outlined a pixel thicker; its padding gives that pixel back so the
        // portrait does not shift when the selection moves.
        var button = new Button
        {
            Content = content,
            IsEnabled = !locked,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Padding = new Thickness(selected ? 5 : 6, selected ? 8 : 9, selected ? 5 : 6, selected ? 8 : 9),
        };

        Paint(button, surface, dark);
        button.Click += (_, _) =>
        {
            _selectedPet = pet;
            BuildPetTiles();
            UpdateHero();
            ClearStatus();
        };

        return button;
    }

    /// <summary>Three sizes, drawn at three sizes. "Small" is a word; this is a size.</summary>
    private void BuildSizeChoices()
    {
        var dark = IsDark;
        var accent = CompanionPalette.AppBudget(dark);

        PetSizeHost.Children.Clear();

        var sizes = new[]
        {
            (Size: CompanionPetSize.Small, Key: "Settings_PetSizeSmall", Dot: 7d),
            (Size: CompanionPetSize.Medium, Key: "Settings_PetSizeMedium", Dot: 11d),
            (Size: CompanionPetSize.Large, Key: "Settings_PetSizeLarge", Dot: 15d),
        };

        for (var index = 0; index < sizes.Length; index++)
        {
            var (size, key, dot) = sizes[index];
            var selected = size == _petSize;
            var surface = selected
                ? new CompanionTileSurface(accent, accent, CompanionPalette.HeroInk, CompanionPalette.HeroInk)
                : CompanionPalette.Tile(CompanionTileState.Ready, dark);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            row.Children.Add(new Border
            {
                Width = dot,
                Height = dot,
                CornerRadius = new CornerRadius(dot / 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(selected ? surface.Ink : accent),
            });
            row.Children.Add(new TextBlock
            {
                Text = Strings.Get(key),
                FontSize = 12.5,
                FontWeight = selected
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = new SolidColorBrush(surface.Ink),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var button = new Button
            {
                Content = row,
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 9, 4, 9),
            };

            Paint(button, surface, dark);
            button.Click += (_, _) =>
            {
                _petSize = size;
                BuildSizeChoices();
                UpdateHero();
                ClearStatus();
            };

            Grid.SetColumn(button, index);
            PetSizeHost.Children.Add(button);
        }
    }

    private void BuildBehaviorRows()
    {
        var rows = new[]
        {
            (Emoji: "\U0001F43E", Title: "Settings_CompanionEnabled",
                Description: "Settings_CompanionEnabledDescription", Switch: _companionEnabled),
            (Emoji: "\U0001F4AC", Title: "Settings_Proactive",
                Description: "Settings_ProactiveDescription", Switch: _proactiveEnabled),
            (Emoji: "\U0001F4CC", Title: "Settings_AlwaysVisible",
                Description: "Settings_AlwaysVisibleDescription", Switch: _alwaysVisible),
            (Emoji: "\U0001F3C3", Title: "Settings_ReduceMotion",
                Description: "Settings_ReduceMotionDescription", Switch: _petMovement),
        };

        foreach (var (emoji, title, description, toggle) in rows)
        {
            if (BehaviorRows.Children.Count > 0)
            {
                var separator = new Border { Height = 1 };
                _separators.Add(separator);
                BehaviorRows.Children.Add(separator);
            }

            var row = NewBehaviorRow(emoji, Strings.Get(title), Strings.Get(description), toggle);
            _behaviorRows.Add(row);
            BehaviorRows.Children.Add(row.Root);
        }
    }

    private static BehaviorRow NewBehaviorRow(
        string emoji,
        string title,
        string description,
        ToggleSwitch toggle)
    {
        var root = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 11, 0, 11) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(11),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = emoji,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };

        var descriptionBlock = new TextBlock
        {
            Text = description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(titleBlock);
        text.Children.Add(descriptionBlock);

        Grid.SetColumn(text, 1);
        Grid.SetColumn(toggle, 2);
        root.Children.Add(icon);
        root.Children.Add(text);
        root.Children.Add(toggle);

        return new BehaviorRow(root, icon, titleBlock, descriptionBlock, toggle);
    }

    private void PaintBehaviorRows(bool dark, Color accent)
    {
        var ink = new SolidColorBrush(CompanionPalette.Ink(dark));
        var subtle = new SolidColorBrush(CompanionPalette.Subtle(dark));
        var wash = new SolidColorBrush(Wash(accent, dark));
        var separator = new SolidColorBrush(CompanionPalette.CardBorder(dark));

        foreach (var row in _behaviorRows)
        {
            row.Icon.Background = wash;
            row.Title.Foreground = ink;
            row.Description.Foreground = subtle;
            PaintSwitch(row.Switch, dark, accent);
        }

        foreach (var line in _separators)
        {
            line.Background = separator;
        }
    }

    /// <summary>
    /// A switch in the card's teal rather than the Windows accent colour.
    /// </summary>
    /// <remarks>
    /// The system accent is whatever the user set for Windows, which on this page collides with
    /// the indigo that means "the pet" and the purple that means "quiet". Teal here means the same
    /// thing it means on the dashboard: what the companion does for you.
    /// </remarks>
    private static void PaintSwitch(ToggleSwitch toggle, bool dark, Color accent)
    {
        var on = new SolidColorBrush(accent);
        var onLift = new SolidColorBrush(CompanionPalette.Lift(accent, dark));
        var onPress = new SolidColorBrush(CompanionPalette.Press(accent, dark));
        var off = new SolidColorBrush(CompanionPalette.Track(dark));
        var offStroke = new SolidColorBrush(CompanionPalette.Subtle(dark));
        var knobOn = new SolidColorBrush(CompanionPalette.HeroInk);
        var knobOff = new SolidColorBrush(CompanionPalette.Subtle(dark));

        toggle.Resources["ToggleSwitchFillOn"] = on;
        toggle.Resources["ToggleSwitchFillOnPointerOver"] = onLift;
        toggle.Resources["ToggleSwitchFillOnPressed"] = onPress;
        toggle.Resources["ToggleSwitchStrokeOn"] = on;
        toggle.Resources["ToggleSwitchStrokeOnPointerOver"] = onLift;
        toggle.Resources["ToggleSwitchStrokeOnPressed"] = onPress;
        toggle.Resources["ToggleSwitchKnobFillOn"] = knobOn;
        toggle.Resources["ToggleSwitchKnobFillOnPointerOver"] = knobOn;
        toggle.Resources["ToggleSwitchKnobFillOnPressed"] = knobOn;

        toggle.Resources["ToggleSwitchFillOff"] = off;
        toggle.Resources["ToggleSwitchFillOffPointerOver"] = off;
        toggle.Resources["ToggleSwitchFillOffPressed"] = off;
        toggle.Resources["ToggleSwitchStrokeOff"] = offStroke;
        toggle.Resources["ToggleSwitchStrokeOffPointerOver"] = offStroke;
        toggle.Resources["ToggleSwitchStrokeOffPressed"] = offStroke;
        toggle.Resources["ToggleSwitchKnobFillOff"] = knobOff;
        toggle.Resources["ToggleSwitchKnobFillOffPointerOver"] = knobOff;
        toggle.Resources["ToggleSwitchKnobFillOffPressed"] = knobOff;
    }

    private void UpdateHero()
    {
        AvatarSpriteStrip.Source = new BitmapImage(new Uri(
            $"ms-appx:///Assets/Companion/{_selectedPet.AssetKey()}-walk.png"));

        // Not the raw scale — a third-size pet in an 88pt well is a speck. Enough of the difference
        // to see which one is selected, in the order the three sizes actually are.
        var scale = _petSize switch
        {
            CompanionPetSize.Small => 0.68,
            CompanionPetSize.Medium => 0.84,
            _ => 1.0,
        };
        AvatarSizeTransform.ScaleX = scale;
        AvatarSizeTransform.ScaleY = scale;

        HeroName.Text = CompanionSettings.NormalizePetName(PetNameInput.Text);
        SpeciesPillText.Text = Strings.Get($"Settings_Pet{_selectedPet}");

        var on = _companionEnabled.IsOn;
        StatusPillText.Text = Strings.Get(on ? "Settings_StatusOn" : "Settings_StatusOff");
        StatusPillText.Foreground = new SolidColorBrush(
            on ? CompanionPalette.HeroInk : CompanionPalette.HeroSubtle);
    }

    /// <summary>
    /// The quiet window as a band across one day.
    /// </summary>
    /// <remarks>
    /// The band is three weighted columns rather than a positioned shape, so it survives a resized
    /// window with no layout pass of our own. A window that wraps past midnight paints the outer
    /// two; one that does not paints the middle. Start equal to end means never quiet, which is
    /// what <see cref="CompanionSettings.IsQuietAt"/> does with it, so the rail stays empty.
    /// </remarks>
    private void UpdateQuietHours()
    {
        var dark = IsDark;
        var start = TimeOf(QuietStart);
        var end = TimeOf(QuietEnd);
        var quiet = new SolidColorBrush(CompanionPalette.Bond(dark));
        var track = new SolidColorBrush(CompanionPalette.Track(dark));

        var startOfDay = start.ToTimeSpan().TotalMinutes / 1440d;
        var endOfDay = end.ToTimeSpan().TotalMinutes / 1440d;

        if (start == end)
        {
            SetSpans(1, 0, 0);
            QuietFillA.Background = track;
            QuietFillB.Background = track;
            QuietFillC.Background = track;
            QuietSummary.Text = Strings.Get("Settings_QuietNone");
            QuietSummary.Foreground = new SolidColorBrush(CompanionPalette.Subtle(dark));
            return;
        }

        if (start < end)
        {
            SetSpans(startOfDay, endOfDay - startOfDay, 1 - endOfDay);
            QuietFillA.Background = track;
            QuietFillB.Background = quiet;
            QuietFillC.Background = track;
        }
        else
        {
            SetSpans(endOfDay, startOfDay - endOfDay, 1 - startOfDay);
            QuietFillA.Background = quiet;
            QuietFillB.Background = track;
            QuietFillC.Background = quiet;
        }

        var minutes = (int)Math.Round(((end - start).TotalMinutes + 1440) % 1440);
        QuietSummary.Text = Strings.Format(
            "Settings_QuietWindowFormat",
            start.ToString("HH:mm", CultureInfo.CurrentCulture),
            end.ToString("HH:mm", CultureInfo.CurrentCulture),
            Duration(minutes));
        QuietSummary.Foreground = quiet;
    }

    /// <summary>
    /// A picker's time, or midnight while it has none.
    /// </summary>
    /// <remarks>
    /// The window paints itself once in its constructor and only loads the stored settings when it
    /// is first activated, so it reads both pickers while they are still untouched. An untouched
    /// <see cref="TimePicker"/> reports a sentinel outside a day, which
    /// <see cref="TimeOnly.FromTimeSpan"/> throws on.
    /// </remarks>
    private static TimeOnly TimeOf(TimePicker picker)
    {
        var value = picker.SelectedTime ?? picker.Time;
        return value >= TimeSpan.Zero && value < TimeSpan.FromDays(1)
            ? TimeOnly.FromTimeSpan(value)
            : TimeOnly.MinValue;
    }

    private void SetSpans(double a, double b, double c)
    {
        QuietSpanA.Width = new GridLength(Math.Max(0, a), GridUnitType.Star);
        QuietSpanB.Width = new GridLength(Math.Max(0, b), GridUnitType.Star);
        QuietSpanC.Width = new GridLength(Math.Max(0, c), GridUnitType.Star);
    }

    private static string Duration(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;

        if (hours > 0 && rest > 0)
        {
            return Strings.Format("Settings_DurationHoursMinutesFormat", hours, rest);
        }

        return hours > 0
            ? Strings.Format("Settings_DurationHoursFormat", hours)
            : Strings.Format("Settings_DurationMinutesFormat", rest);
    }

    /// <summary>
    /// Dims what the switches above have turned off.
    /// </summary>
    /// <remarks>
    /// Three of the four switches do nothing while the companion is off, and quiet hours only ever
    /// gated proactive suggestions. Leaving them live invited a user to set a quiet window that
    /// could never apply. Disabling does not touch <c>IsOn</c>, so nothing is lost on save.
    /// </remarks>
    private void UpdateAvailability()
    {
        var enabled = _companionEnabled.IsOn;

        foreach (var row in _behaviorRows.Skip(1))
        {
            row.Switch.IsEnabled = enabled;
            row.Root.Opacity = enabled ? 1 : 0.4;
        }

        var quietApplies = enabled && _proactiveEnabled.IsOn;
        QuietStart.IsEnabled = quietApplies;
        QuietEnd.IsEnabled = quietApplies;
        QuietBody.Opacity = quietApplies ? 1 : 0.4;
        QuietInactiveHint.Visibility = quietApplies ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Paints a button in one of the palette's surfaces.
    /// </summary>
    /// <remarks>
    /// Same reason as the dashboard's tiles: the template rebinds background and border from theme
    /// resources on every visual state, so a selected tile would turn system grey the moment the
    /// pointer touched it.
    /// </remarks>
    private static void Paint(Button button, CompanionTileSurface surface, bool dark)
    {
        var fill = new SolidColorBrush(surface.Fill);
        var border = new SolidColorBrush(surface.Border);
        var ink = new SolidColorBrush(surface.Ink);

        button.Background = fill;
        button.BorderBrush = border;
        button.Foreground = ink;
        button.Resources["ButtonBackground"] = fill;
        button.Resources["ButtonBackgroundDisabled"] = fill;
        button.Resources["ButtonBackgroundPointerOver"] =
            new SolidColorBrush(CompanionPalette.Lift(surface.Fill, dark));
        button.Resources["ButtonBackgroundPressed"] =
            new SolidColorBrush(CompanionPalette.Press(surface.Fill, dark));
        button.Resources["ButtonBorderBrush"] = border;
        button.Resources["ButtonBorderBrushDisabled"] = border;
        button.Resources["ButtonBorderBrushPointerOver"] = border;
        button.Resources["ButtonBorderBrushPressed"] = border;
        button.Resources["ButtonForeground"] = ink;
        button.Resources["ButtonForegroundPointerOver"] = ink;
        button.Resources["ButtonForegroundPressed"] = ink;
        button.Resources["ButtonForegroundDisabled"] = ink;
    }

    /// <summary>The accent at card strength: enough to tint a well, not enough to shout.</summary>
    private static Color Wash(Color accent, bool dark) =>
        Color.FromArgb((byte)(dark ? 0x33 : 0x1F), accent.R, accent.G, accent.B);

    /// <summary>
    /// A switch with its own on/off wording removed: the row already says what it does, and the
    /// default reserves 100pt of width for a second copy of the answer.
    /// </summary>
    private static ToggleSwitch NewSwitch() => new()
    {
        OnContent = null,
        OffContent = null,
        MinWidth = 0,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void ApplyStrings()
    {
        PageTitle.Text = Strings.Get("Settings_Title");
        LocalOnlyLabel.Text = Strings.Get("Settings_LocalOnly");
        CharacterHeader.Text = Strings.Get("Settings_CharacterHeader");
        PetNameInput.Header = Strings.Get("Settings_PetName");
        PetNameInput.PlaceholderText = Strings.Get("Settings_PetNamePlaceholder");
        PetPickerLabel.Text = Strings.Get("Settings_PetPicker");
        PetSizeLabel.Text = Strings.Get("Settings_PetSize");
        DragonLockHint.Text = Strings.Get("Settings_DragonUnlockHint");
        CompanionHeader.Text = Strings.Get("Settings_CompanionHeader");
        CompanionDescription.Text = Strings.Get("Settings_CompanionDescription");
        QuietHoursLabel.Text = Strings.Get("Settings_QuietHours");
        QuietStart.Header = Strings.Get("Settings_QuietStart");
        QuietEnd.Header = Strings.Get("Settings_QuietEnd");
        QuietInactiveHint.Text = Strings.Get("Settings_QuietInactiveHint");
        SaveButton.Content = Strings.Get("Settings_Save");
        CloseButton.Content = Strings.Get("Ai_Close");
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
