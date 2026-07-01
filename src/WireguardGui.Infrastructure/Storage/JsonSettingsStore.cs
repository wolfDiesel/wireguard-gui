using System.Text.Json;
using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.Storage;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public JsonSettingsStore()
        : this(Path.Combine(JsonProfileStore.GetDefaultDataRoot(), "settings.json"))
    {
    }

    public JsonSettingsStore(string filePath) => _filePath = filePath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return new AppSettings(UiSettings.CreateDefault());

        await using var stream = File.OpenRead(_filePath);
        var file = await JsonSerializer.DeserializeAsync<SettingsFile>(stream, JsonOptions, cancellationToken);
        return file?.ToDomain() ?? new AppSettings(UiSettings.CreateDefault());
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, SettingsFile.FromDomain(settings), JsonOptions, cancellationToken);
    }

    private sealed class SettingsFile
    {
        public UiFile? Ui { get; set; }

        public AppSettings ToDomain() =>
            new(Ui?.ToDomain() ?? UiSettings.CreateDefault());

        public static SettingsFile FromDomain(AppSettings settings) =>
            new() { Ui = UiFile.FromDomain(settings.Ui) };
    }

    private sealed class UiFile
    {
        public int WindowWidth { get; set; } = 960;
        public int WindowHeight { get; set; } = 640;
        public string? ColorScheme { get; set; }
        public string? Appearance { get; set; }
        public string? Language { get; set; }
        public bool TrayEnabled { get; set; } = true;
        public bool MinimizeToTray { get; set; }
        public bool CloseToTray { get; set; } = true;

        public UiSettings ToDomain() =>
            new(
                WindowWidth,
                WindowHeight,
                NormalizeColorScheme(ColorScheme),
                NormalizeAppearance(Appearance),
                NormalizeLanguage(Language),
                TrayEnabled,
                MinimizeToTray,
                CloseToTray);

        public static UiFile FromDomain(UiSettings ui) =>
            new()
            {
                WindowWidth = ui.WindowWidth,
                WindowHeight = ui.WindowHeight,
                ColorScheme = ui.ColorScheme,
                Appearance = ui.Appearance,
                Language = ui.Language,
                TrayEnabled = ui.TrayEnabled,
                MinimizeToTray = ui.MinimizeToTray,
                CloseToTray = ui.CloseToTray,
            };
    }

    private static string NormalizeLanguage(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UiLanguages.All.Contains(value)
            ? value
            : UiLanguages.Default;

    private static string NormalizeColorScheme(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UiColorSchemes.All.Contains(value)
            ? value
            : UiColorSchemes.Default;

    private static string NormalizeAppearance(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UiAppearances.All.Contains(value)
            ? value
            : UiAppearances.Default;
}
