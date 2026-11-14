using WireguardGui.App.Avalonia.Localization;
using WireguardGui.Domain;

namespace WireguardGui.App.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void Loads_English_Strings_From_Embedded_Resources()
    {
        var localization = new LocalizationService();

        Assert.Equal("Profiles", localization.Get("Profiles_Title"));
        Assert.Equal("Import", localization.Get("Profiles_Import"));
        Assert.Equal("Settings", localization.Get("Settings_Title"));
        Assert.NotEqual("Profiles_Title", localization.Get("Profiles_Title"));
    }

    [Fact]
    public void SetLanguage_Switches_To_Russian_And_Back()
    {
        var localization = new LocalizationService();

        localization.SetLanguage(UiLanguages.Russian);
        Assert.Equal("Профили", localization.Get("Profiles_Title"));
        Assert.Equal("Импорт", localization.Get("Profiles_Import"));

        localization.SetLanguage(UiLanguages.English);
        Assert.Equal("Profiles", localization.Get("Profiles_Title"));
        Assert.Equal("Import", localization.Get("Profiles_Import"));
    }

    [Fact]
    public void Format_Uses_Placeholders_Safely()
    {
        var localization = new LocalizationService();

        Assert.Equal(
            "Connected: home",
            localization.Format("Status_Connected", "home"));
    }

    [Theory]
    [InlineData(UiLanguages.English)]
    [InlineData(UiLanguages.Russian)]
    [InlineData(UiLanguages.German)]
    [InlineData(UiLanguages.French)]
    [InlineData(UiLanguages.Spanish)]
    [InlineData(UiLanguages.Japanese)]
    public void AmneziaWg_Keys_Are_Localized(string language)
    {
        var localization = new LocalizationService();
        localization.SetLanguage(language);

        string[] keys =
        [
            "Backend_AmneziaWg",
            "Import_Backend_AmneziaWg",
            "Import_AmneziaWg_Unavailable",
            "Import_AmneziaWg_Required",
            "Profiles_Setup_AmneziaWg_Fedora",
            "Profiles_Setup_AmneziaWg_Debian",
            "Settings_Tab_General",
            "Settings_Tab_Amnesia",
            "AmneziaWg_Title",
            "AmneziaWg_Hint",
            "AmneziaWg_Available",
            "AmneziaWg_NotInstalled",
            "AmneziaWg_Missing",
            "AmneziaWg_Version",
            "AmneziaWg_VersionUnknown",
            "AmneziaWg_Transport",
            "AmneziaWg_Transport_Kernel",
            "AmneziaWg_Transport_Userspace",
            "AmneziaWg_Transport_None",
            "AmneziaWg_Check",
            "AmneziaWg_Install",
            "AmneziaWg_ManualInstall",
            "AmneziaWg_Install_Title",
            "AmneziaWg_Install_Starting",
            "AmneziaWg_Install_Step",
            "AmneziaWg_Install_Done",
            "AmneziaWg_Install_Failed",
            "AmneziaWg_Install_NeedsRoot",
            "AmneziaWg_Install_Close",
            "AmneziaWg_Manual_Title",
            "AmneziaWg_Manual_Intro",
            "AmneziaWg_Manual_Copy",
            "AmneziaWg_Manual_Copied",
            "AmneziaWg_Manual_Debian",
            "AmneziaWg_Manual_Fedora",
            "AmneziaWg_Manual_Alt",
            "AmneziaWg_Manual_Source",
        ];

        foreach (var key in keys)
            Assert.NotEqual(key, localization.Get(key));
    }
}
