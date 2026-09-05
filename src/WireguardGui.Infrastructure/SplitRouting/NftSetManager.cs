using WireguardGui.Application.Abstractions;
using WireguardGui.Domain;

namespace WireguardGui.Infrastructure.SplitRouting;

public sealed class NftSetManager(IProcessRunner processRunner)
{
    public async Task CleanupBrokenNftMarkPathAsync(VpnProfile profile, CancellationToken cancellationToken)
    {
        var mark = PolicyRoutingCommands.FormatFwMark(PolicyRoutingNaming.FwMark(profile.Id));
        var chain = PolicyRoutingNaming.ChainName(profile.Id);
        var nftTable = PolicyRoutingNaming.NftTable;

        await RunIpPrivilegedIgnoringErrorsAsync(["rule", "flush", "fwmark", mark], cancellationToken)
            .ConfigureAwait(false);
        await RunIpPrivilegedIgnoringErrorsAsync(["-6", "rule", "flush", "fwmark", mark], cancellationToken)
            .ConfigureAwait(false);

        if (!processRunner.IsCommandAvailable("nft"))
            return;

        await RunPrivilegedIgnoringErrorsAsync(
            "nft",
            ["delete", "chain", "inet", nftTable, chain],
            cancellationToken).ConfigureAwait(false);
        await RunPrivilegedIgnoringErrorsAsync(
            "nft",
            ["delete", "set", "inet", nftTable, PolicyRoutingNaming.HostsSetName(profile.Id)],
            cancellationToken).ConfigureAwait(false);
        await RunPrivilegedIgnoringErrorsAsync(
            "nft",
            ["delete", "set", "inet", nftTable, PolicyRoutingNaming.NetsSetName(profile.Id)],
            cancellationToken).ConfigureAwait(false);
        await RunPrivilegedIgnoringErrorsAsync(
            "nft",
            ["delete", "set", "inet", nftTable, PolicyRoutingNaming.Hosts6SetName(profile.Id)],
            cancellationToken).ConfigureAwait(false);
    }

    private Task RunIpPrivilegedIgnoringErrorsAsync(string[] arguments, CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync("ip", arguments, cancellationToken);

    private Task RunPrivilegedIgnoringErrorsAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        processRunner.RunPrivilegedAsync(fileName, arguments, cancellationToken);
}