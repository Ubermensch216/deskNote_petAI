using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DeskNote.App.Views;

/// <summary>A paged album of facts, rendered in the current language without copying note text.</summary>
public sealed class CompanionAlbumWindow : Window
{
    private readonly Func<CompanionMemoryQuery, Task<IReadOnlyList<CompanionMemory>>> _read;
    private readonly StackPanel _cards = new() { Spacing = 12 };
    private readonly ComboBox _pet = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CalendarDatePicker _date = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _more = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private int _offset;
    private int _generation;
    private bool _closed;
    private readonly HashSet<long> _shownCards = [];

    public CompanionAlbumWindow(Func<CompanionMemoryQuery, Task<IReadOnlyList<CompanionMemory>>> read)
    {
        _read = read;
        AppWindow.Title = Strings.Get("Companion_AlbumTitle");
        AppWindow.Resize(new SizeInt32(480, 700));
        AppIcon.Apply(this);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 360;
            presenter.PreferredMinimumHeight = 420;
        }

        _pet.Header = Strings.Get("Companion_AlbumPet");
        _pet.Items.Add(Strings.Get("Companion_AlbumAllPets"));
        foreach (var pet in CompanionPetCatalog.All)
        {
            _pet.Items.Add(Strings.Get($"Settings_Pet{pet}"));
        }

        _pet.SelectedIndex = 0;
        _date.Header = Strings.Get("Companion_AlbumDate");
        _date.PlaceholderText = Strings.Get("Companion_AlbumAllDates");
        _more.Content = Strings.Get("Companion_AlbumMore");
        var clear = new Button { Content = Strings.Get("Companion_AlbumClearDate") };
        clear.Click += (_, _) => _date.Date = null;
        var refresh = new Button { Content = Strings.Get("Companion_Refresh") };
        refresh.Click += async (_, _) => await LoadAsync(true);
        _pet.SelectionChanged += async (_, _) => await LoadAsync(true);
        _date.DateChanged += async (_, _) => await LoadAsync(true);
        _more.Click += async (_, _) => await LoadAsync(false);
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = Strings.Get("Companion_AlbumTitle"),
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock { Text = Strings.Get("Companion_AlbumPrivacy"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_pet);
        panel.Children.Add(_date);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(clear);
        actions.Children.Add(refresh);
        panel.Children.Add(actions);
        panel.Children.Add(_status);
        panel.Children.Add(_cards);
        panel.Children.Add(_more);
        var scroll = new ScrollViewer { Content = panel };
        void ApplyTheme() => scroll.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            DeskNote.App.Theming.CompanionPalette.Board(panel.ActualTheme == ElementTheme.Dark));
        panel.ActualThemeChanged += (_, _) => ApplyTheme();
        Content = scroll;
        ApplyTheme();
        panel.Loaded += async (_, _) => await LoadAsync(true);
        Closed += (_, _) => { _closed = true; _generation++; };
    }

    private async Task LoadAsync(bool reset)
    {
        var generation = ++_generation;
        if (reset)
        {
            _offset = 0;
            _cards.Children.Clear();
            _shownCards.Clear();
        }

        _more.IsEnabled = false;
        _status.Text = Strings.Get("Companion_AlbumLoading");
        try
        {
            var query = new CompanionMemoryQuery(_offset,
                _pet.SelectedIndex > 0 ? CompanionPetCatalog.All[_pet.SelectedIndex - 1] : null,
                _date.Date is { } date ? DateOnly.FromDateTime(date.DateTime) : null);
            var memories = await _read(query);
            if (_closed || generation != _generation)
            {
                return;
            }

            foreach (var memory in memories)
            {
                if (!_shownCards.Add(memory.Id))
                {
                    continue;
                }

                var card = new StackPanel { Spacing = 6 };
                card.Children.Add(new TextBlock
                {
                    Text = $"{memory.LocalDate:yyyy-MM-dd} · {Strings.Get($"Settings_Pet{memory.Pet}")}",
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                });
                card.Children.Add(new TextBlock
                {
                    Text = Strings.Get($"Companion_Memory{memory.TemplateId}"),
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                });
                _cards.Children.Add(new Border
                {
                    Child = card,
                    Padding = new Thickness(16),
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                });
            }

            _offset += memories.Count;
            _status.Text = _offset == 0 ? Strings.Get("Companion_AlbumEmpty") : "";
            _more.Visibility = memories.Count == CompanionMemoryQuery.PageSize ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!_closed && generation == _generation)
            {
                _status.Text = Strings.Get("Companion_AlbumError");
            }

            CrashLog.Write("Reading the companion album failed", ex);
        }
        finally
        {
            if (!_closed && generation == _generation)
            {
                _more.IsEnabled = true;
            }
        }
    }
}
