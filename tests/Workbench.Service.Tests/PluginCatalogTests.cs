using Workbench.Service.Abstractions;
using Xunit;

namespace Workbench.Service.Tests;

public sealed class PluginCatalogTests
{
    [Fact]
    public async Task Load_rejects_invalid_packages_and_duplicate_ids()
    {
        Assert.Empty(await PluginV2Catalog.LoadAsync(Path.Combine(Path.GetTempPath(), "wb-missing-" + Guid.NewGuid().ToString("N")), CancellationToken.None));
        var root = NewRoot();
        try
        {
            Assert.Empty(await PluginV2Catalog.LoadAsync(root, CancellationToken.None));
            Write("no-manifest", null);
            Write("bad json", "{");
            Write("not-object", "[]");
            Write("duplicate-key", """
            { "contract": "workbench.plugin.v2", "contract": "again" }
            """);
            Write("com.example.empty-text", Manifest(id: "com.example.empty-text", name: ""));
            Write("com.example.contract", Manifest(id: "com.example.contract", contract: "other"));
            Write("com.example.unsafe-id", Manifest(id: "Not Safe"));
            Write("com.example.ui", Manifest(id: "com.example.ui", uiKind: "native", mount: "window"));
            Write("com.example.assembly", Manifest(id: "com.example.assembly", assembly: "bad:name.dll", entryType: ""));
            Write("com.example.permissions", Manifest(id: "com.example.permissions", permissions: "[]"));
            Write("com.example.blank-permission", Manifest(id: "com.example.blank-permission", permissions: """
            [
              { "id": " ", "reason": "x" },
              { "id": "project.read", "reason": " " },
              { "id": "message.send", "reason": "no", "sensitive": false },
              { "id": "plugin.data.read", "reason": "yes", "sensitive": true }
            ]
            """));
            var many = string.Join(",", Enumerable.Range(0, 33).Select(index => $$"""{ "id": "project.read", "reason": "r{{index}}" }"""));
            Write("com.example.too-many", Manifest(id: "com.example.too-many", permissions: "[" + many + "]"));
            Write("com.example.bad-image", Manifest(id: "com.example.bad-image", assembly: "NotAPlugin.dll"));
            File.WriteAllText(Path.Combine(root, "com.example.bad-image", "NotAPlugin.dll"), "not an assembly");
            Write("com.example.duplicate", Manifest(id: "com.example.duplicate", assembly: "Missing.dll"));
            Write("com.example.duplicate-copy", Manifest(id: "com.example.duplicate", assembly: "Missing.dll"));

            var loaded = await PluginV2Catalog.LoadAsync(root, CancellationToken.None);
            Assert.Contains(loaded, package => package.Id == "invalid" && package.State == "Invalid");
            Assert.Contains("IncompatibleContract", ById(loaded, "com.example.contract").DiagnosticCodes);
            Assert.Contains("MissingAssembly", ById(loaded, "com.example.contract").DiagnosticCodes);
            Assert.Equal("com.example.unsafe-id", ById(loaded, "com.example.unsafe-id").Id);
            Assert.Contains("InvalidManifest", ById(loaded, "com.example.empty-text").DiagnosticCodes);
            Assert.Contains("InvalidManifest", ById(loaded, "com.example.ui").DiagnosticCodes);
            Assert.Contains("InvalidManifest", ById(loaded, "com.example.assembly").DiagnosticCodes);
            Assert.Contains("InvalidManifest", ById(loaded, "com.example.permissions").DiagnosticCodes);
            var blank = ById(loaded, "com.example.blank-permission");
            Assert.Contains("PermissionRejected", blank.DiagnosticCodes);
            Assert.Contains("SensitiveActionRequiresConfirmation", blank.DiagnosticCodes);
            Assert.DoesNotContain("plugin.data.read", blank.GrantedPermissions);
            Assert.Contains("InvalidManifest", ById(loaded, "com.example.too-many").DiagnosticCodes);
            Assert.Equal(["LoadFailed"], ById(loaded, "com.example.bad-image").DiagnosticCodes);
            var duplicates = loaded.Where(package => package.Id == "com.example.duplicate").ToArray();
            Assert.Equal(2, duplicates.Length);
            Assert.All(duplicates, package =>
            {
                Assert.Contains("DuplicatePluginId", package.DiagnosticCodes);
                Assert.False(package.Loaded);
            });

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PluginV2Catalog.LoadAsync(root, canceled.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        string NewRoot()
        {
            var path = Path.Combine(Path.GetTempPath(), "wb-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        void Write(string folder, string? manifest)
        {
            var directory = Path.Combine(root, folder);
            Directory.CreateDirectory(directory);
            if (manifest is not null) File.WriteAllText(Path.Combine(directory, "plugin.v2.json"), manifest);
        }
    }

    [Fact]
    public void Plugin_ids_match_the_reverse_dns_pattern()
    {
        Assert.True(PluginV2Catalog.IsSafePluginId("com.projectworkbench.test-cases.v2"));
        Assert.False(PluginV2Catalog.IsSafePluginId(null));
        Assert.False(PluginV2Catalog.IsSafePluginId(""));
        Assert.False(PluginV2Catalog.IsSafePluginId(new string('a', 129)));
        Assert.False(PluginV2Catalog.IsSafePluginId("com.Bad.Id"));
        Assert.False(PluginV2Catalog.IsSafePluginId("single"));
    }

    private static V2Package ById(IReadOnlyList<V2Package> packages, string id)
        => Assert.Single(packages, package => package.Id == id);

    private static string Manifest(
        string id,
        string contract = PluginContractV2.Name,
        string version = PluginContractV2.Version,
        string name = "示例",
        string description = "用于覆盖校验分支的本地清单。",
        string assembly = "Missing.dll",
        string entryType = "Example.Module",
        string uiKind = "web-package",
        string mount = "embedded",
        string permissions = """[{ "id": "project.read", "reason": "读取项目" }]""")
        => $$"""
        {
          "contract": "{{contract}}",
          "contractVersion": "{{version}}",
          "id": "{{id}}",
          "name": "{{name}}",
          "version": "0.1.0",
          "description": "{{description}}",
          "autoSend": false,
          "backend": { "entryAssembly": "{{assembly}}", "entryType": "{{entryType}}" },
          "ui": { "kind": "{{uiKind}}", "module": "./ui/Missing.tsx", "mount": "{{mount}}" },
          "permissions": {{permissions}}
        }
        """;
}
