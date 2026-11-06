using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.Core;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class SettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plugin_converter_failures_return_defaults_with_only_safe_diagnostics(bool unknownFailure)
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        var settings = manager.GetSettings("com.example.fixture");
        await settings.SetAsync("converted", "synthetic stored value");
        if (unknownFailure)
        {
            var fallback = new HostileValue();
            Assert.Same(fallback, await settings.GetAsync("converted", fallback));
        }
        else
        {
            var fallback = new SecretValue();
            Assert.Same(fallback, await settings.GetAsync("converted", fallback));
        }
        var diagnostic = Assert.Single(Assert.Single(manager.Plugins).Diagnostics);
        Assert.Equal(PluginDiagnosticCode.SettingsCorrupt, diagnostic.Code);
        Assert.Contains("SettingsConversionFailed", diagnostic.Message);
        Assert.DoesNotContain("synthetic-converter-private-response", diagnostic.Message);
        Assert.Equal("synthetic stored value", await settings.GetAsync("converted", "missing"));
    }

    [JsonConverter(typeof(SecretConverter))]
    public sealed class SecretValue;
    public sealed class SecretConverter : JsonConverter<SecretValue>
    {
        public override SecretValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new JsonException("synthetic-converter-private-response");
        public override void Write(Utf8JsonWriter writer, SecretValue value, JsonSerializerOptions options) => throw new NotSupportedException();
    }

    [JsonConverter(typeof(HostileConverter))]
    public sealed class HostileValue;
    public sealed class HostileConverter : JsonConverter<HostileValue>
    {
        public override HostileValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new HostileConverterException();
        public override void Write(Utf8JsonWriter writer, HostileValue value, JsonSerializerOptions options) => throw new NotSupportedException();
    }
    public sealed class HostileConverterException : Exception
    {
        public override string Message => throw new InvalidOperationException("A converter exception Message must not be evaluated.");
        public override string ToString() => throw new InvalidOperationException("A converter exception ToString must not be evaluated.");
    }

    [Fact]
    public async Task Defaults_and_json_values_persist_across_fresh_manager_instances()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.first");
        disk.Package("com.example.second");
        await using (var manager = disk.Manager())
        {
            await manager.DiscoverAsync();
            var first = manager.GetSettings("com.example.first");
            Assert.Equal("demo draft", await first.GetAsync("draft", "demo draft"));
            await first.SetAsync("draft", "saved draft");
            await first.SetAsync("numbers", new[] { 2, 3, 5 });
            Assert.Equal("second default", await manager.GetSettings("com.example.second").GetAsync("draft", "second default"));
        }
        await using var restarted = disk.Manager();
        await restarted.DiscoverAsync();

        Assert.Equal("saved draft", await restarted.GetSettings("com.example.first").GetAsync("draft", "wrong default"));
        Assert.Equal(new[] { 2, 3, 5 }, await restarted.GetSettings("com.example.first").GetAsync("numbers", Array.Empty<int>()));
        Assert.False(Assert.Single(restarted.Plugins, p => p.Manifest?.Id == "com.example.first").IsLoaded);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a..b")]
    [InlineData("")]
    [InlineData("C:outside")]
    [InlineData("draft\n")]
    public async Task Setting_keys_cannot_address_a_path(string key)
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        var settings = manager.GetSettings("com.example.fixture");

        await Assert.ThrowsAsync<ArgumentException>(() => settings.SetAsync(key, "bad"));
        await Assert.ThrowsAsync<ArgumentException>(() => settings.GetAsync(key, "default"));
        Assert.False(File.Exists(Path.Combine(disk.User, "escape")));
    }

    [Fact]
    public async Task Plugin_ids_cannot_escape_the_settings_root()
    {
        using var disk = new TestDirectory();
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();

        Assert.Throws<ArgumentException>(() => manager.GetSettings("../escape"));
        Assert.Throws<KeyNotFoundException>(() => manager.GetSettings("com.example.absent"));
    }

    [Fact]
    public async Task Symlinked_plugin_settings_directory_cannot_write_outside_the_user_root()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        var outside = Path.Combine(disk.Root, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Path.Combine(disk.User, "settings"));
        Directory.CreateSymbolicLink(Path.Combine(disk.User, "settings", "com.example.fixture"), outside);
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();

        await Assert.ThrowsAsync<IOException>(() => manager.GetSettings("com.example.fixture").SetAsync("draft", "escape"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public async Task Corrupt_json_and_incompatible_value_types_return_defaults_with_diagnostics()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        var directory = Path.Combine(disk.User, "settings", "com.example.fixture");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(path, "{ broken");
        await using (var manager = disk.Manager())
        {
            await manager.DiscoverAsync();
            Assert.Equal("fallback", await manager.GetSettings("com.example.fixture").GetAsync("draft", "fallback"));
            Assert.Contains(Assert.Single(manager.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.SettingsCorrupt);
            Assert.Equal("{ broken", await File.ReadAllTextAsync(path));
        }
        await File.WriteAllTextAsync(path, "{\"draft\":42,\"good\":\"kept\"}");
        await using var restarted = disk.Manager();
        await restarted.DiscoverAsync();
        var settings = restarted.GetSettings("com.example.fixture");

        Assert.Equal("fallback", await settings.GetAsync("draft", "fallback"));
        Assert.Equal("kept", await settings.GetAsync("good", "missing"));
        Assert.Contains(Assert.Single(restarted.Plugins).Diagnostics, d => d.Code == PluginDiagnosticCode.SettingsCorrupt);
    }

    [Fact]
    public async Task Concurrent_writes_preserve_all_values_and_leave_no_temporary_files()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        await using (var manager = disk.Manager())
        {
            await manager.DiscoverAsync();
            var settings = manager.GetSettings("com.example.fixture");
            await Task.WhenAll(Enumerable.Range(0, 20).Select(i => settings.SetAsync($"value-{i}", i)));
        }
        await using var restarted = disk.Manager();
        await restarted.DiscoverAsync();
        var persisted = restarted.GetSettings("com.example.fixture");
        for (var i = 0; i < 20; i++)
            Assert.Equal(i, await persisted.GetAsync($"value-{i}", -1));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(disk.User, "settings", "com.example.fixture")));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(disk.User, "settings", "com.example.fixture", "settings.json")));
        Assert.Equal(20, json.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task Canceled_writes_preserve_the_previous_json_file()
    {
        using var disk = new TestDirectory();
        disk.Package("com.example.fixture");
        await using var manager = disk.Manager();
        await manager.DiscoverAsync();
        var settings = manager.GetSettings("com.example.fixture");
        await settings.SetAsync("draft", "kept");
        var path = Path.Combine(disk.User, "settings", "com.example.fixture", "settings.json");
        var previous = await File.ReadAllTextAsync(path);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => settings.SetAsync("draft", "lost", cancel.Token));
        Assert.Equal(previous, await File.ReadAllTextAsync(path));
    }
}
