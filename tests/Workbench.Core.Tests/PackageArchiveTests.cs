using System.IO.Compression;
using Workbench.Core;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class PackageArchiveTests
{
    // Removing portable name validation, actual byte limits or private-stage
    // revalidation would expose an unsafe or incomplete package through these APIs.
    [Theory]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.NoCompression)]
    public async Task Imports_a_root_zip_without_execution_or_approval(CompressionLevel compression)
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.archive", external: true);
        File.WriteAllText(Path.Combine(source, "说明.txt"), "synthetic UTF-8 asset");
        var zip = Archive(disk, source, compression);
        using var marker = disk.WatchActivation();
        var store = Store(disk);
        var result = await store.InstallAsync(zip);
        Assert.True(result.Succeeded, result.Message);
        await using var manager = disk.Manager();
        var plugin = Assert.Single(await manager.DiscoverAsync());
        Assert.Equal("com.example.archive", plugin.Manifest!.Id);
        Assert.False(plugin.IsTrusted);
        Assert.False(plugin.DesiredEnabled);
        Assert.Equal("synthetic UTF-8 asset", File.ReadAllText(Path.Combine(plugin.PackagePath, "说明.txt")));
        Assert.False(File.Exists(marker.Path));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("dir\\escape.txt")]
    [InlineData("file:stream")]
    [InlineData("CON.txt")]
    [InlineData("dir/LPT1")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public async Task Rejects_unsafe_zip_targets(string entry)
    {
        using var disk = new TestDirectory();
        var zip = Archive(disk, disk.Package("com.example.archive", external: true));
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("unsafe");
        Assert.False((await Store(disk).InstallAsync(zip)).Succeeded);
        Assert.False(Directory.Exists(Path.Combine(disk.Plugins, "com.example.archive")));
        Assert.False(File.Exists(Path.Combine(disk.Root, "escape.txt")));
    }

    [Theory]
    [InlineData("plugin.json", false)]
    [InlineData("PLUGIN.JSON", false)]
    [InlineData("assets", false)]
    [InlineData("ASSETS/other.txt", false)]
    [InlineData("link", true)]
    public async Task Rejects_duplicate_colliding_and_link_entries(string name, bool link)
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.archive", external: true);
        Directory.CreateDirectory(Path.Combine(source, "assets"));
        File.WriteAllText(Path.Combine(source, "assets", "file.txt"), "asset");
        var zip = Archive(disk, source);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            var entry = archive.CreateEntry(name);
            if (link) entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("target");
        }
        Assert.False((await Store(disk).InstallAsync(zip)).Succeeded);
    }

    [Theory]
    [InlineData(false, "files")]
    [InlineData(true, "files")]
    [InlineData(false, "file")]
    [InlineData(true, "file")]
    [InlineData(false, "total")]
    [InlineData(true, "total")]
    public async Task Enforces_limits_on_folder_and_zip_copies(bool zipped, string limit)
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.archive", external: true);
        if (limit == "files")
            for (var i = Directory.GetFiles(source).Length; i <= 1000; i++) File.WriteAllText(Path.Combine(source, $"f{i}"), "x");
        else if (limit == "file")
            using (var file = File.Create(Path.Combine(source, "large"))) file.SetLength(50L * 1024 * 1024 + 1);
        else
            for (var i = 0; i < 3; i++)
                using (var file = File.Create(Path.Combine(source, $"large{i}"))) file.SetLength(35L * 1024 * 1024);
        var input = zipped ? Archive(disk, source) : source;
        var result = await Store(disk).InstallAsync(input);
        Assert.False(result.Succeeded);
        Assert.False(Directory.Exists(Path.Combine(disk.Plugins, "com.example.archive")));
    }

    [Fact]
    public async Task Accepts_exact_file_count_and_per_file_limit()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.archive", external: true);
        using (var file = File.Create(Path.Combine(source, "large"))) file.SetLength(50L * 1024 * 1024);
        for (var i = Directory.GetFiles(source).Length; i < 1000; i++) File.WriteAllText(Path.Combine(source, $"f{i}"), "x");
        var result = await Store(disk).InstallAsync(Archive(disk, source));
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1000, Directory.GetFiles(Path.Combine(disk.Plugins, "com.example.archive"), "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Accepts_exact_total_uncompressed_byte_limit()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.archive", external: true);
        var existing = Directory.GetFiles(source).Sum(path => new FileInfo(path).Length);
        using (var file = File.Create(Path.Combine(source, "part1"))) file.SetLength(50L * 1024 * 1024);
        using (var file = File.Create(Path.Combine(source, "part2"))) file.SetLength(50L * 1024 * 1024 - existing);
        Assert.True((await Store(disk).InstallAsync(source)).Succeeded);
        Assert.Equal(100L * 1024 * 1024, Directory.GetFiles(Path.Combine(disk.Plugins, "com.example.archive")).Sum(path => new FileInfo(path).Length));
    }

    [Fact]
    public async Task Rejects_zip64_even_when_only_the_uncompressed_size_uses_an_extended_field()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.archive", external: true);
        var zip = Archive(disk, source);
        var bytes = File.ReadAllBytes(zip);
        var central = -1;
        for (var offset = 0; offset < bytes.Length - 57; offset++)
            if (BitConverter.ToUInt32(bytes, offset) == 0x02014b50 &&
                System.Text.Encoding.UTF8.GetString(bytes, offset + 46, 11) == "plugin.json") { central = offset; break; }
        Assert.True(central >= 0);
        var length = BitConverter.ToUInt32(bytes, central + 24);
        var extraLength = BitConverter.ToUInt16(bytes, central + 30);
        var extraStart = central + 46 + BitConverter.ToUInt16(bytes, central + 28);
        var extra = new byte[12];
        BitConverter.GetBytes((ushort)1).CopyTo(extra, 0);
        BitConverter.GetBytes((ushort)8).CopyTo(extra, 2);
        BitConverter.GetBytes((ulong)length).CopyTo(extra, 4);
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, central + 24);
        BitConverter.GetBytes((ushort)(extraLength + 12)).CopyTo(bytes, central + 30);
        BitConverter.GetBytes((ushort)45).CopyTo(bytes, central + 6);
        var end = bytes.Length - 22;
        Assert.Equal(0x06054b50U, BitConverter.ToUInt32(bytes, end));
        BitConverter.GetBytes(BitConverter.ToUInt32(bytes, end + 12) + 12).CopyTo(bytes, end + 12);
        File.WriteAllBytes(zip, [.. bytes[..extraStart], .. extra, .. bytes[extraStart..]]);
        // The ordinary ZIP reader accepts the valid extended size; our supported
        // subset must reject it rather than accidentally accepting part of ZIP64.
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            Assert.Equal(length, (uint)archive.GetEntry("plugin.json")!.Length);
        Assert.False((await Store(disk).InstallAsync(zip)).Succeeded);
    }

    [Fact]
    public async Task Counts_actual_inflated_bytes_when_zip_metadata_understates_size()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.archive", external: true);
        using (var file = File.Create(Path.Combine(source, "large.bin"))) file.SetLength(50L * 1024 * 1024 + 1);
        var zip = Archive(disk, source);
        var bytes = File.ReadAllBytes(zip);
        // Hand-edit only the selected entry's local and central uncompressed
        // sizes. Deflated payload and CRC retain the real oversized content.
        var edits = 0;
        for (var offset = 0; offset < bytes.Length - 46; offset++)
        {
            var signature = BitConverter.ToUInt32(bytes, offset);
            var header = signature == 0x04034b50 ? 30 : signature == 0x02014b50 ? 46 : 0;
            if (header == 0 || offset + header + 9 > bytes.Length ||
                System.Text.Encoding.UTF8.GetString(bytes, offset + header, 9) != "large.bin") continue;
            BitConverter.GetBytes(1U).CopyTo(bytes, offset + (header == 30 ? 22 : 24));
            edits++;
        }
        Assert.Equal(2, edits);
        File.WriteAllBytes(zip, bytes);
        Assert.False((await Store(disk).InstallAsync(zip)).Succeeded);
        Assert.False(Directory.Exists(Path.Combine(disk.Plugins, "com.example.archive")));
    }

    [Fact]
    public async Task Rejects_source_tree_changes_during_a_real_folder_copy()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.archive", external: true);
        for (var i = 0; i < 900; i++) File.WriteAllText(Path.Combine(source, $"asset{i:D4}"), "original");
        var sourceCount = Directory.GetFiles(source).Length;
        var paused = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var install = ImportWithCheckpointAsync(disk, source, paused, release);
        try
        {
            var first = await Task.WhenAny(paused.Task, install).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Same(paused.Task, first);
            Assert.InRange(await paused.Task, 1, sourceCount - 1);
            Assert.False(install.IsCompleted, "The real copy must remain paused while its source changes.");
            File.WriteAllText(Path.Combine(source, "added-during-copy"), "changed source");
        }
        finally { release.Set(); }
        Assert.False((await install).Succeeded);
        Assert.False(Directory.Exists(Path.Combine(disk.Plugins, "com.example.archive")));
    }

    [Fact]
    public async Task Observing_a_staged_file_does_not_prevent_a_valid_import_from_finishing_before_later_source_changes()
    {
        using var disk = new TestDirectory();
        var source = disk.Package("com.example.archive", external: true);
        for (var i = 0; i < 900; i++) File.WriteAllText(Path.Combine(source, $"asset{i:D4}"), "original");
        var paused = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var install = ImportWithCheckpointAsync(disk, source, paused, release);
        try
        {
            Assert.Same(paused.Task, await Task.WhenAny(paused.Task, install).WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.InRange(await paused.Task, 1, Directory.GetFiles(source).Length - 1);
            Assert.False(install.IsCompleted);
        }
        finally { release.Set(); }
        // This is a controlled legal interleaving of the old polling fixture:
        // it saw a stage file, but its writer did not run until import completed.
        Assert.True((await install).Succeeded);
        File.WriteAllText(Path.Combine(source, "added-after-copy"), "later source change");
        var installed = Path.Combine(disk.Plugins, "com.example.archive");
        Assert.False(File.Exists(Path.Combine(installed, "added-after-copy")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(installed, "asset0000")));
    }

    private static Task<PluginOperationResult> ImportWithCheckpointAsync(TestDirectory disk, string source,
        TaskCompletionSource<int> paused, ManualResetEventSlim release)
    {
        var sourceCount = Directory.GetFiles(source).Length;
        var imports = Path.Combine(disk.Plugins, ".workbench-packages", "imports");
        return TestDispatcher.RunAsync(() => Store(disk).InstallAsync(source), () =>
        {
            if (paused.Task.IsCompleted || !Directory.Exists(imports)) return;
            var copied = Directory.EnumerateFiles(imports, "*", SearchOption.AllDirectories).Count();
            if (copied == 0 || copied >= sourceCount) return;
            // Hold the actual importer dispatcher with only part of its original
            // file list staged. It cannot finish copying/revalidate/publish until released.
            paused.TrySetResult(copied);
            Assert.True(release.Wait(TimeSpan.FromSeconds(20)), "The test did not release its copy checkpoint.");
        });
    }

    internal static PluginPackageStore Store(TestDirectory disk, params string[] bundled) => new(new PluginManagerOptions
    {
        PluginsRoot = disk.Plugins,
        UserRoot = disk.User,
        AdditionalDiscoveryRoots = bundled
    });

    internal static string Archive(TestDirectory disk, string source, CompressionLevel compression = CompressionLevel.Optimal)
    {
        var path = Path.Combine(disk.Root, $"{Guid.NewGuid():N}.zip");
        ZipFile.CreateFromDirectory(source, path, compression, includeBaseDirectory: false);
        return path;
    }
}
