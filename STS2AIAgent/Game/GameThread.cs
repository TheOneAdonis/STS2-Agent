using System.Threading;
using MegaCrit.Sts2.Core.Logging;

namespace STS2AIAgent.Game;

internal static class GameThread
{
    private const string LogPrefix = "[STS2AIAgent.GameThread]";

    private static readonly object Gate = new();

    private static SynchronizationContext? _syncContext;
    private static int _threadId;

    public static void Initialize()
    {
        lock (Gate)
        {
            _syncContext = SynchronizationContext.Current;
            _threadId = Environment.CurrentManagedThreadId;

            if (_syncContext == null)
            {
                Log.Error($"{LogPrefix} Failed to capture SynchronizationContext.");
                return;
            }

            Log.Info($"{LogPrefix} Captured game thread context on managed thread {_threadId}");
        }
    }

    public static Task<T> InvokeAsync<T>(Func<T> action)
    {
        if (_syncContext == null)
        {
            throw new InvalidOperationException("Game thread context has not been initialized.");
        }

        if (Environment.CurrentManagedThreadId == _threadId)
        {
            return Task.FromResult(action());
        }

        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(_ =>
        {
            try
            {
                completionSource.SetResult(action());
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        }, null);

        return completionSource.Task;
    }

    public static Task<T> InvokeAsync<T>(Func<Task<T>> action)
    {
        if (_syncContext == null)
        {
            throw new InvalidOperationException("Game thread context has not been initialized.");
        }

        if (Environment.CurrentManagedThreadId == _threadId)
        {
            return action();
        }

        var completionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _syncContext.Post(async _ =>
        {
            try
            {
                completionSource.SetResult(await action());
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        }, null);

        return completionSource.Task;
    }
}
