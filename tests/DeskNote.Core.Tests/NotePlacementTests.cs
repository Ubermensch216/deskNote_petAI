using DeskNote.Core.Models;
using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class NotePlacementTests
{
    private static readonly WorkArea Primary = new(@"\\.\DISPLAY1", 0, 0, 1920, 1032);

    /// <summary>A second display to the right, as in a typical two-monitor desk setup.</summary>
    private static readonly WorkArea Secondary = new(@"\\.\DISPLAY2", 1920, 0, 3840, 1032);

    /// <summary>A second display to the *left*, which gives the note negative coordinates.</summary>
    private static readonly WorkArea LeftOfPrimary = new(@"\\.\DISPLAY3", -1920, 0, 0, 1032);

    private static bool IsReachable(NoteGeometry geometry, params WorkArea[] monitors) =>
        monitors.Any(m =>
            geometry.X + NotePlacement.MinimumVisibleEdge > m.Left
            && geometry.X < m.Right
            && geometry.Y + NotePlacement.MinimumVisibleEdge > m.Top
            && geometry.Y < m.Bottom);

    [Fact]
    public void A_note_on_an_attached_display_is_left_where_it_is()
    {
        var geometry = new NoteGeometry(400, 300, 320, 260, @"\\.\DISPLAY1", NoteSizePreset.Medium);

        var result = NotePlacement.ClampToVisibleArea(geometry, [Primary, Secondary]);

        Assert.Equal(geometry, result);
    }

    /// <summary>
    /// The regression this whole type exists for. A note last used on a display that has since
    /// been unplugged must come back somewhere reachable — notes have no taskbar button, so an
    /// off-screen note is gone for good.
    /// </summary>
    [Fact]
    public void A_note_from_an_unplugged_display_comes_back_on_screen()
    {
        var geometry = new NoteGeometry(4200, 2600, 420, 360, @"\\.\DISPLAY7", NoteSizePreset.Large);

        var result = NotePlacement.ClampToVisibleArea(geometry, [Primary]);

        Assert.True(IsReachable(result, Primary), $"Restored off-screen at {result.X},{result.Y}.");
        Assert.Equal(Primary.DeviceName, result.MonitorKey);
    }

    /// <summary>
    /// The same situation the bug produced: the recorded device name matches nothing. A
    /// first-match-or-default lookup silently yields a struct with a null device name, and the
    /// note window then fails to appear at all.
    /// </summary>
    [Theory]
    [InlineData(@"\\.\DISPLAY9")]
    [InlineData("")]
    [InlineData("MONITOR-FROM-ANOTHER-MACHINE")]
    public void An_unrecognized_display_name_never_produces_a_null_key(string monitorKey)
    {
        var geometry = new NoteGeometry(100, 100, 320, 260, monitorKey, NoteSizePreset.Medium);

        var result = NotePlacement.ClampToVisibleArea(geometry, [Primary, Secondary]);

        Assert.False(string.IsNullOrEmpty(result.MonitorKey));
    }

    /// <summary>
    /// When the recorded display is gone, prefer the one the saved position actually falls on.
    /// Snapping everything to primary would collapse a carefully arranged two-monitor desktop.
    /// </summary>
    [Fact]
    public void A_missing_display_falls_back_to_the_one_containing_the_saved_point()
    {
        var geometry = new NoteGeometry(2400, 200, 320, 260, @"\\.\GONE", NoteSizePreset.Medium);

        var result = NotePlacement.ClampToVisibleArea(geometry, [Primary, Secondary]);

        Assert.Equal(Secondary.DeviceName, result.MonitorKey);
        Assert.Equal(2400, result.X);
    }

    [Fact]
    public void Negative_coordinates_on_a_left_hand_display_are_preserved()
    {
        var geometry = new NoteGeometry(-1200, 300, 320, 260, @"\\.\DISPLAY3", NoteSizePreset.Medium);

        var result = NotePlacement.ClampToVisibleArea(geometry, [LeftOfPrimary, Primary]);

        Assert.Equal(-1200, result.X);
        Assert.Equal(LeftOfPrimary.DeviceName, result.MonitorKey);
    }

    [Fact]
    public void A_note_larger_than_the_display_is_shrunk_to_fit()
    {
        var small = new WorkArea(@"\\.\SMALL", 0, 0, 300, 240);
        var geometry = new NoteGeometry(0, 0, 1600, 1200, @"\\.\SMALL", NoteSizePreset.Custom);

        var result = NotePlacement.ClampToVisibleArea(geometry, [small]);

        Assert.True(result.Width <= small.Width);
        Assert.True(result.Height <= small.Height);
    }

    [Fact]
    public void A_note_is_never_shrunk_below_the_usable_minimum()
    {
        var geometry = new NoteGeometry(10, 10, 1, 1, @"\\.\DISPLAY1", NoteSizePreset.Custom);

        var result = NotePlacement.ClampToVisibleArea(geometry, [Primary]);

        Assert.Equal(NoteGeometry.MinWidth, result.Width);
        Assert.Equal(NoteGeometry.MinHeight, result.Height);
    }

    /// <summary>Deliberate edge placements should survive, so long as enough remains grabbable.</summary>
    [Fact]
    public void A_note_may_hang_off_the_right_edge_but_stays_grabbable()
    {
        var geometry = new NoteGeometry(1850, 500, 320, 260, @"\\.\DISPLAY1", NoteSizePreset.Medium);

        var result = NotePlacement.ClampToVisibleArea(geometry, [Primary]);

        Assert.True(result.X < Primary.Right - NotePlacement.MinimumVisibleEdge + 1);
        Assert.True(IsReachable(result, Primary));
    }

    [Fact]
    public void A_note_is_never_restored_under_the_taskbar()
    {
        var geometry = new NoteGeometry(400, 1030, 320, 260, @"\\.\DISPLAY1", NoteSizePreset.Medium);

        var result = NotePlacement.ClampToVisibleArea(geometry, [Primary]);

        Assert.True(result.Y <= Primary.Bottom - NotePlacement.MinimumVisibleEdge);
    }

    /// <summary>With no displays reported there is nothing sensible to clamp against.</summary>
    [Fact]
    public void An_empty_display_list_leaves_the_geometry_alone()
    {
        var geometry = new NoteGeometry(4200, 2600, 420, 360, @"\\.\DISPLAY7", NoteSizePreset.Large);

        Assert.Equal(geometry, NotePlacement.ClampToVisibleArea(geometry, []));
    }

    [Fact]
    public void A_new_note_lands_near_the_centre_of_its_display()
    {
        var result = NotePlacement.PlaceNew(NoteSizePreset.Medium, Primary, cascadeIndex: 0);

        Assert.Equal((1920 - 320) / 2, result.X);
        Assert.Equal((1032 - 260) / 2, result.Y);
        Assert.Equal(NoteSizePreset.Medium, result.Preset);
        Assert.Equal(Primary.DeviceName, result.MonitorKey);
    }

    [Fact]
    public void Consecutive_new_notes_cascade_instead_of_stacking()
    {
        var first = NotePlacement.PlaceNew(NoteSizePreset.Medium, Primary, 0);
        var second = NotePlacement.PlaceNew(NoteSizePreset.Medium, Primary, 1);
        var third = NotePlacement.PlaceNew(NoteSizePreset.Medium, Primary, 2);

        Assert.True(second.X > first.X && second.Y > first.Y);
        Assert.True(third.X > second.X && third.Y > second.Y);
    }

    /// <summary>The cascade wraps rather than marching a long session's notes off the screen.</summary>
    [Fact]
    public void The_cascade_wraps_and_stays_on_screen()
    {
        for (var i = 0; i < 40; i++)
        {
            var result = NotePlacement.PlaceNew(NoteSizePreset.Medium, Primary, i);
            Assert.True(IsReachable(result, Primary), $"Note {i} placed off-screen at {result.X},{result.Y}.");
        }
    }

    [Fact]
    public void A_new_note_on_a_secondary_display_stays_on_that_display()
    {
        var result = NotePlacement.PlaceNew(NoteSizePreset.Large, Secondary, 0, [Primary, Secondary]);

        Assert.Equal(Secondary.DeviceName, result.MonitorKey);
        Assert.True(result.X >= Secondary.Left);
    }
}
