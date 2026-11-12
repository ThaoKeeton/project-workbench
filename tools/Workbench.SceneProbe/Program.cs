using System.Globalization;
using Workbench.TestExecutor;

var port = 0;
var output = Path.Combine(Directory.GetCurrentDirectory(), "scene-probe.json");
var title = (string?)null;
var reload = false;
for (var index = 0; index < args.Length; index++)
{
    var arg = args[index];
    if (arg == "--reload")
    {
        reload = true;
        continue;
    }
    if (index + 1 >= args.Length) return 2;
    var value = args[++index];
    switch (arg)
    {
        case "--port":
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port)) return 2;
            break;
        case "--out":
            output = value;
            break;
        case "--title":
            title = value;
            break;
        default:
            return 2;
    }
}
if (port is < 1 or > 65535) return 2;

var session = await CdpWebSession.ConnectAsync(port, title, null, CancellationToken.None);
if (session is null)
{
    await Console.Error.WriteLineAsync("debug-port-unreachable");
    return 1;
}
await using (session)
{
    await session.InstallSceneHookAsync(reload, CancellationToken.None);
    var report = await session.ProbeAsync(CancellationToken.None);
    var full = Path.GetFullPath(output);
    await File.WriteAllTextAsync(full, report);
    await Console.Out.WriteLineAsync(full);
}
return 0;
