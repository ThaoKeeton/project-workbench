using System.Text.Json;

namespace Workbench.App;

/// <summary>Host-caught failures never evaluate exception text or plugin-defined virtual members.</summary>
internal static class HostFailure
{
    internal static (T Theme, string? Error) ReadTheme<T>(Func<T> read, T systemTheme)
    {
        try { return (read(), null); }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return (systemTheme, Describe("ThemeSettingsFailed", error));
        }
    }

    internal static string Describe(string code, Exception error) => $"{code} ({error switch
    {
        OperationCanceledException => "Canceled",
        JsonException => "InvalidData",
        IOException or UnauthorizedAccessException => "Storage",
        _ => "PluginFailure"
    }}).";

    internal static (T? Value, string? Error) CreateView<T>(Func<T?> factory) where T : class
    {
        try
        {
            var value = factory();
            return value is null ? (null, "NativeViewFailed (NullView).") : (value, null);
        }
        catch (Exception error) { return (null, Describe("NativeViewFailed", error)); }
    }

    internal static async Task<string?> RunAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return Describe("HostOperationFailed", error); }
    }
}
