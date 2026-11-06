using Microsoft.UI.Xaml;
using Workbench.Core;
using Workbench.WinUI.Sdk;

namespace Workbench.App;

/// <summary>Owns the single-writer store lease for the whole manager lifetime.</summary>
internal sealed class HostRuntime : IAsyncDisposable
{
    private readonly FileStream _lease;
    public PluginManager Manager { get; }

    public HostRuntime(RunOptions options)
    {
        RejectLinks(options.UserRoot);
        Directory.CreateDirectory(options.UserRoot);
        var lockPath = Path.Combine(options.UserRoot, "host.lock");
        RejectLinks(lockPath);
        try { _lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new IOException("此用户目录已由另一个工作台实例占用，或无法锁定。请关闭该实例后重试。", error); }
        try
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "Plugins");
            if (options.SmokeTest)
            {
                // Curate a new root: never enumerate installed plugins or read normal user settings.
                var isolated = Path.Combine(options.UserRoot, "bundled");
                foreach (var id in RunOptions.SampleIds)
                    CopySample(Path.Combine(bundled, id), Path.Combine(isolated, id));
                bundled = isolated;
            }
            var projectionNames = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
                .Select(Path.GetFileNameWithoutExtension).OfType<string>()
                .Where(name => name.StartsWith("Microsoft.Windows.", StringComparison.Ordinal) && name.EndsWith(".Projection", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.WindowsAppRuntime.", StringComparison.Ordinal))
                .Append("Microsoft.InteractiveExperiences.Projection").Distinct().ToArray();
            Manager = new PluginManager(new PluginManagerOptions
            {
                PluginsRoot = Path.Combine(options.UserRoot, "Plugins"),
                UserRoot = options.UserRoot,
                AdditionalDiscoveryRoots = [bundled],
                SharedAssemblies = [typeof(WinUiView).Assembly, typeof(FrameworkElement).Assembly,
                    typeof(WinRT.ComWrappersSupport).Assembly, typeof(Windows.Foundation.IAsyncAction).Assembly],
                SharedAssemblyNames = projectionNames
            });
        }
        catch { _lease.Dispose(); throw; }
    }

    private static void CopySample(string source, string destination)
    {
        RejectLinks(source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Bundled smoke package is missing: {source}");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            RejectLinks(file);
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        if (Directory.EnumerateDirectories(source).Any())
            throw new IOException("Bundled smoke samples must be flat code-only packages.");
    }

    private static void RejectLinks(string path)
    {
        for (var part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Reparse points are not allowed for the user store or smoke packages: {part}");
    }

    public async ValueTask DisposeAsync()
    {
        try { await Manager.DisposeAsync(); }
        finally { _lease.Dispose(); }
    }
}
