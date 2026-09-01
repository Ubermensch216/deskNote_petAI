using DeskNote.App.Services;
using DeskNote.App.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.System;

namespace DeskNote.App.Views;

/// <summary>An image attached to a note, in the terms the window needs to draw it.</summary>
/// <param name="AttachmentId">Identifies the record, so the window can say which one to detach.</param>
/// <param name="Path">Where the bytes are: inside the attachment store, or wherever the user keeps them.</param>
/// <param name="DisplayName">The file's name, for the tooltip and for screen readers.</param>
public readonly record struct NoteImage(Guid AttachmentId, string Path, string DisplayName);

/// <summary>
/// The part of a note window that shows attached images as images.
/// </summary>
/// <remarks>
/// <para>
/// Attaching a screenshot used to write <c>![shot.png](C:/Users/…/a1b2…png)</c> into the body, and
/// a sticky note would then be four lines of percent-escaped path where a picture should be. The
/// bytes were always stored and recorded against the note; nothing ever drew them. This does.
/// </para>
/// <para>
/// The strip is built in code rather than bound to a collection because a note holds a handful of
/// images at most, and each one needs its own decode from disk, its own failure to survive, and
/// its own natural size — none of which a template does well and all of which are three lines here.
/// </para>
/// </remarks>
public sealed partial class NoteWindow
{
    /// <summary>
    /// Tallest a single image is drawn, in DIPs.
    /// </summary>
    /// <remarks>
    /// A note is a note. A full-height phone screenshot would otherwise fill the window and leave
    /// the writing below the fold; capped, it reads as an illustration of the text and the whole
    /// image is one click away in the system viewer.
    /// </remarks>
    private const double MaxImageHeight = 220;

    private readonly List<Button> _imageRemoveButtons = [];
    private bool _imageChromeVisible;

    /// <summary>Raised when the user asks for an attached image to be taken off the note.</summary>
    public event EventHandler<NoteImage>? ImageRemoveRequested;

    /// <summary>Replaces the strip with the note's images, in the order they were attached.</summary>
    public void ShowImages(IReadOnlyList<NoteImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);

        ImageStrip.Children.Clear();
        _imageRemoveButtons.Clear();

        foreach (var image in images)
        {
            AddImage(image);
        }
    }

    /// <summary>Appends one image, for an attachment that has just been stored.</summary>
    public void AddImage(NoteImage image)
    {
        ImageStrip.Children.Add(BuildCard(image));
        ImageStripScroller.Visibility = Visibility.Visible;
    }

    /// <summary>Drops an image from the strip, after its attachment has been removed.</summary>
    public void RemoveImage(Guid attachmentId)
    {
        var card = ImageStrip.Children
            .OfType<FrameworkElement>()
            .FirstOrDefault(child => child.Tag is NoteImage image && image.AttachmentId == attachmentId);

        if (card is null)
        {
            return;
        }

        ImageStrip.Children.Remove(card);
        _imageRemoveButtons.RemoveAll(button => button.Tag is NoteImage tag && tag.AttachmentId == attachmentId);

        if (ImageStrip.Children.Count == 0)
        {
            ImageStripScroller.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Keeps the images from crowding out the writing.
    /// </summary>
    /// <remarks>
    /// The strip sits in an auto-sized row above the editor, so without a ceiling a tall
    /// screenshot on a small note would take the whole surface and leave the text nowhere to go.
    /// Half the note, and never less than the height of a thumbnail.
    /// </remarks>
    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ImageStripScroller.MaxHeight = Math.Max(96, e.NewSize.Height * 0.5);
    }

    /// <summary>Follows the rest of the chrome: the remove buttons appear with it.</summary>
    private void SetImageChromeVisible(bool visible)
    {
        _imageChromeVisible = visible;

        foreach (var button in _imageRemoveButtons)
        {
            button.Opacity = visible ? 1 : 0;
            button.IsHitTestVisible = visible;
        }
    }

    /// <summary>Repaints the plate behind the remove buttons after a colour change.</summary>
    private void ApplyImageChromeBrushes()
    {
        var plate = RemoveButtonPlate();

        foreach (var button in _imageRemoveButtons)
        {
            button.Background = plate;
        }
    }

    /// <summary>
    /// Paper, at nearly full opacity, so the ✕ stays readable over whatever the image happens to
    /// be. A transparent chrome button works on paper and disappears on a photograph.
    /// </summary>
    private SolidColorBrush RemoveButtonPlate() =>
        new(NotePalette.WithOpacity(NotePalette.Resolve(_colorKey, IsDarkTheme).Background, 0.88));

    private FrameworkElement BuildCard(NoteImage image)
    {
        var chromeStyle = (Style)Surface.Resources["ChromeButtonStyle"];

        var picture = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxHeight = MaxImageHeight,
        };

        var missing = new TextBlock
        {
            Text = $"⚠ {image.DisplayName} — {Strings.Get("Note_ImageMissing")}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.75,
            Visibility = Visibility.Collapsed,
        };

        // A button rather than a tapped image: opening the file is a command, and it should be
        // reachable by tab and Enter like every other command on the note.
        var frame = new Button
        {
            Style = chromeStyle,
            Width = double.NaN,
            Height = double.NaN,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Children = { picture, missing } },
        };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(frame, image.DisplayName);
        ToolTipService.SetToolTip(frame, image.DisplayName);
        frame.Click += async (_, _) => await OpenImageAsync(image).ConfigureAwait(true);

        var remove = new Button
        {
            Style = chromeStyle,
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 4, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = RemoveButtonPlate(),
            Opacity = _imageChromeVisible ? 1 : 0,
            IsHitTestVisible = _imageChromeVisible,
            Tag = image,
            Content = new FontIcon { Glyph = "\uE711", FontSize = 11 },
        };

        remove.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) };
        Describe(remove, "Note_ImageRemove");
        remove.Click += (_, _) => ImageRemoveRequested?.Invoke(this, image);

        _imageRemoveButtons.Add(remove);

        var card = new Grid { Tag = image, Children = { frame, remove } };

        _ = LoadImageAsync(picture, missing, image.Path);
        return card;
    }

    /// <summary>
    /// Decodes the file into the strip, and says so plainly when it cannot.
    /// </summary>
    /// <remarks>
    /// A linked image lives where the user put it and can be renamed or removed behind the note's
    /// back. Showing its name and a warning beats an empty box: it tells them which file to go
    /// looking for, and the remove button beside it is still there to tidy the note up with.
    /// </remarks>
    private async Task LoadImageAsync(Image target, TextBlock missing, string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            // Never blown up past its own resolution: a 48-pixel icon stretched across the note
            // is a smear. PixelWidth is physical, the layout is in DIPs, so the display scale is
            // what converts one to the other.
            var scale = Surface.XamlRoot?.RasterizationScale ?? 1.0;
            target.MaxWidth = bitmap.PixelWidth / scale;
            target.MaxHeight = Math.Min(MaxImageHeight, bitmap.PixelHeight / scale);
            target.Source = bitmap;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            target.Visibility = Visibility.Collapsed;
            missing.Visibility = Visibility.Visible;
            CrashLog.Write($"Showing attached image {path} failed", ex);
        }
    }

    /// <summary>Hands the image to whatever the user opens pictures with.</summary>
    private static async Task OpenImageAsync(NoteImage image)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(image.Path);
            _ = await Launcher.LaunchFileAsync(file);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CrashLog.Write($"Opening attached image {image.Path} failed", ex);
        }
    }
}
