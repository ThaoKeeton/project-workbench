using Workbench.TestExecutor;

var port = 0;
string? title = null;
foreach (var argument in args)
{
    if (argument.StartsWith("--remote-debugging-port=", StringComparison.Ordinal) && int.TryParse(argument.Split('=', 2)[1], out var parsed))
        port = parsed;
    if (argument.StartsWith("--window-title=", StringComparison.Ordinal))
        title = argument.Split('=', 2)[1];
}
if (port is < 1 or > 65535) return 1;
await using var host = SyntheticCdpHost.StartOn(port, title, Environment.GetEnvironmentVariable("SYNTHETIC_MARKER"));
await Task.Delay(Timeout.Infinite);
return 0;
