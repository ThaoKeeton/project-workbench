using System.Text.Json;
using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class HostThemeSessionTests
{
    [Fact]
    public async Task Failed_autosave_retains_choice_and_dirty_intent_until_actual_retry_writes_it()
    {
        using var disk = new TestDirectory();
        var session = new HostThemeSession(disk.User);
        session.Load();
        session.Select(2);
        var obstruction = Path.Combine(disk.User, "host-settings.json.tmp");
        Directory.CreateDirectory(obstruction);
        var failure = await Record.ExceptionAsync(() => session.FlushAsync());
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.True(session.IsDirty);
        Assert.Equal(2, session.SelectedTheme);
        Assert.NotNull(session.ErrorCode);
        Assert.False(File.Exists(Path.Combine(disk.User, "host-settings.json")));
        Directory.Delete(obstruction);
        await session.FlushAsync();
        Assert.False(session.IsDirty);
        Assert.Null(session.ErrorCode);
        using var saved = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(disk.User, "host-settings.json")));
        Assert.Equal(2, saved.RootElement.GetProperty("Theme").GetInt32());
        var reopened = new HostThemeSession(disk.User); reopened.Load();
        Assert.Equal(2, reopened.SelectedTheme);
    }

    [Theory]
    [InlineData("{synthetic-damaged-theme")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"Theme\":\"private\"}")]
    [InlineData("{\"Theme\":2,\"Future\":\"synthetic\"}")]
    [InlineData("{\"Theme\":2,\"Theme\":0}")]
    public async Task Damaged_original_is_preserved_once_before_selected_system_theme_replaces_it(string damaged)
    {
        using var disk = new TestDirectory();
        Directory.CreateDirectory(disk.User);
        var path = Path.Combine(disk.User, "host-settings.json");
        await File.WriteAllTextAsync(path, damaged);
        var original = await File.ReadAllBytesAsync(path);
        var session = new HostThemeSession(disk.User); session.Load();
        Assert.True(session.NeedsRepair);
        Assert.Equal(0, session.SelectedTheme);
        await session.FlushAsync(); // Untouched fallback is never an implicit replacement on close/switch.
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(disk.User, "host-settings.json.corrupt-*.bak"));
        session.Select(0);
        await session.FlushAsync();
        Assert.False(session.NeedsRepair);
        Assert.Equal(original, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(disk.User, "host-settings.json.corrupt-*.bak"))));
        session.Select(2); await session.FlushAsync();
        Assert.Single(Directory.GetFiles(disk.User, "host-settings.json.corrupt-*.bak"));
    }

    [Fact]
    public async Task Missing_second_root_does_not_adopt_first_root_theme_or_repair_state()
    {
        using var disk = new TestDirectory();
        var first = new HostThemeSession(disk.User); first.Load(); first.Select(2); await first.FlushAsync();
        var secondRoot = Path.Combine(disk.User, "second");
        var second = new HostThemeSession(secondRoot); second.Load();
        Assert.Equal(0, second.SelectedTheme);
        Assert.False(second.NeedsRepair);
        Assert.False(second.IsDirty);
        second.Select(1); await second.FlushAsync();
        var firstAgain = new HostThemeSession(disk.User); firstAgain.Load();
        Assert.Equal(2, firstAgain.SelectedTheme);
    }
}
