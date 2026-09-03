namespace WireguardGui.Application.Abstractions;

public interface ISystemResumeWatcher
{
    void Start(Func<CancellationToken, Task> onResumed);
}
