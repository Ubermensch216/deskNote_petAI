using DeskNote.App.Services;
using DeskNote.Companion.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskNote.App.Views;

public sealed partial class CompanionWindow
{
    private CompanionAlbumWindow? _albumWindow;
    private string? _unlockSignature;

    private void ShowMemories(CompanionSnapshot snapshot)
    {
        AlbumButton.Content = Strings.Get("Companion_AlbumTitle");
        MemoryPreview.Text = snapshot.LatestMemory is { } memory
            ? $"{memory.LocalDate:yyyy-MM-dd} · {Strings.Get($"Companion_Memory{memory.TemplateId}")}"
            : Strings.Get("Companion_AlbumEmpty");
        MissionRewardStatus.Text = Strings.Get(snapshot.MissionRewardGranted
            ? "Companion_MissionRewardGranted" : "Companion_MissionRewardHint");
        UnlockHeader.Text = Strings.Get("Companion_UnlockHeader");
        var signature = $"{Strings.ActiveLocale}:{snapshot.Profile.AppearanceKey}:{string.Join(',', snapshot.Unlocks)}";
        if (_unlockSignature == signature)
        {
            return;
        }

        _unlockSignature = signature;
        UnlockActions.Children.Clear();
        foreach (var unlock in CompanionUnlockCatalog.All)
        {
            var button = new Button
            {
                Content = Strings.Format("Companion_UnlockFormat", unlock.Stage, Strings.Get($"Companion_Unlock{unlock.Key}")),
                IsEnabled = snapshot.Unlocks.Contains(unlock.Key),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) => _playUnlock(unlock.Key);
            UnlockActions.Children.Add(button);
        }
    }

    private void OnAlbumClicked(object sender, RoutedEventArgs e)
    {
        if (_albumWindow is null)
        {
            _albumWindow = new CompanionAlbumWindow(_readMemories);
            _albumWindow.Closed += (_, _) => _albumWindow = null;
        }

        _albumWindow.Activate();
    }
}
