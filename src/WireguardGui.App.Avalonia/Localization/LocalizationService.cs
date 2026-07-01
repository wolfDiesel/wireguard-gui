using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WireguardGui.Domain;

namespace WireguardGui.App.Avalonia.Localization;

public sealed class LocalizationService
{
    private static readonly Assembly Assembly = typeof(LocalizationService).Assembly;
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private string _language = UiLanguages.Default;
    private Action<Action>? _uiSynchronizer;

    public event EventHandler? Changed;

    public string Language => _language;

    public void SetUiSynchronizer(Action<Action> uiSynchronizer) =>
        _uiSynchronizer = uiSynchronizer;

    public LocalizationService()
    {
        LoadStrings(_language);
    }

    public void SetLanguage(string language)
    {
        var normalized = UiLanguages.All.Contains(language) ? language : UiLanguages.Default;
        if (string.Equals(_language, normalized, StringComparison.Ordinal))
            return;

        _language = normalized;
        LoadStrings(_language);
        RaiseChanged();
    }

    public string Get(string key) =>
        _strings.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params object[] args)
    {
        var template = Get(key);
        if (args.Length == 0)
            return template;

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public string GetLanguageLabel(string languageCode) =>
        Get($"Lang_{languageCode}");

    private void RaiseChanged()
    {
        if (Changed is null)
            return;

        if (_uiSynchronizer is not null)
        {
            _uiSynchronizer(() => Changed?.Invoke(this, EventArgs.Empty));
            return;
        }

        Changed.Invoke(this, EventArgs.Empty);
    }

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
