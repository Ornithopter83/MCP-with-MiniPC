namespace ProjectHub.Worker;

/// <summary>A per-job, one-use permit created only from the user's explicit Run-time checkbox snapshot.</summary>
public sealed class JobHighLevelPermit
{
    private int _remaining;

    public JobHighLevelPermit(bool authorizedAtLaunch) => _remaining = authorizedAtLaunch ? 1 : 0;
    public bool IsAvailable => _remaining == 1;
    public bool TryConsume()
    {
        if (_remaining != 1) return false;
        _remaining = 0;
        return true;
    }
}
