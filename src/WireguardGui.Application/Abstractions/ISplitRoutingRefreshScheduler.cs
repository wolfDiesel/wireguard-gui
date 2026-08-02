namespace WireguardGui.Application.Abstractions;

public interface ISplitRoutingRefreshScheduler
{
    void NotifyProfileConnected(string profileId);

    void NotifyProfileDisconnected(string? profileId = null);

    void Stop();

    IDisposable BeginManualApply();
}
