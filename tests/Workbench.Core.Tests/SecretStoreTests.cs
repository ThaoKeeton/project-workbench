using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workbench.Core;
using Workbench.Sdk;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class SecretStoreTests
{
    private const string Id = "com.example.runtime";

    [Fact]
    public async Task Declared_secret_metadata_is_scoped_and_unsupported_provider_never_writes_plaintext()
    {
        using var disk = BackgroundTaskTests.Package();
        await using var manager = BackgroundTaskTests.Manager(disk);
        await manager.DiscoverAsync();
        Assert.Empty(manager.GetSecretDefinitions(Id));
        var enabled = await manager.EnableAsync(Id, confirmTrust: true);
        Assert.True(enabled.Succeeded, enabled.Message);
        Assert.Equal(new[] { "openai.api-key", "dingtalk.webhook-url" }, manager.GetSecretDefinitions(Id).Select(d => d.Id));
        if (OperatingSystem.IsWindows()) return; // The real provider is exercised separately on Windows.
        Assert.False(manager.IsSecretStorageSupported(Id));
        var error = await Assert.ThrowsAsync<PluginSecretStoreException>(() => manager.WriteSecretAsync(Id, "openai.api-key", "synthetic-secret-never-log"));
        Assert.Equal("SecretStorageUnsupported", error.ErrorCode);
        Assert.Null(error.InnerException);
        Assert.False(Directory.Exists(Path.Combine(disk.User, "secrets")));
        Assert.All(Assert.Single(manager.Plugins).Diagnostics, d => Assert.DoesNotContain("synthetic-secret-never-log", d.Message));
    }

    [Fact]
    public async Task Fake_protector_exercises_real_fixture_declarations_and_plugin_key_storage_boundaries()
    {
        using var disk = BackgroundTaskTests.Package();
        const string second = "com.example.second";
        disk.Package(second, fields: new() { ["entryType"] = "Workbench.TestPlugin.RuntimeFixturePlugin" });
        var protector = new FakeProtector();
        await using var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User }, protector);
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.True((await manager.EnableAsync(second, confirmTrust: true)).Succeeded);
        var unknown = await Assert.ThrowsAsync<PluginSecretStoreException>(() => manager.WriteSecretAsync(Id, "undeclared", "synthetic-secret-never-log"));
        Assert.Equal("SecretNotDeclared", unknown.ErrorCode);
        Assert.False(Directory.Exists(Path.Combine(disk.User, "secrets")));

        await manager.WriteSecretAsync(Id, "openai.api-key", "synthetic-secret-never-log");
        Assert.Equal("synthetic-secret-never-log", await manager.ReadSecretAsync(Id, "openai.api-key"));
        Assert.Null(await manager.ReadSecretAsync(second, "openai.api-key"));
        Assert.Null(await manager.ReadSecretAsync(Id, "dingtalk.webhook-url"));
        Assert.All(protector.PlaintextBuffers.SelectMany(buffer => buffer), value => Assert.Equal(0, value));

        var directory = Path.Combine(disk.User, "secrets", Id);
        var firstPath = Assert.Single(Directory.EnumerateFiles(directory));
        Assert.Matches("^[a-f0-9]{64}\\.bin$", Path.GetFileName(firstPath));
        Assert.DoesNotContain("synthetic-secret-never-log", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(firstPath)));
        await manager.WriteSecretAsync(Id, "dingtalk.webhook-url", "https://synthetic.invalid/token=synthetic-secret-never-log");
        var otherPath = Directory.EnumerateFiles(directory).Single(path => path != firstPath);
        File.Copy(firstPath, otherPath, overwrite: true);
        Assert.Equal("SecretReadFailed", (await Assert.ThrowsAsync<PluginSecretStoreException>(() => manager.ReadSecretAsync(Id, "dingtalk.webhook-url"))).ErrorCode);

        var secondDirectory = Path.Combine(disk.User, "secrets", second);
        Directory.CreateDirectory(secondDirectory);
        File.Copy(firstPath, Path.Combine(secondDirectory, Path.GetFileName(firstPath)));
        Assert.Equal("SecretReadFailed", (await Assert.ThrowsAsync<PluginSecretStoreException>(() => manager.ReadSecretAsync(second, "openai.api-key"))).ErrorCode);
        await manager.DeleteSecretAsync(Id, "openai.api-key");
        Assert.Null(await manager.ReadSecretAsync(Id, "openai.api-key"));
        Assert.All(manager.Plugins.SelectMany(plugin => plugin.Diagnostics), d => Assert.DoesNotContain("synthetic-secret-never-log", d.Message));
        Assert.All(manager.GetSecretDefinitions(Id), definition => Assert.DoesNotContain("synthetic-secret-never-log", definition.ToString()));
    }

    [Fact]
    public async Task Fake_provider_exception_and_corrupt_ciphertext_return_only_safe_codes_with_no_inner_exception()
    {
        using var disk = new TestDirectory();
        var diagnostics = new List<PluginDiagnostic>();
        using var store = new PluginSecretStore(disk.User, Id, new FailingProtector(), diagnostics.Add);
        store.Register(new("api-key", "Synthetic key"));
        var write = await Assert.ThrowsAsync<PluginSecretStoreException>(() => store.WriteAsync("api-key", "synthetic-secret-never-log"));
        Assert.Equal("SecretWriteFailed", write.ErrorCode);
        Assert.Null(write.InnerException);
        Assert.DoesNotContain("synthetic-secret-never-log", write.ToString());
        Assert.False(Directory.Exists(Path.Combine(disk.User, "secrets")));
        Assert.All(diagnostics, d => Assert.DoesNotContain("synthetic-secret-never-log", d.Message));
    }

    [Fact]
    public async Task Fake_protector_checks_cancellation_and_link_rejection_without_claiming_encryption()
    {
        using var disk = new TestDirectory();
        using var store = new PluginSecretStore(disk.User, Id, new FakeProtector(), _ => { });
        store.Register(new("api-key", "Synthetic key"));
        await store.WriteAsync("api-key", "synthetic original");
        var path = Assert.Single(Directory.EnumerateFiles(Path.Combine(disk.User, "secrets", Id)));
        var before = await File.ReadAllBytesAsync(path);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync("api-key", "synthetic lost", canceled.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var corrupt = await Assert.ThrowsAsync<PluginSecretStoreException>(() => store.ReadAsync("api-key"));
        Assert.Equal("SecretReadFailed", corrupt.ErrorCode);
        Assert.Null(corrupt.InnerException);
        await store.DeleteAsync("api-key");
        var outside = Path.Combine(disk.Root, "outside-cipher.bin");
        await File.WriteAllBytesAsync(outside, before);
        File.CreateSymbolicLink(path, outside);
        Assert.Equal("SecretReadFailed", (await Assert.ThrowsAsync<PluginSecretStoreException>(() => store.ReadAsync("api-key"))).ErrorCode);
        Assert.Equal("SecretWriteFailed", (await Assert.ThrowsAsync<PluginSecretStoreException>(() => store.WriteAsync("api-key", "synthetic lost"))).ErrorCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(outside));
    }

    [WindowsFact]
    public async Task Real_Windows_DPAPI_synthetic_roundtrip_and_corruption_are_safe()
    {
        using var disk = BackgroundTaskTests.Package();
        await using var manager = BackgroundTaskTests.Manager(disk);
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.True(manager.IsSecretStorageSupported(Id));
        await manager.WriteSecretAsync(Id, "openai.api-key", "synthetic-windows-value-only");
        Assert.Equal("synthetic-windows-value-only", await manager.ReadSecretAsync(Id, "openai.api-key"));
        var path = Assert.Single(Directory.EnumerateFiles(Path.Combine(disk.User, "secrets", Id)));
        Assert.DoesNotContain("synthetic-windows-value-only", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path)));
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var error = await Assert.ThrowsAsync<PluginSecretStoreException>(() => manager.ReadSecretAsync(Id, "openai.api-key"));
        Assert.Equal("SecretReadFailed", error.ErrorCode);
        Assert.Null(error.InnerException);
        await manager.DeleteSecretAsync(Id, "openai.api-key");
        Assert.Null(await manager.ReadSecretAsync(Id, "openai.api-key"));
    }

    [WindowsFact]
    public async Task Real_Windows_DPAPI_survives_same_user_process_restart_with_only_synthetic_credentials()
    {
        using var disk = BackgroundTaskTests.Package();
        using var written = await ProbeAsync(disk, "secret-write");
        Assert.True(written.RootElement.GetProperty("Supported").GetBoolean());
        Assert.True(written.RootElement.GetProperty("Saved").GetBoolean());
        using var restarted = await ProbeAsync(disk, "secret-read-delete");
        Assert.True(restarted.RootElement.GetProperty("Verified").GetBoolean());
        Assert.True(restarted.RootElement.GetProperty("MissingAfterDelete").GetBoolean());
    }

    private static async Task<JsonDocument> ProbeAsync(TestDirectory disk, string operation)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "deployment-probe", "Workbench.DeploymentProbe.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { disk.Plugins, disk.User, Id, operation }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The synthetic secret probe did not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await errors);
            return JsonDocument.Parse(await output);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    // A deterministic boundary double. It is intentionally not encryption or DPAPI evidence.
    private sealed class FakeProtector : ISecretProtector
    {
        internal List<byte[]> PlaintextBuffers { get; } = [];
        public bool IsSupported => true;
        public byte[] Protect(byte[] plaintext, byte[] entropy)
        {
            PlaintextBuffers.Add(plaintext);
            return [.. SHA256.HashData(entropy), .. plaintext.Select(value => (byte)(value ^ 0x5A))];
        }
        public byte[] Unprotect(byte[] ciphertext, byte[] entropy)
        {
            if (ciphertext.Length < 32 || !ciphertext.AsSpan(0, 32).SequenceEqual(SHA256.HashData(entropy)))
                throw new CryptographicException("synthetic-secret-never-log");
            var plaintext = ciphertext[32..].Select(value => (byte)(value ^ 0x5A)).ToArray();
            PlaintextBuffers.Add(plaintext);
            return plaintext;
        }
    }

    private sealed class FailingProtector : ISecretProtector
    {
        public bool IsSupported => true;
        public byte[] Protect(byte[] plaintext, byte[] entropy) => throw new CryptographicException("synthetic-secret-never-log");
        public byte[] Unprotect(byte[] ciphertext, byte[] entropy) => throw new CryptographicException("synthetic-secret-never-log");
    }
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires real Windows DPAPI CurrentUser; Linux fake-provider tests are not encryption evidence."; }
}
