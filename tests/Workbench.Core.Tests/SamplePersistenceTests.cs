using Workbench.Samples;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class SamplePersistenceTests
{
    [Fact]
    public async Task Explicit_save_of_an_unchanged_default_still_creates_the_requested_draft()
    {
        using var disk = new TestDirectory();
        var path = Path.Combine(disk.Root, "default-draft.txt");
        var buffer = new SampleTextBuffer("default draft", text => SampleTextBuffer.WriteFileAsync(path, text));
        buffer.Set("default draft"); // The explicit Save action captures even an unchanged editor.
        await buffer.SaveAsync();
        Assert.True(File.Exists(path));
        Assert.Equal("default draft", await File.ReadAllTextAsync(path));
    }
    [Fact]
    public async Task Buffered_sample_edits_are_saved_to_the_actual_file_when_preparation_flushes()
    {
        using var disk = new TestDirectory();
        var path = Path.Combine(disk.Root, "sample-draft.txt");
        var buffer = new SampleTextBuffer("default demo", text => SampleTextBuffer.WriteFileAsync(path, text));
        buffer.Set("edited nonsecret draft");
        Assert.False(File.Exists(path));
        await buffer.SaveAsync();
        Assert.Equal("edited nonsecret draft", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Failed_sample_save_keeps_the_buffer_available_for_a_real_io_retry()
    {
        using var disk = new TestDirectory();
        var path = Path.Combine(disk.Root, "blocked-draft.txt");
        Directory.CreateDirectory(path);
        var buffer = new SampleTextBuffer("initial", text => SampleTextBuffer.WriteFileAsync(path, text));
        buffer.Set("retained after failed preparation");
        // File.Move over a directory reports Win32 access-denied on Windows; Unix reports IO.
        // Both are the real obstruction, not a product change to normalize exception semantics.
        if (OperatingSystem.IsWindows())
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => buffer.SaveAsync());
        else
            await Assert.ThrowsAnyAsync<IOException>(() => buffer.SaveAsync());
        Assert.Equal("retained after failed preparation", buffer.Text);
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.EnumerateFiles(disk.Root, ".*.tmp"));
        Directory.Delete(path);
        await buffer.SaveAsync();
        Assert.Equal("retained after failed preparation", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.EnumerateFiles(disk.Root, ".*.tmp"));
    }

    [Fact]
    public async Task Edits_accepted_during_a_pending_write_are_flushed_in_order_before_completion()
    {
        using var disk = new TestDirectory();
        var path = Path.Combine(disk.Root, "ordered-draft.txt");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var buffer = new SampleTextBuffer("initial", async text =>
        {
            if (text == "first") { entered.TrySetResult(); await release.Task; }
            await SampleTextBuffer.WriteFileAsync(path, text);
        });
        buffer.Set("first");
        var first = buffer.SaveAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            buffer.Set("latest");
            var second = buffer.SaveAsync();
            Assert.False(second.IsCompleted);
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("latest", await File.ReadAllTextAsync(path));
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Sample_preference_autosave_updates_real_settings_without_a_separate_activation()
    {
        using var disk = new TestDirectory();
        var settings = new Workbench.Core.JsonPluginSettings(Path.Combine(disk.User, "settings", "sample"), _ => { });
        var buffer = new SampleTextBuffer("Project team", text => settings.SetAsync("audience", text));
        buffer.Set("Edited audience");
        buffer.Autosave();
        await RuntimeTest.WaitAsync(async () => await settings.GetAsync("audience", "fallback") == "Edited audience");
        Assert.Equal("Edited audience", await settings.GetAsync("audience", "fallback"));
        Assert.False(Directory.Exists(Path.Combine(disk.User, "secrets")));
    }
}
