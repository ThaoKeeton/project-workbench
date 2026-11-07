using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workbench.Core.Projects;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class ProjectContextDocumentStoreTests
{
    [Fact]
    public async Task ContextMissingReadDoesNotCreateFiles()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");

        var document = await store.ReadAsync();

        Assert.Equal(new ProjectContextDocument("missing", "", true), document);
        Assert.False(Directory.Exists(disk.User));
    }

    [Fact]
    public async Task ContextProjectBindingAndUnicodeLimitsRoundTrip()
    {
        using var disk = new TestDirectory();
        const string id = "1234567890abcdef1234567890abcdef";
        var store = new ProjectContextDocumentStore(disk.User, id);
        var text = "你好 🧪\r\n" + new string('a', 1024 * 1024 - Encoding.UTF8.GetByteCount("你好 🧪\r\n"));

        var written = await store.WriteAsync(text, "missing");

        Assert.Equal(text, written.Text);
        Assert.False(written.IsMissing);
        var bytes = await File.ReadAllBytesAsync(ContextPath(disk.User));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), written.Revision);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(id, json.RootElement.GetProperty("projectId").GetString());
        Assert.True(json.RootElement.GetProperty("updatedUtc").TryGetDateTimeOffset(out _));
        Assert.Equal(written, await new ProjectContextDocumentStore(disk.User, id).ReadAsync());
        var second = await store.WriteAsync("later", written.Revision);
        Assert.Equal("later", (await store.ReadAsync()).Text);
        Assert.NotEqual(written.Revision, second.Revision);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(disk.User, ".workbench-project"), "*.tmp"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(disk.User, ".workbench-project"), "*.bak"));
    }

    [Fact]
    public async Task ContextConflictFailedWriteAndInvalidSchemaPreserveOriginal()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        var first = await store.WriteAsync("first", "missing");
        var latest = await store.WriteAsync("latest", first.Revision);
        var bytes = await File.ReadAllBytesAsync(ContextPath(disk.User));
        Assert.Equal("latest", (await store.ReadAsync()).Text);
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("RevisionConflict", () => store.WriteAsync("editor buffer", first.Revision));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ContextPath(disk.User)));
        var leasePath = Path.Combine(disk.User, ".workbench-project", "context.lock");
        using (var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync("editor buffer", latest.Revision, cancellation.Token));
        }
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ContextPath(disk.User)));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync("editor buffer", latest.Revision, canceled.Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ContextPath(disk.User)));
    }

    [Fact]
    public async Task ActualContextIoFailurePreservesDocumentAndLeavesNoOwnedStaging()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        var valid = await store.WriteAsync("durable original", "missing");
        var path = ContextPath(disk.User);
        var original = await File.ReadAllBytesAsync(path);
        var directory = Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows())
        {
            using var retained = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("StoreIoFailure", () => store.WriteAsync("retained editor buffer", valid.Revision));
        }
        else
        {
            var mode = File.GetUnixFileMode(directory);
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try { await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("StoreIoFailure", () => store.WriteAsync("retained editor buffer", valid.Revision)); }
            finally { File.SetUnixFileMode(directory, mode); }
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal("durable original", (await store.ReadAsync()).Text);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"schemaVersion\":2,\"projectId\":\"default\",\"text\":\"private\",\"updatedUtc\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{\"schemaVersion\":1,\"projectId\":\"11111111111111111111111111111111\",\"text\":\"private\",\"updatedUtc\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{\"schemaVersion\":1,\"projectId\":\"default\",\"text\":\"private\",\"updatedUtc\":\"2026-01-01T00:00:00Z\",\"unknown\":true}")]
    [InlineData("{\"schemaVersion\":1,\"projectId\":\"default\",\"text\":\"private\",\"text\":\"duplicate\",\"updatedUtc\":\"2026-01-01T00:00:00Z\"}")]
    [InlineData("{\"schemaVersion\":1,\"projectId\":\"default\",\"text\":\"private\",\"updatedUtc\":\"invalid\"}")]
    [InlineData("{\"schemaVersion\":1,\"projectId\":\"default\",\"text\":\"\\uD800\",\"updatedUtc\":\"2026-01-01T00:00:00Z\"}")]
    public async Task InvalidContextIsNeverAnEmptyEditableDefault(string invalid)
    {
        using var disk = new TestDirectory();
        var path = ContextPath(disk.User);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ValidContext);
        var store = new ProjectContextDocumentStore(disk.User, "default");
        Assert.Equal("independent valid fixture", (await store.ReadAsync()).Text);
        await File.WriteAllTextAsync(path, invalid);
        var bytes = await File.ReadAllBytesAsync(path);
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("InvalidContext", () => store.ReadAsync());
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("InvalidContext", () => store.WriteAsync("buffer", Convert.ToHexStringLower(SHA256.HashData(bytes))));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ContextUtf8AndEncodedInputLimitsPreserveExistingDocument()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        var valid = await store.WriteAsync("valid", "missing");
        var bytes = await File.ReadAllBytesAsync(ContextPath(disk.User));
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("ContextLimitExceeded", () => store.WriteAsync(new string('a', 1024 * 1024 + 1), valid.Revision));
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("ContextLimitExceeded", () => store.WriteAsync(new string('界', 349526), valid.Revision));
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("InvalidContext", () => store.WriteAsync("broken" + (char)0xd800, valid.Revision));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ContextPath(disk.User)));
        var path = ContextPath(disk.User);
        await File.WriteAllTextAsync(path, new string(' ', 8 * 1024 * 1024) + ValidContext);
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("ContextLimitExceeded", () => store.ReadAsync());
        Assert.True(new FileInfo(path).Length > 8 * 1024 * 1024);
    }

    [Fact]
    public async Task ContextLinksAndDirectoryTargetsCannotEscapeOrBlockReads()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        var valid = await store.WriteAsync("valid", "missing");
        Assert.Equal("valid", (await store.ReadAsync()).Text);
        var path = ContextPath(disk.User);
        var outside = Path.Combine(disk.Root, "outside.json");
        await File.WriteAllTextAsync(outside, "private outside bytes");
        File.Delete(path);
        File.CreateSymbolicLink(path, outside);
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("UnsafePath", () => store.ReadAsync());
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("UnsafePath", () => store.WriteAsync("escape", valid.Revision));
        Assert.Equal("private outside bytes", await File.ReadAllTextAsync(outside));
        File.Delete(path);
        Directory.CreateDirectory(path);
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("UnsafePath", () => store.ReadAsync());
        await ProjectWorkspaceCatalogTests.ThrowsCodeAsync("UnsafePath", () => store.WriteAsync("escape", valid.Revision));
    }

    [Fact]
    public async Task CooperatingContextInstancesCannotOverwriteAConcurrentEdit()
    {
        using var disk = new TestDirectory();
        var first = new ProjectContextDocumentStore(disk.User, "default");
        var second = new ProjectContextDocumentStore(disk.User, "default");
        var initial = await first.WriteAsync("initial", "missing");
        var writes = new[] { first.WriteAsync("one", initial.Revision), second.WriteAsync("two", initial.Revision) };
        try { await Task.WhenAll(writes); } catch (ProjectWorkspaceStoreException) { }
        _ = Assert.Single(writes, t => t.IsCompletedSuccessfully);
        var error = Assert.Single(writes, t => t.IsFaulted).Exception!.InnerException;
        Assert.Equal("RevisionConflict", Assert.IsType<ProjectWorkspaceStoreException>(error).ErrorCode);
        Assert.Contains((await first.ReadAsync()).Text, new[] { "one", "two" });
    }

    internal const string ValidContext = "{\"schemaVersion\":1,\"projectId\":\"default\",\"text\":\"independent valid fixture\",\"updatedUtc\":\"2026-01-01T00:00:00Z\"}";

    internal static string ContextPath(string root) => Path.Combine(root, ".workbench-project", "context.json");
}
