using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Workbench.Service.Abstractions;

namespace Workbench.Service;

internal sealed record V2PermissionView(string Id, bool Sensitive);

internal sealed record V2Package
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string State { get; init; }
    public required bool Loaded { get; init; }
    public required IReadOnlyList<string> DiagnosticCodes { get; init; }
    public required IReadOnlyList<V2PermissionView> Permissions { get; init; }
    public string? UiPath { get; init; }
    public IWorkbenchPluginModule? Module { get; init; }
    public IReadOnlySet<string> GrantedPermissions { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

internal static partial class PluginV2Catalog
{
    public const string SampleUiPath = "/plugins/test-cases";
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false
    };

    private static readonly HashSet<string> AllowedPermissions = new(StringComparer.Ordinal)
    {
        "project.read", "plugin.data.read", "plugin.data.write", "local.file.export"
    };

    [GeneratedRegex("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*(?:\\.[a-z][a-z0-9]*(?:-[a-z0-9]+)*)+\\z")]
    private static partial Regex PluginIdPattern();

    public static bool IsSafePluginId(string? id) => id is { Length: > 0 and <= 128 } && PluginIdPattern().IsMatch(id);

    public static async Task<IReadOnlyList<V2Package>> LoadAsync(string root, CancellationToken cancellationToken = default, string? previewUserRoot = null)
    {
        if (!Directory.Exists(root)) return [];
        List<V2Package> packages = [];
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            packages.Add(await LoadDirectoryAsync(directory, cancellationToken, previewUserRoot));
        }
        var duplicates = packages.Where(p => IsSafePluginId(p.Id)).GroupBy(p => p.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        if (duplicates.Count == 0) return packages;
        return packages.Select(p => duplicates.Contains(p.Id)
            ? p with { State = "Invalid", Loaded = false, Module = null, DiagnosticCodes = [.. p.DiagnosticCodes, "DuplicatePluginId"] }
            : p).ToArray();
    }

    private static async Task<V2Package> LoadDirectoryAsync(string directory, CancellationToken cancellationToken, string? previewUserRoot)
    {
        var manifestPath = Path.Combine(directory, "plugin.v2.json");
        if (!File.Exists(manifestPath))
            return Invalid(Path.GetFileName(directory), "InvalidManifest");
        try
        {
            var text = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = ParseManifest(text);
            List<string> codes = [];
            if (manifest.Contract != PluginContractV2.Name || manifest.ContractVersion != PluginContractV2.Version)
                codes.Add("IncompatibleContract");
            if (!IsSafePluginId(manifest.Id)) codes.Add("InvalidManifest");
            if (manifest.AutoSend) codes.Add("AutoSendRejected");
            if (manifest.Ui.Kind != "web-package" || manifest.Ui.Mount != "embedded") codes.Add("InvalidManifest");
            if (!IsFileName(manifest.Backend.EntryAssembly) || !manifest.Backend.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                codes.Add("InvalidManifest");
            if (string.IsNullOrWhiteSpace(manifest.Backend.EntryType)) codes.Add("InvalidManifest");
            var granted = new HashSet<string>(StringComparer.Ordinal);
            List<V2PermissionView> permissions = [];
            if (manifest.Permissions.Count is 0 or > 32) codes.Add("InvalidManifest");
            foreach (var permission in manifest.Permissions)
            {
                if (string.IsNullOrWhiteSpace(permission.Id) || string.IsNullOrWhiteSpace(permission.Reason))
                {
                    codes.Add("InvalidManifest");
                    continue;
                }
                permissions.Add(new(permission.Id, permission.Sensitive));
                if (!AllowedPermissions.Contains(permission.Id)) codes.Add("PermissionRejected");
                if (permission.Sensitive) codes.Add("SensitiveActionRequiresConfirmation");
                else if (AllowedPermissions.Contains(permission.Id)) granted.Add(permission.Id);
            }
            var assemblyPath = Path.Combine(directory, manifest.Backend.EntryAssembly);
            if (!File.Exists(assemblyPath)) codes.Add("MissingAssembly");
            codes = codes.Distinct(StringComparer.Ordinal).ToList();
            if (codes.Count > 0)
                return new V2Package
                {
                    Id = IsSafePluginId(manifest.Id) ? manifest.Id : SafeFolder(directory),
                    Name = Clip(manifest.Name),
                    Version = Clip(manifest.Version),
                    State = "Invalid",
                    Loaded = false,
                    DiagnosticCodes = codes,
                    Permissions = permissions,
                    GrantedPermissions = granted
                };
            try
            {
                var module = LoadModule(assemblyPath, manifest.Backend.EntryType, manifest.Id);
                await module.StartAsync(new PluginModuleStart(granted) { PreviewUserRoot = previewUserRoot }, cancellationToken);
                return new V2Package
                {
                    Id = manifest.Id,
                    Name = Clip(manifest.Name),
                    Version = Clip(manifest.Version),
                    State = "Ready",
                    Loaded = true,
                    DiagnosticCodes = [],
                    Permissions = permissions,
                    UiPath = manifest.Id == "com.projectworkbench.test-cases.v2" ? SampleUiPath : null,
                    Module = module,
                    GrantedPermissions = granted
                };
            }
            catch (Exception)
            {
                return new V2Package
                {
                    Id = manifest.Id,
                    Name = Clip(manifest.Name),
                    Version = Clip(manifest.Version),
                    State = "Invalid",
                    Loaded = false,
                    DiagnosticCodes = ["LoadFailed"],
                    Permissions = permissions,
                    GrantedPermissions = granted
                };
            }
        }
        catch (JsonException)
        {
            return Invalid(SafeFolder(directory), "InvalidManifest");
        }
    }

    private static V2Manifest ParseManifest(string text)
    {
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("manifest");
        RejectDuplicates(document.RootElement);
        var manifest = JsonSerializer.Deserialize<V2Manifest>(text, ManifestJson) ?? throw new JsonException("manifest");
        if (manifest.Name is not { Length: > 0 and <= 200 } || manifest.Description is not { Length: > 0 and <= 4000 }
            || manifest.Version is not { Length: > 0 and <= 32 })
            throw new JsonException("manifest");
        return manifest;
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new JsonException("duplicate");
            if (property.Value.ValueKind == JsonValueKind.Object) RejectDuplicates(property.Value);
        }
    }

    private static IWorkbenchPluginModule LoadModule(string assemblyPath, string entryType, string id)
    {
        var context = new PluginModuleLoadContext(assemblyPath);
        var assembly = context.LoadFromAssemblyPath(assemblyPath);
        var type = assembly.GetType(entryType, throwOnError: false) ?? throw new InvalidOperationException("entry");
        if (Activator.CreateInstance(type) is not IWorkbenchPluginModule module || module.Id != id)
            throw new InvalidOperationException("entry");
        return module;
    }

    private static V2Package Invalid(string id, string code) => new()
    {
        Id = IsSafePluginId(id) ? id : "invalid",
        Name = "无效插件",
        Version = "0.0.0",
        State = "Invalid",
        Loaded = false,
        DiagnosticCodes = [code],
        Permissions = []
    };

    private static string SafeFolder(string directory)
    {
        var name = Path.GetFileName(directory);
        return IsSafePluginId(name) ? name : "invalid";
    }

    private static string Clip(string value) => value.Length <= 200 ? value : value[..200];

    private static bool IsFileName(string? value) => value is { Length: > 0 and <= 128 }
        && value.IndexOfAny(['/', '\\', ':']) < 0 && value is not "." and not "..";

    private sealed class PluginModuleLoadContext(string assemblyPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(IWorkbenchPluginModule).Assembly.GetName().Name)
                return typeof(IWorkbenchPluginModule).Assembly;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }

    private sealed record V2Manifest
    {
        public required string Contract { get; init; }
        public required string ContractVersion { get; init; }
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Version { get; init; }
        public required string Description { get; init; }
        public required bool AutoSend { get; init; }
        public required V2Backend Backend { get; init; }
        public required V2Ui Ui { get; init; }
        public required IReadOnlyList<V2Permission> Permissions { get; init; }
    }

    private sealed record V2Backend
    {
        public required string EntryAssembly { get; init; }
        public required string EntryType { get; init; }
    }

    private sealed record V2Ui
    {
        public required string Kind { get; init; }
        public required string Module { get; init; }
        public required string Mount { get; init; }
    }

    private sealed record V2Permission
    {
        public required string Id { get; init; }
        public required string Reason { get; init; }
        public bool Sensitive { get; init; }
    }
}
