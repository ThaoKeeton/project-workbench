using System.Text.Json;
using Workbench.App;
using Workbench.Core.Projects;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class ProjectDocumentSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_document_close_refuses_flush_or_recovery_behind_its_barrier(bool recovery)
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        await store.WriteAsync("before", "missing");
        var editor = new ProjectDocumentSession(store);
        await editor.InitializeAsync();
        using var held = new FileStream(Path.Combine(disk.User, ".workbench-project", "context.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        editor.Edit("saved before retirement");
        var close = editor.DisposeAsync().AsTask();
        Task? late = null;
        try
        {
            Assert.False(close.IsCompleted);
            Assert.True(editor.IsDirty);
            Assert.Throws<InvalidOperationException>(() =>
            {
                late = recovery ? editor.PreserveConflictAndReloadAsync() : editor.FlushAsync();
            });
        }
        finally
        {
            held.Dispose();
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            if (late is not null) await late.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(editor.HasPendingWork);
        Assert.Equal("saved before retirement", (await store.ReadAsync()).Text);
        Assert.Throws<ObjectDisposedException>(() => { _ = editor.PreserveConflictAndReloadAsync(); });
        Assert.True(editor.FlushAsync().IsCompletedSuccessfully);
        Assert.False(editor.HasPendingWork);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settled_failed_document_close_allows_invalid_buffer_correction_and_owned_autosave(bool invalidUnicode)
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        await store.WriteAsync("before", "missing");
        var editor = new ProjectDocumentSession(store);
        await editor.InitializeAsync();
        editor.Edit(invalidUnicode ? "invalid \uD800 buffer" : new string('x', 1024 * 1024 + 1));
        var failure = await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => editor.DisposeAsync().AsTask());
        Assert.Equal(invalidUnicode ? "InvalidContext" : "ContextLimitExceeded", failure.ErrorCode);
        Assert.True(editor.IsDirty);
        Assert.True(editor.IsEditable);
        using var held = new FileStream(Path.Combine(disk.User, ".workbench-project", "context.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        editor.Edit("corrected after failed close");
        var retry = editor.DisposeAsync().AsTask();
        Assert.False(editor.IsEditable);
        Assert.Throws<InvalidOperationException>(() => editor.Edit("too late"));
        Assert.Throws<InvalidOperationException>(() => { _ = editor.FlushAsync(); });
        Assert.Throws<InvalidOperationException>(() => { _ = editor.PreserveConflictAndReloadAsync(); });
        held.Dispose();
        await retry.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("corrected after failed close", (await store.ReadAsync()).Text);
        Assert.False(editor.IsDirty);
        Assert.False(editor.HasPendingWork);
    }

    [Fact]
    public async Task Missing_document_is_editable_and_autosaves_latest_buffer_without_an_activation_step()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        await using var editor = new ProjectDocumentSession(store);
        await editor.InitializeAsync();
        Assert.True(editor.IsEditable);
        Assert.False(Directory.Exists(disk.User));
        editor.Edit("first"); editor.Edit("latest");
        // Reload is queued after autosave; it does not activate or write the current document.
        await editor.PreserveConflictAndReloadAsync();
        Assert.Equal("latest", editor.Text);
        await editor.FlushAsync();
        Assert.Equal("latest", (await store.ReadAsync()).Text);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Corrected_failed_buffer_autosaves_again_without_a_manual_flush()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        await using var editor = new ProjectDocumentSession(store);
        await editor.InitializeAsync();
        editor.Edit(new string('x', 1024 * 1024 + 1));
        var failure = await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => editor.FlushAsync());
        Assert.Equal("ContextLimitExceeded", failure.ErrorCode);
        Assert.True(editor.IsEditable);
        editor.Edit("corrected buffer");
        await editor.PreserveConflictAndReloadAsync();
        Assert.Equal("corrected buffer", (await store.ReadAsync()).Text);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Malformed_document_stays_uneditable_and_original_bytes_are_retained()
    {
        using var disk = new TestDirectory();
        var directory = Path.Combine(disk.User, ".workbench-project");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "context.json");
        await File.WriteAllTextAsync(path, "{broken}");
        await using var editor = new ProjectDocumentSession(new(disk.User, "default"));
        await editor.InitializeAsync();
        Assert.Equal("InvalidContext", editor.ErrorCode);
        Assert.False(editor.IsEditable);
        Assert.Throws<InvalidOperationException>(() => editor.Edit("lost"));
        Assert.Equal("{broken}", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Conflict_copy_is_durable_before_reload_and_keeps_both_versions()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        var initial = await store.WriteAsync("before", "missing");
        await using var editor = new ProjectDocumentSession(store);
        await editor.InitializeAsync();
        await store.WriteAsync("outside", initial.Revision);
        editor.Edit("my dirty copy");
        var failure = await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => editor.FlushAsync());
        Assert.Equal("RevisionConflict", failure.ErrorCode);
        Assert.Equal("my dirty copy", editor.Text);
        Assert.True(editor.IsDirty);
        await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => editor.DisposeAsync().AsTask());
        await editor.PreserveConflictAndReloadAsync();
        var retained = Assert.Single(Directory.EnumerateFiles(Path.Combine(disk.User, ".workbench-project", "context-conflicts"), "*.json"));
        using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(retained));
        Assert.Equal("my dirty copy", saved.RootElement.GetProperty("text").GetString());
        Assert.Equal(initial.Revision, saved.RootElement.GetProperty("baseRevision").GetString());
        Assert.Equal("outside", editor.Text);
        Assert.False(editor.IsDirty);
        Assert.Equal("outside", (await store.ReadAsync()).Text);
    }

    [Fact]
    public async Task Failed_conflict_copy_never_reloads_or_discards_dirty_text()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        var initial = await store.WriteAsync("before", "missing");
        var editor = new ProjectDocumentSession(store);
        await editor.InitializeAsync();
        await store.WriteAsync("outside", initial.Revision);
        editor.Edit("keep this");
        await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => editor.FlushAsync());
        var copyDirectory = Path.Combine(disk.User, ".workbench-project", "context-conflicts");
        await File.WriteAllTextAsync(copyDirectory, "synthetic obstruction");
        await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => editor.PreserveConflictAndReloadAsync());
        Assert.Equal("keep this", editor.Text);
        Assert.True(editor.IsDirty);
        Assert.Equal("outside", (await store.ReadAsync()).Text);
        File.Delete(copyDirectory);
        await editor.PreserveConflictAndReloadAsync();
        await editor.DisposeAsync();
    }

    [Fact]
    public async Task Store_conflict_receipts_validate_tokens_caps_and_never_replace_current_bytes()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        var current = await store.WriteAsync("current document", "missing");
        var path = Path.Combine(disk.User, ".workbench-project", "context.json");
        var original = await File.ReadAllBytesAsync(path);
        foreach (var invalid in new string?[] { null, "", "NOT_A_REVISION", new('A', 64) })
            Assert.Equal("InvalidContext", (await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => store.PreserveConflictAsync("dirty", invalid!))).ErrorCode);
        Assert.Equal("ContextLimitExceeded", (await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => store.PreserveConflictAsync(new string('x', 1024 * 1024 + 1), current.Revision))).ErrorCode);
        var first = await store.PreserveConflictAsync("dirty 1", current.Revision);
        var second = await store.PreserveConflictAsync("dirty 2", "missing");
        Assert.NotEqual(first, second);
        Assert.Equal(Path.Combine(disk.User, ".workbench-project", "context-conflicts"), Path.GetDirectoryName(first));
        Assert.Matches("^[a-f0-9]{32}\\.json$", Path.GetFileName(first));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        using var receipt = JsonDocument.Parse(await File.ReadAllBytesAsync(first));
        Assert.Equal(1, receipt.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("default", receipt.RootElement.GetProperty("projectId").GetString());
        Assert.Equal("dirty 1", receipt.RootElement.GetProperty("text").GetString());
        Assert.Equal(TimeSpan.Zero, receipt.RootElement.GetProperty("savedUtc").GetDateTimeOffset().Offset);
    }

    [Fact]
    public async Task Reload_failure_keeps_durable_copy_and_the_dirty_model()
    {
        using var disk = new TestDirectory();
        var store = new ProjectContextDocumentStore(disk.User, "default");
        var initial = await store.WriteAsync("before", "missing");
        var editor = new ProjectDocumentSession(store);
        await editor.InitializeAsync();
        var outside = await store.WriteAsync("outside", initial.Revision);
        editor.Edit("retained dirty text");
        await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => editor.FlushAsync());
        var copyDirectory = Path.Combine(disk.User, ".workbench-project", "context-conflicts");
        Directory.CreateDirectory(copyDirectory);
        var copied = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(copyDirectory, "*.json");
        watcher.Created += (_, e) => copied.TrySetResult(e.FullPath);
        watcher.Renamed += (_, e) => copied.TrySetResult(e.FullPath);
        watcher.EnableRaisingEvents = true;
        using var held = new FileStream(Path.Combine(disk.User, ".workbench-project", "context.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var reload = editor.PreserveConflictAndReloadAsync();
        var retained = await copied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(reload.IsCompleted);
        var documentPath = Path.Combine(disk.User, ".workbench-project", "context.json");
        var valid = await File.ReadAllBytesAsync(documentPath);
        await File.WriteAllTextAsync(documentPath, "{broken after durable copy}");
        held.Dispose();
        Assert.Equal("InvalidContext", (await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(() => reload)).ErrorCode);
        Assert.True(editor.IsDirty);
        Assert.Equal("retained dirty text", editor.Text);
        Assert.False(editor.IsEditable);
        using var receipt = JsonDocument.Parse(await File.ReadAllBytesAsync(retained));
        Assert.Equal("retained dirty text", receipt.RootElement.GetProperty("text").GetString());
        await File.WriteAllBytesAsync(documentPath, valid);
        Assert.Equal(outside.Revision, (await store.ReadAsync()).Revision);
        await editor.PreserveConflictAndReloadAsync();
        await editor.DisposeAsync();
    }
}
