using System.Security.Cryptography;
using System.Text;
using Workbench.Sdk;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class SecretMetadataTests
{
    private const string Id = "com.example.runtime";

    [Fact]
    public async Task Metadata_reports_missing_and_stored_ciphertext_without_ever_decrypting_or_validating_values()
    {
        using var disk = BackgroundTaskTests.Package();
        var protector = new NeverDecryptProtector();
        await using var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User }, protector);
        await manager.DiscoverAsync();
        Assert.Empty(manager.GetSecretSnapshots(Id));
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        Assert.All(manager.GetSecretSnapshots(Id), snapshot => Assert.Equal(false, snapshot.IsConfigured));
        await manager.WriteSecretAsync(Id, "openai.api-key", "synthetic-secret-never-log");
        var saved = manager.GetSecretSnapshots(Id).Single(snapshot => snapshot.Id == "openai.api-key");
        Assert.Equal(true, saved.IsConfigured);
        Assert.Null(saved.ErrorCode);
        var path = Assert.Single(Directory.EnumerateFiles(Path.Combine(disk.User, "secrets", Id)));
        await File.WriteAllBytesAsync(path, [0]); // Presence is deliberately not a promise of usable credentials.
        Assert.Equal(true, manager.GetSecretSnapshots(Id).Single(snapshot => snapshot.Id == saved.Id).IsConfigured);
        await manager.DeleteSecretAsync(Id, saved.Id);
        Assert.Equal(false, manager.GetSecretSnapshots(Id).Single(snapshot => snapshot.Id == saved.Id).IsConfigured);
        Assert.Equal(0, protector.DecryptCalls);
        Assert.All(manager.GetSecretSnapshots(Id), snapshot => Assert.DoesNotContain("synthetic-secret-never-log", snapshot.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nonregular_or_linked_metadata_returns_constant_unknown_without_exposing_paths(bool link)
    {
        using var disk = BackgroundTaskTests.Package();
        var protector = new NeverDecryptProtector();
        await using var manager = new PluginManager(new() { PluginsRoot = disk.Plugins, UserRoot = disk.User }, protector);
        await manager.DiscoverAsync();
        Assert.True((await manager.EnableAsync(Id, confirmTrust: true)).Succeeded);
        var directory = Path.Combine(disk.User, "secrets", Id);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("openai.api-key")))) + ".bin";
        if (link)
        {
            var outside = Path.Combine(disk.Root, "synthetic-secret-never-log");
            await File.WriteAllTextAsync(outside, "synthetic outside");
            File.CreateSymbolicLink(path, outside);
        }
        else Directory.CreateDirectory(path);
        var snapshot = manager.GetSecretSnapshots(Id).Single(snapshot => snapshot.Id == "openai.api-key");
        Assert.Null(snapshot.IsConfigured);
        Assert.Equal("SecretMetadataUnavailable", snapshot.ErrorCode);
        Assert.DoesNotContain(disk.Root, snapshot.ToString());
        Assert.Equal(0, protector.DecryptCalls);
    }

    private sealed class NeverDecryptProtector : ISecretProtector
    {
        internal int DecryptCalls;
        public bool IsSupported => true;
        public byte[] Protect(byte[] plaintext, byte[] entropy) => [1, 2, 3];
        public byte[] Unprotect(byte[] ciphertext, byte[] entropy)
        {
            DecryptCalls++;
            throw new CryptographicException("synthetic-secret-never-log");
        }
    }
}
