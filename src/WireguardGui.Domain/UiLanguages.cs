namespace WireguardGui.Domain;

public static class UiLanguages
{
    public const string Default = English;

    public const string English = "en";
    public const string Russian = "ru";
    public const string French = "fr";
    public const string German = "de";
    public const string Spanish = "es";
    public const string Chinese = "zh";
    public const string Japanese = "ja";

    public static readonly IReadOnlyList<string> All =
    [
        English,
        Russian,
        French,
        German,
        Spanish,
        Chinese,
        Japanese,
    ];
}
