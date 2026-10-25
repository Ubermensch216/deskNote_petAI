using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Xaml;

namespace DeskNote.App.Views;

/// <summary>The pet's speech bubble and the idle chatter that fills it.</summary>
public sealed partial class DesktopPetWindow
{
    private const double SleepSpeechFraction = 0.80;
    private const double SpeechBubbleGap = 2;
    private const double SpeechBubbleReserve = 58;
    private const int IdleChatterMinimumSeconds = 45;
    private const int IdleChatterMaximumSeconds = 105;

    /// <summary>
    /// What the pet says to itself when nothing has happened.
    /// </summary>
    /// <remarks>
    /// Small, self-absorbed thoughts rather than prompts: the pet is not a reminder that has
    /// learned to talk, and a line that asks the user for something every time it opens its mouth
    /// stops being company and becomes another notification. The corpus is per species and per
    /// language, and it is dealt from a shuffled deck rather than drawn at random - three hundred
    /// lines picked independently repeat inside an afternoon, and a repeat is what makes a large
    /// corpus feel like a small one.
    /// </remarks>
    private CompanionChatterBag? _chatterBag;

    /// <summary>The species and language the loaded corpus belongs to.</summary>
    private (CompanionPetKind Pet, string Locale) _chatterSource;

    private void Say(string key) => SayLine(Strings.Get(key));

    /// <summary>
    /// Puts one already-written line in the bubble.
    /// </summary>
    /// <remarks>
    /// The care and mood lines come from the UI resources by key, while the idle musings come from
    /// the chatter corpus as finished text. Both end here so the bubble timing and the idle reset
    /// are written once - the pet should not have two ways of opening its mouth.
    /// </remarks>
    private void SayLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            ScheduleIdleChatter();
            return;
        }

        // The bubble rests on the head of an upright pet; on a wall or the ceiling the head is
        // elsewhere, so the line is dropped there rather than shown floating.
        if (_motion.IsClimbing)
        {
            ScheduleIdleChatter();
            return;
        }

        SpeechText.Text = line;
        SpeechBubble.Visibility = Visibility.Visible;
        UpdateOrnamentVisibility();
        _bubbleTimer.Stop();
        _bubbleTimer.Start();

        // Anything the pet was told to say resets the idle clock, so a musing never lands on top
        // of news the user actually asked for.
        ScheduleIdleChatter();
    }

    /// <summary>
    /// Rests the bubble on the pet's head.
    /// </summary>
    /// <remarks>
    /// The window is a fixed 250px tall; the pet inside it is anywhere from about 40px to the full
    /// 200, once the size setting and the growth silhouette are applied. A bubble pinned to the top
    /// of the window therefore floated further above the animal the smaller it was — most of a
    /// hand's width away from a small, young pet, which reads as words belonging to nothing.
    /// Measuring from the drawn height instead keeps the two together at every size; the cap stops
    /// a full-grown pet from pushing the bubble out through the top of the window.
    /// </remarks>
    private void PositionSpeechBubble() =>
        SpeechBubble.Margin = new Thickness(
            0,
            0,
            0,
            Math.Min(
                (_frameHeight * _growthScale * SpeechAnchorFraction()) + SpeechBubbleGap,
                WindowHeight - SpeechBubbleReserve));

    /// <summary>How much of the frame the pet currently fills, from the floor up.</summary>
    private double SpeechAnchorFraction() =>
        IsRig ? RigHeadTopFraction(_animation == PetAnimation.Sleep) + 0.02
        : _animation is PetAnimation.Sleep or PetAnimation.Stretch ? SleepSpeechFraction : 1;

    /// <summary>
    /// Waits a while, then lets the pet think out loud.
    /// </summary>
    /// <remarks>
    /// The gap is randomised because a fixed one turns into a metronome: the second time a bubble
    /// appears exactly a minute after the first, it stops being a pet having a thought and becomes
    /// a timer firing. Quiet hours and the proactive switch apply — this is the pet speaking
    /// first, which is exactly what those settings are about.
    /// </remarks>
    private void ScheduleIdleChatter()
    {
        _idleChatterTimer.Stop();
        _idleChatterTimer.Interval = TimeSpan.FromSeconds(
            _chatter.Next(IdleChatterMinimumSeconds, IdleChatterMaximumSeconds + 1));
        _idleChatterTimer.Start();
    }

    private void OnIdleChatterTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        sender.Stop();

        if (!_settings.AllowsProactiveAt(DateTimeOffset.Now)
            || SpeechBubble.Visibility == Visibility.Visible
            || _pointerDown)
        {
            ScheduleIdleChatter();
            return;
        }

        SayLine(Chatter().Next());
    }

    /// <summary>
    /// Loads the corpus for the species and language now in force, if either has changed.
    /// </summary>
    /// <remarks>
    /// Both can change while the window is alive - the user picks another species in settings, or
    /// switches the UI language - and a pet that went on musing in the language it was born in
    /// would be the most visible part of the app to miss the switch.
    /// </remarks>
    private CompanionChatterBag Chatter()
    {
        var source = (Pet: _settings.SelectedPet, Locale: Strings.ActiveLocale);

        if (_chatterBag is null || _chatterSource != source)
        {
            _chatterSource = source;
            _chatterBag = new CompanionChatterBag(
                CompanionChatterCatalog.Lines(source.Pet, source.Locale));
        }

        return _chatterBag;
    }
}
