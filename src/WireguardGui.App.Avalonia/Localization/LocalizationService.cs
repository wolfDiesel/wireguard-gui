using System.Reflection;
using System.Text.Json;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.Localization;

public sealed class LocalizationService
{
    private static readonly Assembly Assembly = typeof(LocalizationService).Assembly;
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private string _language = UiLanguages.Default;

    public event EventHandler? Changed;

    public string Language => _language;

    public void SetLanguage(string language)
    {
        var normalized = UiLanguages.All.Contains(language) ? language : UiLanguages.Default;
        if (string.Equals(_language, normalized, StringComparison.Ordinal))
            return;

        _language = normalized;
        LoadStrings(_language);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key) =>
        _strings.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params object[] args) =>
        string.Format(Get(key), args);

    public string GetLanguageLabel(string languageCode) =>
        Get($"Lang_{languageCode}");

    private void LoadStrings(string language)
    {
        _strings.Clear();
        Merge(LoadResource(UiLanguages.Default));
        if (!string.Equals(language, UiLanguages.Default, StringComparison.Ordinal))
            Merge(LoadResource(language));
    }

    private void Merge(IReadOnlyDictionary<string, string> source)
    {
        foreach (var (key, value) in source)
            _strings[key] = value;
    }

    private static IReadOnlyDictionary<string, string> LoadResource(string language)
    {
        var suffix = $".Localization.{language}.json";
        var name = Assembly.GetManifestResourceNames()
            .FirstOrDefault(resource => resource.EndsWith(suffix, StringComparison.Ordinal));
        if (name is null)
            return new Dictionary<string, string>();

        using var stream = Assembly.GetManifestResourceStream(name);
        if (stream is null)
            return new Dictionary<string, string>();

        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            result[property.Name] = property.Value.GetString() ?? property.Name;
        return result;
    }
}
