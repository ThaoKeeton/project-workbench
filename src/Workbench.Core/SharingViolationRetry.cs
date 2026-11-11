namespace Workbench.Core;

/// <summary>
/// Limited retries for a raw Win32 sharing violation on ordinary data reads and writes.
/// Exclusive store leases must not use this. A held host.lock or testing.lock is ownership,
/// and retrying it would turn a refused second opener into a wait for the owner to leave.
/// </summary>
internal static class SharingViolationRetry
{
    internal const int MaxAttempts = 8;
    private const int DelayMilliseconds = 25;

    // ERROR_SHARING_VIOLATION is 32. HRESULT_FROM_WIN32 keeps that code in the low word.
    // ERROR_LOCK_VIOLATION (33) and COR_E_IO from a wrapped lease exception are not included.
    internal static bool IsSharingViolation(Exception error)
        => error is IOException && (error.HResult & 0xFFFF) == 32;

    internal static Task RunAsync(Func<Task> action, CancellationToken cancellationToken)
        => RunAsync(async () => { await action().ConfigureAwait(false); return true; }, cancellationToken);

    internal static Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        => RunAsync(action, cancellationToken, MaxAttempts, DelayMilliseconds);

    internal static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken, int attempts, int delayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempts);
        ArgumentOutOfRangeException.ThrowIfNegative(delayMilliseconds);
        for (var attempt = 1; ; attempt++)
        {
            try { return await action().ConfigureAwait(false); }
            catch (Exception error) when (IsSharingViolation(error) && attempt < attempts)
            {
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
