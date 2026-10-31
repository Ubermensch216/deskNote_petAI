namespace DeskNote.Companion.Core.Tests;

public sealed class RigSignatureTextTests
{
    public static TheoryData<RigCharacter, int, string> EveryMoveInBothLanguages()
    {
        var data = new TheoryData<RigCharacter, int, string>();

        foreach (var character in Enum.GetValues<RigCharacter>())
        {
            for (var index = 0; index < RigSignatures.PerCharacter; index++)
            {
                data.Add(character, index, "ko-KR");
                data.Add(character, index, "en-US");
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryMoveInBothLanguages))]
    public void Every_move_is_named_and_described_in_both_languages(RigCharacter character, int index, string locale)
    {
        var text = RigSignatures.Text(character, index, locale);

        Assert.False(string.IsNullOrWhiteSpace(text.Name), $"{character} #{index} has no name in {locale}.");
        Assert.False(string.IsNullOrWhiteSpace(text.Description), $"{character} #{index} has no description in {locale}.");
    }

    /// <summary>
    /// The shouts are timed to a pose, so a shout that is not inside the move would never be heard
    /// and one at the very start is what keeps the bubble from opening empty.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMoveInBothLanguages))]
    public void Shouts_start_with_the_move_and_end_inside_it(RigCharacter character, int index, string locale)
    {
        var text = RigSignatures.Text(character, index, locale);
        var duration = RigSignatures.Duration(character, index);

        Assert.NotEmpty(text.Shouts);
        Assert.Equal(0, text.Shouts[0].At);
        Assert.All(text.Shouts, shout => Assert.InRange(shout.At, 0, duration));
        Assert.Equal(
            text.Shouts.Select(shout => shout.At).OrderBy(at => at),
            text.Shouts.Select(shout => shout.At));
        Assert.False(string.IsNullOrEmpty(text.ShoutAt(0)), "The move opens in silence.");
    }

    /// <summary>
    /// The bubble is 210px wide at 13px type: the title and the shout each get one line, and a
    /// line that wraps pushes the second one out of a two-line bubble.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMoveInBothLanguages))]
    public void The_bubble_lines_fit_the_bubble(RigCharacter character, int index, string locale)
    {
        var text = RigSignatures.Text(character, index, locale);
        var korean = locale.StartsWith("ko", StringComparison.Ordinal);
        var titleLimit = korean ? 13 : 26;
        var shoutLimit = korean ? 14 : 26;

        Assert.True(text.Name.Length + 4 <= titleLimit + 4 && text.Name.Length <= titleLimit, $"{text.Name} is too long to title the bubble.");
        Assert.All(text.Shouts, shout => Assert.True(shout.Text.Length <= shoutLimit, $"\"{shout.Text}\" will wrap."));
    }

    [Fact]
    public void The_bubble_is_the_name_between_stars_and_then_the_shout()
    {
        var text = RigSignatures.Text(RigCharacter.Goku, 0, "en");

        Assert.Equal("★ Kamehameha ★\nKa…", text.Bubble(0.1));
        Assert.Equal("★ Kamehameha ★\nHAAAA!!", text.Bubble(4));
    }

    [Fact]
    public void A_silent_stretch_leaves_just_the_title()
    {
        var text = RigSignatures.Text(RigCharacter.Goku, 1, "en");

        Assert.Equal("★ Instant Transmission ★", text.Bubble(2.5));
    }

    [Fact]
    public void An_unknown_language_falls_back_to_english()
    {
        Assert.Equal(
            RigSignatures.Text(RigCharacter.Vegeta, 1, "en").Name,
            RigSignatures.Text(RigCharacter.Vegeta, 1, "fr-FR").Name);
    }

    [Fact]
    public void Korean_is_Korean_and_English_is_not()
    {
        Assert.Matches("[가-힣]", RigSignatures.Text(RigCharacter.Goku, 0, "ko-KR").Name);
        Assert.DoesNotMatch("[가-힣]", RigSignatures.Text(RigCharacter.Goku, 0, "en-US").Name);
    }

    [Fact]
    public void No_two_moves_of_a_character_share_a_name()
    {
        foreach (var locale in new[] { "ko", "en" })
        {
            foreach (var character in Enum.GetValues<RigCharacter>())
            {
                var names = Enumerable.Range(0, RigSignatures.PerCharacter)
                    .Select(index => RigSignatures.Text(character, index, locale).Name)
                    .ToList();

                Assert.Equal(names.Count, names.Distinct().Count());
            }
        }
    }

    [Theory]
    [InlineData(SignatureFrequency.Off, 0)]
    [InlineData(SignatureFrequency.Rare, 0.08)]
    [InlineData(SignatureFrequency.Normal, 0.20)]
    [InlineData(SignatureFrequency.Often, 0.38)]
    public void How_often_a_rest_becomes_a_move(SignatureFrequency frequency, double chance)
    {
        Assert.Equal(chance, frequency.Chance());
    }
}

public sealed class RigCharacterCatalogTests
{
    private static readonly CompanionPetKind[] Drawn =
    [
        CompanionPetKind.Goku,
        CompanionPetKind.KidGoku,
        CompanionPetKind.Roshi,
        CompanionPetKind.Vegeta,
        CompanionPetKind.Piccolo,
    ];

    [Fact]
    public void Exactly_the_five_characters_are_drawn()
    {
        var drawn = Enum.GetValues<CompanionPetKind>().Where(RigCharacters.IsRig).ToList();

        Assert.Equal(Drawn.OrderBy(k => k), drawn.OrderBy(k => k));
    }

    [Fact]
    public void Each_character_maps_to_a_distinct_species_and_back()
    {
        foreach (var pet in Drawn)
        {
            Assert.True(RigCharacters.TryGet(pet, out var character));
            Assert.Equal(pet, RigCharacters.PetOf(character));
        }

        Assert.Equal(5, Drawn.Select(pet => { RigCharacters.TryGet(pet, out var c); return c; }).Distinct().Count());
    }

    [Fact]
    public void The_animals_are_not_drawn()
    {
        foreach (var pet in CompanionPetCatalog.All.Except(Drawn))
        {
            Assert.False(RigCharacters.IsRig(pet), $"{pet} should still be a sprite.");
        }
    }

    [Fact]
    public void The_drawn_characters_are_offered_and_found_by_their_asset_key()
    {
        foreach (var pet in Drawn)
        {
            Assert.Contains(pet, CompanionPetCatalog.All);
            Assert.Equal(pet, CompanionPetCatalog.FromAssetKey(pet.AssetKey()));
        }
    }

    /// <summary>
    /// The dragon's condition was written for the six pets that existed. A pet added later, however
    /// many of them, must not move a goal somebody is already working towards.
    /// </summary>
    [Fact]
    public void The_dragon_still_asks_for_the_original_six()
    {
        Assert.Equal(6, CompanionPetCatalog.RequiredForDragon.Count);
        Assert.Empty(CompanionPetCatalog.RequiredForDragon.Intersect(Drawn));
    }

    [Fact]
    public void Every_drawn_character_has_a_box_that_fits_its_frame()
    {
        foreach (var pet in Drawn)
        {
            var ink = PetArtBounds.For(pet);

            Assert.InRange(ink.Left, 0, 1);
            Assert.InRange(ink.Right, 0, 1);
            Assert.True(ink.Left < ink.Right);
            Assert.InRange(ink.Top, 0, 1);
            Assert.InRange(ink.Bottom, 0, 1);
            Assert.True(ink.Top < ink.Bottom);

            // Standing on the foot line, centred.
            Assert.Equal(RigCharacters.FootFraction, ink.Bottom, 0.03);
            Assert.Equal(0.5, (ink.Left + ink.Right) / 2, 0.08);
        }
    }

    [Fact]
    public void The_box_height_is_the_characters_height()
    {
        foreach (var pet in Drawn)
        {
            RigCharacters.TryGet(pet, out var character);
            var ink = PetArtBounds.For(pet);

            // Hair and antennae stand a little above the nominal height.
            Assert.Equal(RigCharacters.HeightFraction(character), ink.Bottom - ink.Top, 0.08);
        }
    }

    [Fact]
    public void Piccolo_is_tallest_the_kid_shortest_and_Roshi_slowest()
    {
        double Height(RigCharacter c) => RigCharacters.HeightFactor(c);
        double Speed(RigCharacter c) => RigCharacters.SpeedFactor(c);
        var all = Enum.GetValues<RigCharacter>();

        Assert.Equal(RigCharacter.Piccolo, all.MaxBy(Height));
        Assert.Equal(RigCharacter.Kid, all.MinBy(Height));
        Assert.Equal(RigCharacter.Roshi, all.MinBy(Speed));
        Assert.Equal(RigCharacter.Kid, all.MaxBy(Speed));
    }

    [Fact]
    public void Only_Vegeta_and_Piccolo_bristle_when_poked()
    {
        var bristling = Enum.GetValues<RigCharacter>().Where(RigCharacters.BristlesWhenPoked).ToList();

        Assert.Equal([RigCharacter.Vegeta, RigCharacter.Piccolo], bristling);
    }

    [Fact]
    public void Every_drawn_character_has_a_special_care_of_its_own()
    {
        var cares = CompanionPetCatalog.All.Select(CompanionSpecialCareCatalog.For).ToList();

        // Eight animals have eight, and the five characters add five more; the only repeat is the
        // default every unlisted species falls back to, and none of these is unlisted.
        foreach (var pet in Drawn)
        {
            Assert.Equal(1, cares.Count(care => care == CompanionSpecialCareCatalog.For(pet)));
        }
    }

    [Fact]
    public void A_newborn_character_starts_about_half_size_like_the_rest()
    {
        foreach (var pet in Drawn)
        {
            var newborn = CompanionGrowthAppearanceCatalog.For(pet, 0).Scale;
            var grown = CompanionGrowthAppearanceCatalog.For(pet, CompanionGrowthAppearanceCatalog.FinalStage).Scale;

            Assert.InRange(newborn, 0.49, 0.53);
            Assert.Equal(1, grown, 6);
        }
    }

    [Fact]
    public void Signature_frequency_survives_being_saved()
    {
        Assert.Equal(SignatureFrequency.Normal, new CompanionSettings().SignatureFrequency);
    }
}
