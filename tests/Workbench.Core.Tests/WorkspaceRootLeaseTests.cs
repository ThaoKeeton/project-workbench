using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class WorkspaceRootLeaseTests
{
    [Fact]
    public void BorrowDoesNotReleaseBaseAndAllRuntimesRetireBeforeOwner()
    {
        using var disk = new TestDirectory();
        using var owner = WorkspaceRootLease.Acquire(disk.User);
        using var borrow = owner.Borrow(Path.Combine(disk.User, "."));
        Assert.Throws<InvalidOperationException>(() => owner.Dispose());
        borrow.Dispose();
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(disk.User); });
        owner.Dispose();
        using var next = HostRuntime.AcquireLease(disk.User);
    }
    [Fact]
    public void Borrow_refuses_mismatched_and_closed_owners()
    {
        using var disk = new TestDirectory();
        using var owner = WorkspaceRootLease.Acquire(disk.User);
        Assert.Throws<ArgumentException>(() => owner.Borrow(disk.Root));
        owner.Dispose();
        Assert.Throws<ObjectDisposedException>(() => owner.Borrow(disk.User));
    }

    [Fact]
    public async Task Default_borrow_and_secondary_runtime_both_pin_owner_until_actual_disposal()
    {
        using var disk = new TestDirectory();
        using var owner = WorkspaceRootLease.Acquire(disk.User);
        var first = new HostRuntime(new() { UserRoot = disk.User, PluginsRoot = disk.Plugins }, owner);
        var otherRoot = Path.Combine(disk.Root, "secondary");
        var second = new HostRuntime(new() { UserRoot = otherRoot, PluginsRoot = Path.Combine(otherRoot, "Plugins") }, owner);
        Assert.Throws<InvalidOperationException>(() => owner.Dispose());
        await first.DisposeAsync();
        Assert.Throws<InvalidOperationException>(() => owner.Dispose());
        Assert.ThrowsAny<IOException>(() => { using var duplicate = HostRuntime.AcquireLease(disk.User); });
        await second.DisposeAsync();
        owner.Dispose();
        using var nextBase = HostRuntime.AcquireLease(disk.User);
        using var nextSecondary = HostRuntime.AcquireLease(otherRoot);
    }
}
