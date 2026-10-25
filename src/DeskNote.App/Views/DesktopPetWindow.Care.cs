using DeskNote.Companion.Core;
using Microsoft.UI.Xaml;

namespace DeskNote.App.Views;

/// <summary>The prop acted out beside the pet when it is fed, washed, played with, greeted or put to bed.</summary>
public sealed partial class DesktopPetWindow
{
    private const double CarePropSeconds = 2.6;

    private double _carePropElapsed;

    /// <summary>Acts out one paid care action beside the pet.</summary>
    public void PlayCareReaction(CareRequest request)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => PlayCareReaction(request));
            return;
        }

        FeedProp.Visibility = Collapsed(request.Action == CompanionCareAction.Feed);
        CleanProp.Visibility = Collapsed(request.Action == CompanionCareAction.BasicCare);
        SpecialProp.Visibility = Collapsed(request.Action == CompanionCareAction.SpecialCare);
        PlayProp.Visibility = Collapsed(request.Action == CompanionCareAction.Play);
        GreetProp.Visibility = Collapsed(request.Action == CompanionCareAction.Greeting);
        RestProp.Visibility = Collapsed(request.Action == CompanionCareAction.Rest);

        Say($"Companion_CareReaction{request.Action}");

        // A pet clinging to a wall cannot be fed: it comes down first, and the reaction is acted
        // out when it has landed.
        if (_motion.IsClimbing)
        {
            LetGo();
        }

        // Being put to bed is the one care action with a pose of its own; the rest are acted out
        // by the prop beside the pet, and a pet that curled up and slept through being fed would
        // be answering a different request.
        BeginRest(
            _clock.Elapsed,
            seconds: CarePropSeconds,
            pose: request.Action == CompanionCareAction.Rest
                ? PetRestPose.Sleep
                : PetRestPose.Sit);

        ActOutCare(request.Action);
        _carePropElapsed = 0;
        CareProp.Opacity = 1;
        if (_settings.ReduceMotion)
        {
            CarePropLift.Y = 0;
            _carePropTimer.Stop();
            _bubbleTimer.Stop();
            _bubbleTimer.Start();
            return;
        }

        _carePropTimer.Start();
    }

    private void OnCarePropTick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        _carePropElapsed += sender.Interval.TotalSeconds;
        if (_carePropElapsed >= CarePropSeconds)
        {
            sender.Stop();
            CareProp.Opacity = 0;
            CarePropLift.Y = 0;
            return;
        }

        CarePropLift.Y = -6 * Math.Abs(Math.Sin(_carePropElapsed * 4));
        var fadeFrom = CarePropSeconds - 0.5;
        CareProp.Opacity = _carePropElapsed <= fadeFrom
            ? 1
            : 1 - ((_carePropElapsed - fadeFrom) / 0.5);
    }
}
