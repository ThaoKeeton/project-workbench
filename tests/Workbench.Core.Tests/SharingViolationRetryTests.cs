using System.Diagnostics;
using Workbench.App;
using Workbench.Core;
using Workbench.Core.Projects;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class SharingViolationRetryTests
{
    private const int Sharing = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);
    private const int FileExists = unchecked((int)0x80070050);
    private const int WrappedIo = unchecked((int)0x80131620);

    [Fact]
    public async Task Sharing_violation_is_retried_then_returned()
    {
        var calls = 0;
        var value = await SharingViolationRetry.RunAsync(() =>
        {
            calls++;
            return calls < 3 ? Task.FromException<int>(SharingViolation()) : Task.FromResult(9);
        }, CancellationToken.None, attempts: 8, delayMilliseconds: 0);

        Assert.Equal(9, value);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Exhausted_sharing_violation_is_the_same_exception()
    {
        var calls = 0;
        var error = SharingViolation();
        var thrown = await Assert.ThrowsAsync<IOException>(() => SharingViolationRetry.RunAsync<int>(() =>
        {
            calls++;
            return Task.FromException<int>(error);
        }, CancellationToken.None, attempts: SharingViolationRetry.MaxAttempts, delayMilliseconds: 0));

        Assert.Same(error, thrown);
        Assert.Equal(SharingViolationRetry.MaxAttempts, calls);
    }

    [Theory]
    [InlineData(LockViolation)]
    [InlineData(FileExists)]
    [InlineData(WrappedIo)]
    public async Task Other_io_failures_are_not_retried(int hresult)
    {
        var calls = 0;
        var error = new IOException("synthetic", hresult);
        var thrown = await Assert.ThrowsAsync<IOException>(() => SharingViolationRetry.RunAsync<int>(() =>
        {
            calls++;
            return Task.FromException<int>(error);
        }, CancellationToken.None, attempts: 4, delayMilliseconds: 0));

        Assert.Same(error, thrown);
        Assert.Equal(1, calls);
        Assert.False(SharingViolationRetry.IsSharingViolation(new UnauthorizedAccessException("synthetic")));
    }

    [Fact]
    public async Task Cancellation_during_the_backoff_is_not_a_sharing_failure()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SharingViolationRetry.RunAsync(
            () => Task.FromException<int>(SharingViolation()), cts.Token, attempts: 4, delayMilliseconds: 10));
    }

    [Fact]
    public void Wrapped_host_lease_is_not_a_data_sharing_violation()
    {
        var wrapped = new UserStoreInUseException(SharingViolation());
        Assert.False(SharingViolationRetry.IsSharingViolation(wrapped));
        Assert.True(SharingViolationRetry.IsSharingViolation(wrapped.InnerException!));
        Assert.NotEqual(32, wrapped.HResult & 0xFFFF);
    }

    [Fact]
    public void Second_host_lease_is_refused_without_the_data_retry_budget()
    {
        using var disk = new TestDirectory();
        using var owner = HostRuntime.AcquireLease(disk.User);
        var started = Stopwatch.StartNew();
        var error = Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(disk.User); });
        Assert.True(started.Elapsed < TimeSpan.FromMilliseconds(200));
        if (error is UserStoreInUseException wrapped)
            Assert.False(SharingViolationRetry.IsSharingViolation(wrapped));
    }

    [Fact]
    public async Task Locked_data_read_retries_only_a_win32_sharing_violation()
    {
        using var disk = new TestDirectory();
        Directory.CreateDirectory(disk.User);
        var path = Path.Combine(disk.User, "record.json");
        var original = new byte[] { 4, 5, 6 };
        await File.WriteAllBytesAsync(path, original);
        await using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var started = Stopwatch.StartNew();
        Exception? error = null;
        try { _ = await ProjectWorkspaceStorage.ReadAsync(path, 100, "StoreLimitExceeded", CancellationToken.None); }
        catch (Exception caught) { error = caught; }
        Assert.NotNull(error);
        if (OperatingSystem.IsWindows())
        {
            Assert.True(SharingViolationRetry.IsSharingViolation(error));
            Assert.InRange(started.ElapsedMilliseconds, 100, 2000);
        }
        else
        {
            // Linux reports this collision as EAGAIN, not Win32 32. Do not retry that unrelated errno.
            Assert.False(SharingViolationRetry.IsSharingViolation(error));
            Assert.InRange(started.ElapsedMilliseconds, 0, 100);
        }
        await held.DisposeAsync();
        Assert.Equal(original, await ProjectWorkspaceStorage.ReadAsync(path, 100, "StoreLimitExceeded", CancellationToken.None));
    }

    [Fact]
    public async Task Locked_destination_is_replaced_only_when_the_platform_allows_the_move()
    {
        using var disk = new TestDirectory();
        Directory.CreateDirectory(disk.User);
        var path = Path.Combine(disk.User, "record.json");
        var original = new byte[] { 7, 7 };
        await File.WriteAllBytesAsync(path, original);
        await using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Exception? error = null;
        try { await ProjectWorkspaceStorage.AtomicWriteAsync(path, [1, 2], overwrite: true, CancellationToken.None); }
        catch (Exception caught) { error = caught; }
        await held.DisposeAsync();
        if (OperatingSystem.IsWindows())
        {
            // File.Move against an open destination can fail immediately with an IO error
            // whose code is not ERROR_SHARING_VIOLATION. The replacement must still not commit.
            Assert.NotNull(error);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        Assert.DoesNotContain(Directory.EnumerateFiles(disk.User), candidate => candidate.EndsWith(".tmp", StringComparison.Ordinal));
    }

    private static IOException SharingViolation() => new("synthetic", Sharing);
}
