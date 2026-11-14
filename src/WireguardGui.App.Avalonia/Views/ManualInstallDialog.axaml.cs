using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using WireguardGui.App.Avalonia.Controls;
using WireguardGui.App.Avalonia.Localization;

namespace WireguardGui.App.Avalonia.Views;

public partial class ManualInstallDialog : Window
{
    private static readonly string[] Instructions =
    [
        """
        sudo apt-get update
        sudo apt-get install -y software-properties-common gnupg2
        sudo add-apt-repository -y ppa:amnezia/ppa
        sudo apt-get update
        sudo apt-get install -y amneziawg amneziawg-tools
        """,
        """
        sudo dnf install -y git make gcc golang

        rm -rf /tmp/amneziawg-build && mkdir -p /tmp/amneziawg-build
        git clone --depth 1 https://github.com/amnezia-vpn/amneziawg-tools.git /tmp/amneziawg-build/tools
        cd /tmp/amneziawg-build/tools/src && make && sudo make install

        git clone --depth 1 https://github.com/amnezia-vpn/amneziawg-go.git /tmp/amneziawg-build/go
        cd /tmp/amneziawg-build/go && make && sudo install -m 0755 amneziawg-go /usr/bin/amneziawg-go
        """,
        """
        sudo dnf install -y dnf-plugins-core epel-release
        sudo dnf copr enable amneziavpn/amneziawg
        sudo dnf install -y amneziawg-dkms amneziawg-tools
        """,
        """
        sudo apt-get update
        sudo apt-get install -y amneziawg amneziawg-tools
        """,
        """
        sudo apt-get install -y git make dkms linux-headers-$(uname -r)

        git clone https://github.com/amnezia-vpn/amneziawg-tools.git
        cd amneziawg-tools/src && make && sudo make install

        git clone https://github.com/amnezia-vpn/amneziawg-linux-kernel-module.git
        cd amneziawg-linux-kernel-module && make && sudo make dkms-install
        """,
    ];

    private readonly AppButton[] _chips;

    public ManualInstallDialog(LocalizationService localization)
    {
        InitializeComponent();

        Title = localization.Get("AmneziaWg_Manual_Title");
        DialogTitle.Text = localization.Get("AmneziaWg_Manual_Title");
        IntroText.Text = localization.Get("AmneziaWg_Manual_Intro");
        CopyButton.Content = localization.Get("AmneziaWg_Manual_Copy");
        CloseButton.Content = localization.Get("AmneziaWg_Install_Close");
        CopiedText.Text = localization.Get("AmneziaWg_Manual_Copied");

        DebianChip.Content = localization.Get("AmneziaWg_Manual_Debian");
        FedoraChip.Content = localization.Get("AmneziaWg_Manual_Fedora");
        RhelChip.Content = localization.Get("AmneziaWg_Manual_Rhel");
        AltChip.Content = localization.Get("AmneziaWg_Manual_Alt");
        SourceChip.Content = localization.Get("AmneziaWg_Manual_Source");

        _chips = [DebianChip, FedoraChip, RhelChip, AltChip, SourceChip];
        Select(0);
    }

    private void OnSelectClick(object? sender, RoutedEventArgs e)
    {
        if (sender is AppButton { Tag: string tag } && int.TryParse(tag, out var index))
            Select(index);
    }

    private void Select(int index)
    {
        CommandText.Text = Instructions[index].Trim();
        CopiedText.IsVisible = false;

        for (var i = 0; i < _chips.Length; i++)
            _chips[i].Classes.Set("selected", i == index);
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(CommandText.Text ?? string.Empty);
        CopiedText.IsVisible = true;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
