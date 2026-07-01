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
}
