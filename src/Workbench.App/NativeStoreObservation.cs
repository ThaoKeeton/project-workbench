using Workbench.Sdk;

namespace Workbench.App;

// A single admitted read attempt. The caller owns polling and its existing budget.
internal static class NativeStoreObservation
{
    internal readonly record struct Result<T>(bool IsReady, T Value);

    internal static async Task<Result<T>> ReadAsync<T>(Task<T> read)
    {
        try { return new(true, await read.ConfigureAwait(false)); }
        catch (PluginTestingException error) when (error.ErrorCode == "StoreBusy")
        {
            return new(false, default!);
        }
    }
}
