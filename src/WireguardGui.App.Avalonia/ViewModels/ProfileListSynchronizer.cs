using System.Collections.ObjectModel;
using WireguardGui.App.Avalonia.ViewModels;
using WireguardGui.Application.Contracts;

namespace WireguardGui.App.Avalonia.ViewModels;

internal static class ProfileListSynchronizer
{
    public static void Reconcile(
        ObservableCollection<ProfileRowViewModel> profiles,
        IReadOnlyList<ProfileSummaryDto> items,
        ref ProfileRowViewModel? selectedProfile,
        Localization.LocalizationService localization)
    {
        var selectedId = selectedProfile?.Id;

        for (var i = profiles.Count - 1; i >= 0; i--)
        {
            if (items.All(it => it.Id != profiles[i].Id))
            {
                if (profiles[i].Id == selectedId)
                    selectedProfile = null;
                profiles.RemoveAt(i);
            }
        }

        foreach (var item in items)
        {
            var row = profiles.FirstOrDefault(p => p.Id == item.Id);
            if (row is null)
            {
                profiles.Add(new ProfileRowViewModel(
                    localization,
                    item.Id,
                    item.Name,
                    item.ConnectionName,
                    item.Backend,
                    item.State,
                    item.SplitRoutingEnabled));
                continue;
            }

            row.Name = item.Name;
            row.ConnectionName = item.ConnectionName;
            row.Backend = item.Backend;
            row.State = item.State;
            row.SplitRoutingEnabled = item.SplitRoutingEnabled;
        }

        for (var target = 0; target < items.Count; target++)
        {
            var id = items[target].Id;
            var current = -1;
            for (var i = 0; i < profiles.Count; i++)
            {
                if (profiles[i].Id != id)
                    continue;
                current = i;
                break;
            }

            if (current >= 0 && current != target)
                profiles.Move(current, target);
        }

        if (selectedId is not null)
        {
            var row = profiles.FirstOrDefault(p => p.Id == selectedId);
            if (row is not null && !ReferenceEquals(selectedProfile, row))
                selectedProfile = row;
        }
    }
}
