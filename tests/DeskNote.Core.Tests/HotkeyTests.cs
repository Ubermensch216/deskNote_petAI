using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+N", HotkeyModifiers.Control | HotkeyModifiers.Alt, "N")]
    [InlineData("Ctrl+Shift+F", HotkeyModifiers.Control | HotkeyModifiers.Shift, "F")]
    [InlineData("Win+Space", HotkeyModifiers.Windows, "Space")]
    [InlineData("alt+f4", HotkeyModifiers.Alt, "F4")]
    [InlineData("  Ctrl + Shift + P  ", HotkeyModifiers.Control | HotkeyModifiers.Shift, "P")]
    public void Gestures_parse(string text, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
    }

    /// <summary>
    /// A global hotkey with no modifier would take that key away from every other application, so
    /// it is rejected rather than registered.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+N+M")]
    public void Unusable_gestures_are_rejected(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    /// <summary>
    /// Key names are canonicalized, or a stored binding stops matching the one on screen and two
    /// commands on the same physical key are not seen as conflicting.
    /// </summary>
    [Theory]
    [InlineData("Ctrl+f4", "Ctrl+F4")]
    [InlineData("Ctrl+F4", "Ctrl+F4")]
    [InlineData("Ctrl+SPACE", "Ctrl+Space")]
    [InlineData("Ctrl+space", "Ctrl+Space")]
    [InlineData("Ctrl+n", "Ctrl+N")]
    public void Key_names_are_canonicalized(string text, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(expected, gesture.ToString());
    }

    [Fact]
    public void Differently_spelled_gestures_compare_equal()
    {
        HotkeyGesture.TryParse("Ctrl+f4", out var lower);
        HotkeyGesture.TryParse("CTRL+F4", out var upper);

        Assert.Equal(lower, upper);
    }

    [Fact]
    public void A_gesture_round_trips_through_its_text_form()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+N", out var gesture));

        Assert.Equal("Ctrl+Alt+N", gesture.ToString());
        Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var again));
        Assert.Equal(gesture, again);
    }

    /// <summary>Modifier order is normalized so the same gesture always reads and stores identically.</summary>
    [Fact]
    public void Modifier_order_is_normalized()
    {
        HotkeyGesture.TryParse("Shift+Ctrl+Alt+K", out var a);
        HotkeyGesture.TryParse("Alt+Shift+Ctrl+K", out var b);

        Assert.Equal(a, b);
        Assert.Equal("Ctrl+Alt+Shift+K", a.ToString());
    }

    /// <summary>The defaults report p5 specifies.</summary>
    [Theory]
    [InlineData(HotkeyCommands.NewNote, "Ctrl+Alt+N")]
    [InlineData(HotkeyCommands.SearchNotes, "Ctrl+Shift+F")]
    [InlineData(HotkeyCommands.ToggleAlwaysOnTop, "Ctrl+Shift+P")]
    [InlineData(HotkeyCommands.AddReminder, "Ctrl+Shift+R")]
    public void The_default_bindings_match_the_specification(string command, string expected)
    {
        Assert.Equal(expected, HotkeyBindings.Defaults()[command].ToString());
    }

    /// <summary>
    /// Ctrl+Space belongs to the IME on a Korean desktop. An app with no model has no business
    /// taking it, so the palette stays unbound until local AI is switched on.
    /// </summary>
    [Fact]
    public void The_ai_palette_is_unbound_while_ai_is_off() =>
        Assert.False(HotkeyBindings.Defaults().TryGet(HotkeyCommands.AiPalette, out _));

    [Fact]
    public void The_ai_palette_takes_control_space_once_ai_is_on()
    {
        Assert.True(HotkeyBindings.Defaults(includeAiPalette: true).TryGet(HotkeyCommands.AiPalette, out var gesture));
        Assert.Equal("Ctrl+Space", gesture.ToString());
    }

    [Fact]
    public void Every_known_command_has_a_default()
    {
        var defaults = HotkeyBindings.Defaults(includeAiPalette: true);

        Assert.All(HotkeyCommands.All, command => Assert.True(defaults.TryGet(command, out _)));
    }

    [Fact]
    public void Rebinding_replaces_only_that_command()
    {
        HotkeyGesture.TryParse("Ctrl+Alt+M", out var replacement);

        var rebound = HotkeyBindings.Defaults().With(HotkeyCommands.NewNote, replacement);

        Assert.Equal("Ctrl+Alt+M", rebound[HotkeyCommands.NewNote].ToString());
        Assert.Equal("Ctrl+Shift+F", rebound[HotkeyCommands.SearchNotes].ToString());
    }

    [Fact]
    public void Rebinding_to_an_unusable_gesture_is_ignored()
    {
        var rebound = HotkeyBindings.Defaults().With(HotkeyCommands.NewNote, new HotkeyGesture(HotkeyModifiers.None, "N"));

        Assert.Equal("Ctrl+Alt+N", rebound[HotkeyCommands.NewNote].ToString());
    }

    /// <summary>
    /// Two commands on one combination means the second silently never fires, so it is reported
    /// rather than quietly resolved.
    /// </summary>
    [Fact]
    public void Two_commands_on_one_gesture_are_reported_as_a_conflict()
    {
        HotkeyGesture.TryParse("Ctrl+Shift+F", out var searchGesture);

        var clashing = HotkeyBindings.Defaults().With(HotkeyCommands.NewNote, searchGesture);
        var conflicts = clashing.FindConflicts();

        Assert.Contains(HotkeyCommands.NewNote, conflicts);
        Assert.Contains(HotkeyCommands.SearchNotes, conflicts);
    }

    [Fact]
    public void The_defaults_do_not_conflict()
    {
        Assert.Empty(HotkeyBindings.Defaults().FindConflicts());
    }

    [Fact]
    public void Bindings_round_trip_through_settings()
    {
        HotkeyGesture.TryParse("Ctrl+Alt+M", out var replacement);
        var original = HotkeyBindings.Defaults().With(HotkeyCommands.NewNote, replacement);

        var restored = HotkeyBindings.FromJson(original.ToJson());

        Assert.Equal("Ctrl+Alt+M", restored[HotkeyCommands.NewNote].ToString());
        Assert.Equal("Ctrl+Shift+R", restored[HotkeyCommands.AddReminder].ToString());
    }

    /// <summary>
    /// Settings written by a newer version, or corrupted, must never leave the user without a way
    /// to create a note.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("""{"note.new":"garbage+"}""")]
    [InlineData("""{"unknown.command":"Ctrl+Q"}""")]
    public void Unreadable_settings_fall_back_to_the_defaults(string? json)
    {
        var bindings = HotkeyBindings.FromJson(json);

        Assert.Equal("Ctrl+Alt+N", bindings[HotkeyCommands.NewNote].ToString());
        Assert.Empty(bindings.FindConflicts());
    }

    [Fact]
    public void A_partially_stored_set_keeps_defaults_for_the_rest()
    {
        var bindings = HotkeyBindings.FromJson("""{"note.new":"Ctrl+Alt+J"}""");

        Assert.Equal("Ctrl+Alt+J", bindings[HotkeyCommands.NewNote].ToString());
        Assert.Equal("Ctrl+Shift+F", bindings[HotkeyCommands.SearchNotes].ToString());
    }
}
