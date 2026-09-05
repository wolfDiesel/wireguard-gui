namespace WireguardGui.App.Avalonia.Services;

public sealed class AppVersionService
{
    private const string ResourceName = "WireguardGui.App.version.txt";

    private readonly Lazy<string> _version = new(ReadVersion);

    public string Version => _version.Value;

    private static string ReadVersion()
    {
        var assembly = typeof(AppVersionService).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            return "dev";

        using var reader = new StreamReader(stream);
        var value = reader.ReadToEnd().Trim();
        return value.Length > 0 ? value : "dev";
    }
}