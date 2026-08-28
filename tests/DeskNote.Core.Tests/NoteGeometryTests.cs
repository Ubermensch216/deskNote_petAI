using DeskNote.Core.Models;

namespace DeskNote.Core.Tests;

public class NoteGeometryTests
{
    [Theory]
    [InlineData(NoteSizePreset.Small, 240, 180)]
    [InlineData(NoteSizePreset.Medium, 320, 260)]
    [InlineData(NoteSizePreset.Large, 420, 360)]
    public void Presets_match_the_specified_sizes(NoteSizePreset preset, int width, int height)
    {
        Assert.Equal((width, height), NoteGeometry.SizeOf(preset));
    }

    [Fact]
    public void Custom_falls_back_to_the_medium_size()
    {
        Assert.Equal(NoteGeometry.SizeOf(NoteSizePreset.Medium), NoteGeometry.SizeOf(NoteSizePreset.Custom));
    }

    /// <summary>
    /// A hand-resized note has to stop claiming to be a preset, otherwise re-applying the preset
    /// later would silently resize it back.
    /// </summary>
    [Fact]
    public void Resizing_away_from_a_preset_marks_the_note_custom()
    {
        var geometry = NoteGeometry.ForPreset(NoteSizePreset.Medium, 10, 20, "DISPLAY1");

        var resized = geometry.WithSize(333, 277);

        Assert.Equal(NoteSizePreset.Custom, resized.Preset);
        Assert.Equal(333, resized.Width);
        Assert.Equal(277, resized.Height);
    }

    /// <summary>And the reverse: dragging back to an exact preset size should be recognized as one.</summary>
    [Fact]
    public void Resizing_onto_a_preset_size_re_derives_the_preset()
    {
        var geometry = NoteGeometry.ForPreset(NoteSizePreset.Medium, 10, 20, "DISPLAY1").WithSize(333, 277);

        var snapped = geometry.WithSize(NoteGeometry.LargeWidth, NoteGeometry.LargeHeight);

        Assert.Equal(NoteSizePreset.Large, snapped.Preset);
    }

    [Fact]
    public void Resizing_preserves_position_and_monitor()
    {
        var geometry = NoteGeometry.ForPreset(NoteSizePreset.Small, 640, 480, @"\.\DISPLAY3");

        var resized = geometry.WithSize(500, 400);

        Assert.Equal(640, resized.X);
        Assert.Equal(480, resized.Y);
        Assert.Equal(@"\.\DISPLAY3", resized.MonitorKey);
    }

    [Fact]
    public void Default_geometry_is_a_medium_note_with_no_monitor_recorded()
    {
        Assert.Equal(NoteSizePreset.Medium, NoteGeometry.Default.Preset);
        Assert.Equal(NoteGeometry.UnknownMonitor, NoteGeometry.Default.MonitorKey);
    }
}
