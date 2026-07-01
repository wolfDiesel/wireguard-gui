using WireguardGui.App.Avalonia.Localization;

namespace WireguardGui.App.Avalonia.ViewModels;

internal abstract class LocalizedViewModelBase : ViewModelBase
{
    protected LocalizationService Localization { get; }

    protected LocalizedViewModelBase(LocalizationService localization)
    {
        Localization = localization;
        localization.Changed += OnLocalizationServiceChanged;
    }

    private void OnLocalizationServiceChanged(object? sender, EventArgs e) =>
        OnLocalizationChanged();

    protected virtual void OnLocalizationChanged()
    {
    }

    protected string T(string key) => Localization.Get(key);

    protected string Tf(string key, params object[] args) => Localization.Format(key, args);

    protected void NotifyLocalized(params string[] propertyNames)
    {
        foreach (var name in propertyNames)
            OnPropertyChanged(name);
    }
}
