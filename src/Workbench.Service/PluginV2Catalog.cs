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

    public static async Task<IReadOnlyList<V2Package>> LoadAsync(string root, string? previewUserRoot = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root)) return [];
        List<V2Package> packages = [];
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            packages.Add(await LoadDirectoryAsync(directory, previewUserRoot, cancellationToken));
        }
        var duplicates = packages.Where(p => IsSafePluginId(p.Id)).GroupBy(p => p.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        if (duplicates.Count == 0) return packages;
        return packages.Select(p => duplicates.Contains(p.Id)
            ? p with { State = "Invalid", Loaded = false, Module = null, DiagnosticCodes = [.. p.DiagnosticCodes, "DuplicatePluginId"] }
            : p).ToArray();
    }

    private static async Task<V2Package> LoadDirectoryAsync(string directory, string? previewUserRoot, CancellationToken cancellationToken)
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
                    UiPath = UiPathFor(manifest.Id),
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

    internal static PluginImportReport InspectText(string text, IReadOnlySet<string> existingIds)
    {
        V2Manifest manifest;
        try { manifest = ParseManifest(text); }
        catch (JsonException)
        {
            return new(false, "InvalidManifest", null, null, null, null, [new("manifest", false, "这不是插件清单。请选择 plugin.v2.json。")]);
        }

        var checks = new List<PluginImportCheck> { new("manifest", true, "这是一份插件清单。") };
        var contractOk = AddContract(checks, manifest);
        var idOk = AddIdentity(checks, manifest);
        var sendOk = AddSend(checks, manifest);
        var shapeOk = AddShape(checks, manifest);
        var permissionsOk = AddPermissions(checks, manifest);
        var duplicate = idOk && existingIds.Contains(manifest.Id);
        checks.Add(new("duplicate", !duplicate, duplicate ? "这台电脑上已经有这个插件。" : "没有和已有插件撞号。"));
        var error = ImportError(contractOk, idOk, shapeOk, sendOk, permissionsOk, duplicate, manifest.Permissions.Count);
        var importable = error is null;
        return new(importable, error, idOk ? manifest.Id : null, Clip(manifest.Name), Clip(manifest.Version), importable ? text : null, checks);
    }

    private static bool AddContract(List<PluginImportCheck> checks, V2Manifest manifest)
    {
        var ok = manifest.Contract == PluginContractV2.Name && manifest.ContractVersion == PluginContractV2.Version;
        checks.Add(new("contract", ok, ok ? "契约是这台预览认识的版本。" : "契约版本对不上，不能导入。"));
        return ok;
    }

    private static bool AddIdentity(List<PluginImportCheck> checks, V2Manifest manifest)
    {
        var ok = IsSafePluginId(manifest.Id);
        checks.Add(new("identity", ok, ok ? $"插件是「{Clip(manifest.Name)}」，版本 {Clip(manifest.Version)}。" : "插件编号不符合要求。"));
        return ok;
    }

    private static bool AddSend(List<PluginImportCheck> checks, V2Manifest manifest)
    {
        var ok = !manifest.AutoSend;
        checks.Add(new("autosend", ok, ok ? "没有要求自动发送。" : "这份清单要求自动发送，不能导入。"));
        return ok;
    }

    private static bool AddShape(List<PluginImportCheck> checks, V2Manifest manifest)
    {
        var ok = manifest.Ui.Kind == "web-package" && manifest.Ui.Mount == "embedded"
            && IsFileName(manifest.Backend.EntryAssembly)
            && manifest.Backend.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(manifest.Backend.EntryType);
        checks.Add(new("shape", ok, ok
            ? "界面会嵌在这个窗口里。没有附带后端程序，所以记下后不会启用。"
            : "清单里的界面或后端写法不符合要求。"));
        return ok;
    }

    private static bool AddPermissions(List<PluginImportCheck> checks, V2Manifest manifest)
    {
        var ok = manifest.Permissions.Count is > 0 and <= 32;
        if (!ok) checks.Add(new("permissions", false, "权限列表为空或太长。"));
        foreach (var permission in manifest.Permissions) ok = AddPermission(checks, permission) && ok;
        return ok;
    }

    private static bool AddPermission(List<PluginImportCheck> checks, V2Permission permission)
    {
        if (string.IsNullOrWhiteSpace(permission.Id) || string.IsNullOrWhiteSpace(permission.Reason))
        {
            checks.Add(new("permissions", false, "有一项权限没有写清。"));
            return false;
        }
        if (permission.Sensitive || !AllowedPermissions.Contains(permission.Id))
        {
            checks.Add(new("permissions", false, permission.Sensitive
                ? $"权限「{permission.Id}」需要单独确认，这台预览不导入。"
                : $"不认识的权限「{permission.Id}」，不能导入。"));
            return false;
        }
        checks.Add(new("permissions", true, $"允许「{permission.Id}」：{Clip(permission.Reason)}。不会联网。"));
        return true;
    }

    private static string? ImportError(bool contractOk, bool idOk, bool shapeOk, bool sendOk, bool permissionsOk, bool duplicate, int permissionCount)
    {
        if (!contractOk) return "IncompatibleContract";
        if (!idOk || !shapeOk || permissionCount is 0 or > 32) return "InvalidManifest";
        if (!sendOk) return "AutoSendRejected";
        if (!permissionsOk) return "PermissionRejected";
        if (duplicate) return "DuplicatePluginId";
        return null;
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

    private static string? UiPathFor(string id) => id switch
    {
        "com.projectworkbench.test-cases.v2" => SampleUiPath,
        "com.projectworkbench.upcam-standup.v2" => "/plugin/upcam",
        "com.projectworkbench.dingtalk-insights.v2" => "/plugin/ding",
        "com.projectworkbench.testing-design.v2" => "/plugin/test",
        "com.projectworkbench.test-executor" => "/plugin/runner",
        _ => null
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

internal readonly record struct PluginImportCheck(string Id, bool Ok, string Text);

internal sealed record PluginImportReport(
    bool Importable,
    string? Error,
    string? PluginId,
    string? Name,
    string? Version,
    string? ManifestText,
    IReadOnlyList<PluginImportCheck> Checks);
