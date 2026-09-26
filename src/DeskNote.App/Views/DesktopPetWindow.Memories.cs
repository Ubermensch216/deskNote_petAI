using DeskNote.App.Services;
using DeskNote.Companion.Core;

namespace DeskNote.App.Views;

public sealed partial class DesktopPetWindow
{
    private IReadOnlyList<string> _unlocks = [];
    private bool _hasMemorySnapshot;
    private long? _lastMemoryId;

    private bool CelebrateNewStage(CompanionSnapshot snapshot)
    {
        var memory = snapshot.LatestMemory;
        var isNew = _hasMemorySnapshot && memory is not null && memory.Id != _lastMemoryId;
        _hasMemorySnapshot = true;
        _lastMemoryId = memory?.Id;
        if (!isNew || memory is null || !memory.TemplateId.StartsWith("Stage", StringComparison.Ordinal)
            || memory.Pet != CompanionPetCatalog.FromAssetKey(snapshot.Profile.AppearanceKey)
            || !_settings.AllowsProactiveAt(DateTimeOffset.Now)
            || !PresentationModeDetector.AllowsUserNotification())
        {
            return false;
        }

        var unlock = CompanionUnlockCatalog.All.FirstOrDefault(item => $"Stage{item.Stage}" == memory.TemplateId);
        if (unlock is null)
        {
            return false;
        }

        PlayUnlockedReaction(unlock.Key);
        return true;
    }

    /// <summary>Explicitly requested, cosmetic-only reactions; no XP or care is recorded.</summary>
    public void PlayUnlockedReaction(string key)
    {
        if (!_unlocks.Contains(key) || !PresentationModeDetector.AllowsUserNotification())
        {
            return;
        }

        var action = key switch
        {
            "SpecialCare" => CompanionCareAction.SpecialCare,
            "Rest" => CompanionCareAction.Rest,
            _ => CompanionCareAction.Greeting,
        };
        PlayCareReaction(new CareRequest(action));
        var lineKey = key == "SpecialCare"
            ? $"Companion_UnlockedSpecial{_settings.SelectedPet}"
            : $"Companion_Unlocked{key}";
        Say(lineKey);
        if (key == "Celebration")
        {
            BeginRest(_clock.Elapsed, seconds: CarePropSeconds, pose: PetRestPose.Stretch);
        }
        else if (key == "Rest")
        {
            BeginRest(_clock.Elapsed, seconds: CarePropSeconds * 2, pose: PetRestPose.Daydream);
        }
    }
}
