using Avalonia.Controls;
using Avalonia.Interactivity;
using WireguardGui.App.Avalonia.Localization;
using WireguardGui.Application.Contracts;

namespace WireguardGui.App.Avalonia.Views;

public partial class BackendInstallDialog : Window
{
    private readonly LocalizationService _localization;
    private readonly Func<
        IProgress<BackendInstallProgressDto>,
        CancellationToken,
        Task<BackendInstallResultDto>> _runAsync;

    private readonly CancellationTokenSource _cancellation = new();
    private BackendInstallResultDto? _result;

    public BackendInstallDialog(
        LocalizationService localization,
        Func<IProgress<BackendInstallProgressDto>, CancellationToken, Task<BackendInstallResultDto>> runAsync)
    {
        _localization = localization;
        _runAsync = runAsync;

        InitializeComponent();
        Title = _localization.Get("AmneziaWg_Install_Title");
        DialogTitle.Text = _localization.Get("AmneziaWg_Install_Title");
        ManualButton.Content = _localization.Get("AmneziaWg_ManualInstall");
        CloseButton.Content = _localization.Get("AmneziaWg_Install_Close");
        Opened += OnOpened;
        Closed += OnClosed;
    }

    public bool ManualRequested { get; private set; }

    private void OnClosed(object? sender, EventArgs e)
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        StepText.Text = _localization.Get("AmneziaWg_Install_Starting");

        var progress = new Progress<BackendInstallProgressDto>(UpdateProgress);
        try
        {
            _result = await _runAsync(progress, _cancellation.Token);
        }
        catch (Exception ex)
        {
            _result = new BackendInstallResultDto(false, ex.Message);
        }

        ShowResult();
    }

    private void UpdateProgress(BackendInstallProgressDto progress)
    {
        StepText.Text = progress.StepIndex > 0
            ? $"{_localization.Format("AmneziaWg_Install_Step", progress.StepIndex, progress.StepCount)}: {progress.Title}"
            : progress.Title;

        if (!string.IsNullOrWhiteSpace(progress.Output))
            OutputText.Text = progress.Output;
    }

    private void ShowResult()
    {
        Progress.IsVisible = false;
        CloseButton.IsEnabled = true;

        if (_result?.Success == true)
        {
            StatusText.Text = _localization.Get("AmneziaWg_Install_Done");
            return;
        }

        StatusText.Text = _result?.NeedsAdmin == true
            ? _localization.Get("AmneziaWg_Install_NeedsRoot")
            : _localization.Get("AmneziaWg_Install_Failed");

        if (!string.IsNullOrWhiteSpace(_result?.ErrorMessage))
        {
            OutputText.Text = string.IsNullOrWhiteSpace(OutputText.Text)
                ? _result.ErrorMessage
                : $"{OutputText.Text}{Environment.NewLine}{_result.ErrorMessage}";
        }

        ManualButton.IsVisible = true;
    }

    private void OnManualClick(object? sender, RoutedEventArgs e)
    {
        ManualRequested = true;
        Close(null);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(_result);
}
