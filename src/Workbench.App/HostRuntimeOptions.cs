using Microsoft.UI.Xaml;
using Workbench.Core;
using Workbench.Core.Projects;
using Workbench.WinUI.Sdk;

namespace Workbench.App;

internal static class HostRuntimeOptions
{
    public static PluginManagerOptions Create(RunOptions options, ProjectRuntimeScope? projectScope = null)
    {
        var userRoot = projectScope?.UserRoot ?? options.UserRoot;
        var bundled = Path.Combine(AppContext.BaseDirectory, "Plugins");
        if (options.SmokeTest || options.WorkspaceProbe is not null)
        {
            // Curate a new root: never enumerate installed plugins or read normal user settings.
            var isolated = Path.Combine(userRoot, "bundled");
            foreach (var id in RunOptions.SampleIds)
                CopySample(Path.Combine(bundled, id), Path.Combine(isolated, id));
            bundled = isolated;
        }
        var projectionNames = Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll")
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()
            .Where(name => name.StartsWith("Microsoft.Windows.", StringComparison.Ordinal) && name.EndsWith(".Projection", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.WindowsAppRuntime.", StringComparison.Ordinal))
            .Append("Microsoft.InteractiveExperiences.Projection").Distinct().ToArray();
        return new()
        {
            PluginsRoot = Path.Combine(userRoot, "Plugins"),
            UserRoot = userRoot,
            ProjectScope = projectScope,
            AdditionalDiscoveryRoots = options.RuntimeProbe is not null || options.BusinessProbe is not null ? [] : [bundled],
            SharedAssemblies = [typeof(WinUiView).Assembly, typeof(FrameworkElement).Assembly,
                typeof(WinRT.ComWrappersSupport).Assembly, typeof(Windows.Foundation.IAsyncAction).Assembly],
            SharedAssemblyNames = projectionNames
        };
    }

    private static void CopySample(string source, string destination)
    {
        HostRuntime.RejectLinks(source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Bundled smoke package is missing: {source}");
        HostRuntime.RejectLinks(destination);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            HostRuntime.RejectLinks(file);
            var target = Path.Combine(destination, Path.GetFileName(file));
            HostRuntime.RejectLinks(target);
            // A project can be reopened in this same process. Its retired assembly may remain
            // mapped by Windows; identical curated bytes must be reused without rewriting it.
            if (File.Exists(target))
            {
                if (!System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))
                    .SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(target))))
                    throw new IOException("The isolated curated sample changed.");
            }
            else File.Copy(file, target);
        }
        if (Directory.EnumerateDirectories(source).Any())
            throw new IOException("Bundled smoke samples must be flat code-only packages.");
    }
}
