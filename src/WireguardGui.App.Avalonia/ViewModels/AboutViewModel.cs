using WireguardGui.App.Avalonia.Localization;
using WireguardGui.App.Avalonia.Services;

namespace WireguardGui.App.Avalonia.ViewModels;

internal sealed partial class AboutViewModel : LocalizedViewModelBase
{
    private readonly AppVersionService _appVersion;

    public AboutViewModel(AppVersionService appVersion, LocalizationService localization)
        : base(localization)
    {
        _appVersion = appVersion;
    }

    public string Title => T("About_Title");
    public string AppName => T("App_Title");
    public string VersionLabel => IsDevVersion
        ? T("About_Version_Dev")
        : Tf("About_Version", _appVersion.Version);

    private bool IsDevVersion => string.Equals(_appVersion.Version, "dev", StringComparison.Ordinal);

    protected override void OnLocalizationChanged() =>
        NotifyLocalized(nameof(Title), nameof(AppName), nameof(VersionLabel));
}