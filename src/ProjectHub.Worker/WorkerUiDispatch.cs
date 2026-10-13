using System.Windows.Threading;

namespace ProjectHub.Worker;

/// <summary>
/// Execute a WPF-bound collection update on its owning Dispatcher. A direct
/// call runs inline; a background call synchronously preserves the existing
/// persistence/notification order. No events are silently discarded.
/// </summary>
internal static class WorkerUiDispatch
{
    internal static void Invoke(Dispatcher dispatcher, Action action)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(action);
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
