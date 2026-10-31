using DeskNote.Companion.Core;
using Microsoft.UI.Xaml;

namespace DeskNote.App.Views;

/// <summary>What hangs over the pet's head: the growth mark, the sleep effect, and where each belongs on each kind of pet.</summary>
public sealed partial class DesktopPetWindow
{
    private const double OrnamentMinimumScale = 0.5;
    private const double HeadTopFraction = 0.78;
    private const double RestEffectFraction = 0.68;
    private const double RestEffectSideOffset = 18;
    // The sleeping artwork is the same frame with the animal lying down in it, so everything
    // that hangs off the pet's head has to come down with it. Measured off the generated sheets
    // by assets/companion/build_sleep_sprites.py, which prints the content bounds it produced.
    private const double SleepHeadTopFraction = 0.62;
    private const double SleepRestEffectFraction = 0.54;

    private void ApplyGrowthAppearance(CompanionPetKind pet, int appearanceStage)
    {
        var normalizedStage = Math.Clamp(
            appearanceStage,
            0,
            CompanionGrowthAppearanceCatalog.FinalStage);
        if (_appearancePet == pet && _appearanceStage == normalizedStage)
        {
            return;
        }

        _appearancePet = pet;
        _appearanceStage = normalizedStage;
        var appearance = CompanionGrowthAppearanceCatalog.For(pet, normalizedStage);
        _growthScale = appearance.Scale;
        _growthMark = appearance.Mark;
        ApplyGrowthMark();
        ApplyFacingTransform(_settings.ReduceMotion);
        PositionSpeechBubble();
        PositionOrnaments();
    }

    /// <summary>Hangs the ornament this stage has earned over the pet.</summary>
    private void ApplyGrowthMark()
    {
        SproutMark.Visibility = Collapsed(_growthMark == CompanionGrowthMark.Sprout);
        SparklesMark.Visibility = Collapsed(_growthMark == CompanionGrowthMark.Sparkles);
        TiaraMark.Visibility = Collapsed(_growthMark == CompanionGrowthMark.Tiara);
        CrownMark.Visibility = Collapsed(_growthMark == CompanionGrowthMark.Crown);
        UpdateOrnamentVisibility();
    }

    /// <summary>
    /// Puts both overlays where the pet's head actually is.
    /// </summary>
    /// <remarks>
    /// The sprite sits between roughly a fifth and four fifths of its frame, so the head top is a
    /// fraction of the drawn height rather than the window's. Both layers shrink with the pet but
    /// stop at half scale: a newborn at the small size setting is about thirty pixels tall, and an
    /// ornament that shrank with it in full would be a smudge.
    /// </remarks>
    private void PositionOrnaments()
    {
        var drawnHeight = _frameHeight * _growthScale;
        var scale = Math.Clamp(drawnHeight / BaseFrameHeight, OrnamentMinimumScale, 1);

        GrowthMarkScale.ScaleX = scale;
        GrowthMarkScale.ScaleY = scale;
        GrowthMark.Margin = new Thickness(
            0,
            0,
            0,
            drawnHeight * HeadTopFractionNow());

        RestEffectScale.ScaleX = scale;
        RestEffectScale.ScaleY = scale;

        // The rotor stands on the head, above whatever ornament the pet has earned.
        RotorScale.ScaleX = scale;
        RotorScale.ScaleY = scale;
        RotorProp.Margin = new Thickness(0, 0, 0, (drawnHeight * HeadTopFractionNow()) - 2);

        // A centred child is centred in what is left after its own margins, so twice the wanted
        // offset on the left shifts it by the offset itself. The Zs rise beside the head rather
        // than through the ornament already sitting on top of it.
        RestEffect.Margin = new Thickness(
            2 * RestEffectSideOffset * scale,
            0,
            0,
            drawnHeight * RestEffectFractionNow());
    }

    /// <summary>Where the top of the head is, for the crown to sit on, whichever kind of pet this is.</summary>
    private double HeadTopFractionNow() =>
        IsRig ? RigHeadTopFraction(_animation == PetAnimation.Sleep)
        : _animation is PetAnimation.Sleep or PetAnimation.Stretch ? SleepHeadTopFraction : HeadTopFraction;

    /// <summary>Where the Zs and thought dots start: a little under the top of the head, beside it.</summary>
    private double RestEffectFractionNow() =>
        IsRig ? RigHeadTopFraction(_animation == PetAnimation.Sleep) - 0.07
        : _animation is PetAnimation.Sleep or PetAnimation.Stretch ? SleepRestEffectFraction : RestEffectFraction;

    /// <summary>
    /// Keeps the overlays out of the way of the pet's own words.
    /// </summary>
    /// <remarks>
    /// A small pet's bubble lands in the same band as its ornament, and an opaque bubble with a
    /// crown poking through it looks like a drawing error rather than a pet. The bubble lasts four
    /// seconds; the ornament comes back after it.
    /// </remarks>
    private void UpdateOrnamentVisibility()
    {
        var talking = SpeechBubble.Visibility == Visibility.Visible;

        // The growth mark sits on the head, so it goes where the head is not free: under a rotor,
        // or on a pet whose head is pointing along a wall.
        var headFree = !_motion.IsClimbing && _motion.State != PetMotionState.Falling;
        GrowthMark.Visibility =
            Collapsed(_growthMark != CompanionGrowthMark.None && !talking && headFree);
        RestEffect.Visibility = Collapsed(
            !talking
            && !_settings.ReduceMotion
            && _motion.State == PetMotionState.Resting
            && _motion.RestPose is PetRestPose.Sleep or PetRestPose.Daydream);
    }

    /// <summary>Gives the ornament a slow bob, the stage-three pair its twinkle, and the tiara its gem shimmer.</summary>
    private void AnimateGrowthMark(TimeSpan now)
    {
        if (_growthMark == CompanionGrowthMark.None)
        {
            return;
        }

        var seconds = now.TotalSeconds;
        GrowthMarkLift.Y = -2.5 * Math.Sin(seconds * 1.9);

        if (_growthMark == CompanionGrowthMark.Sparkles)
        {
            SparkleLeft.Opacity = 0.55 + (0.45 * Math.Sin(seconds * 2.6));
            SparkleRight.Opacity = 0.55 + (0.45 * Math.Sin((seconds * 2.6) + Math.PI));
        }
        else if (_growthMark == CompanionGrowthMark.Tiara)
        {
            TiaraGemShimmer.Opacity = 0.45 + (0.55 * Math.Sin(seconds * 2.4));
        }
    }
}
