using System.Security.Cryptography;
using System.Text.Json;
using Workbench.Core.Projects;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class ProjectWorkspaceCatalogTests
{
    [Fact]
    public async Task LegacyAdoptionPreservesEveryExistingByte()
    {
        using var disk = new TestDirectory();
        var originals = await SeedLegacyAsync(disk.User);
        var catalog = new ProjectWorkspaceCatalog(disk.User);

        var snapshot = await catalog.OpenAsync();

        var project = Assert.Single(snapshot.Projects);
        Assert.Equal(new ProjectWorkspaceInfo("default", "Default", false), project);
        Assert.Equal("default", snapshot.ActiveProjectId);
        Assert.Equal(Path.GetFullPath(disk.User), catalog.GetProjectRoot("default"));
        var bytes = await File.ReadAllBytesAsync(CatalogPath(disk.User));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), snapshot.Revision);
        Assert.Equal(snapshot, await catalog.OpenAsync(), SnapshotComparer.Instance);
        await AssertFilesAsync(originals);
    }

    [Fact]
    public async Task NewProjectDoesNotCopySettingsSecretsTrustOrLedger()
    {
        using var disk = new TestDirectory();
        var originals = await SeedLegacyAsync(disk.User);
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var initial = await catalog.OpenAsync();

        var created = await catalog.CreateAsync("  Research 🧪  ", initial.Revision);

        Assert.Matches("^[0-9a-f]{32}$", created.Project.Id);
        Assert.Equal("Research 🧪", created.Project.Name);
        Assert.Equal(2, created.Snapshot.Projects.Count);
        Assert.Equal("default", created.Snapshot.ActiveProjectId);
        var root = catalog.GetProjectRoot(created.Project.Id);
        Assert.Equal(Path.Combine(disk.User, ".workspaces", "projects", created.Project.Id), root);
        Assert.True(Directory.Exists(root));
        Assert.False(Directory.Exists(Path.Combine(root, "settings")));
        Assert.False(Directory.Exists(Path.Combine(root, "secrets")));
        Assert.False(Directory.Exists(Path.Combine(root, "trust")));
        Assert.False(Directory.Exists(Path.Combine(root, "data")));
        Assert.False(File.Exists(Path.Combine(root, ".workbench-project", "context.json")));
        await AssertFilesAsync(originals);
        Assert.Empty(await catalog.GetRecoverableAsync());
    }

    [Fact]
    public async Task ArchiveRestoreAndSingleModeRetainRootsAndFiles()
    {
        using var disk = new TestDirectory();
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var created = await catalog.CreateAsync("Other", (await catalog.OpenAsync()).Revision);
        var root = catalog.GetProjectRoot(created.Project.Id);
        Assert.True(Directory.Exists(root));
        var payload = Path.Combine(root, "custom-ledger.bin");
        await File.WriteAllBytesAsync(payload, [0, 1, 255, 13]);
        var active = await catalog.SetActiveAsync(created.Project.Id, created.Snapshot.Revision);
        Assert.Equal(created.Project.Id, active.ActiveProjectId);
        var defaultActive = await catalog.SetActiveAsync("default", active.Revision);
        Assert.Equal(2, defaultActive.Projects.Count(p => !p.IsArchived));
        var archived = await catalog.SetArchivedAsync(created.Project.Id, true, defaultActive.Revision);
        Assert.Single(archived.Projects, p => !p.IsArchived);
        Assert.True(Assert.Single(archived.Projects, p => p.Id == created.Project.Id).IsArchived);
        Assert.Equal(new byte[] { 0, 1, 255, 13 }, await File.ReadAllBytesAsync(payload));
        var restored = await catalog.SetArchivedAsync(created.Project.Id, false, archived.Revision);
        var renamed = await catalog.RenameAsync(created.Project.Id, " Renamed ", restored.Revision);
        Assert.Equal("Renamed", Assert.Single(renamed.Projects, p => p.Id == created.Project.Id).Name);
        Assert.False(Assert.Single(renamed.Projects, p => p.Id == created.Project.Id).IsArchived);
        Assert.Equal(new byte[] { 0, 1, 255, 13 }, await File.ReadAllBytesAsync(payload));
        Assert.True(Directory.Exists(disk.User));
        Assert.True(Directory.Exists(root));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{\"schemaVersion\":1,\"generation\":3,\"activeProjectId\":\"default\",\"projects\":[]}")]
    public async Task CorruptUnknownCatalogPreservedAndConditionalRecoveryRetainsOriginal(string damaged)
    {
        using var disk = new TestDirectory();
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var initial = await catalog.OpenAsync();
        var created = await catalog.CreateAsync("Other", initial.Revision);
        var latest = await catalog.RenameAsync(created.Project.Id, "Latest retained", created.Snapshot.Revision);
        await catalog.RenameAsync("default", "New current", latest.Revision);
        var path = CatalogPath(disk.User);
        var history = Path.Combine(disk.User, ".workspaces", "catalog-history");
        Directory.CreateDirectory(history);
        await SeedHistoryAsync(history, 1, "Default");
        var decoy = Path.Combine(history, "99999999999999999999-" + new string('a', 64) + ".json");
        await File.WriteAllTextAsync(decoy, "unknown history retained");
        foreach (var file in Directory.EnumerateFiles(history))
            File.SetLastWriteTimeUtc(file, Path.GetFileName(file).StartsWith("00000000000000000001-", StringComparison.Ordinal)
                ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddYears(-1));
        await File.WriteAllTextAsync(path, damaged);
        var original = await File.ReadAllBytesAsync(path);

        await ThrowsCodeAsync("InvalidCatalog", () => catalog.OpenAsync());
        await ThrowsCodeAsync("InvalidCatalog", () => catalog.CreateAsync("Do not reset", "missing"));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        var recovered = await catalog.RecoverCatalogAsync();

        Assert.Equal(2, recovered.Projects.Count);
        Assert.Equal("Latest retained", Assert.Single(recovered.Projects, p => p.Id == created.Project.Id).Name);
        Assert.Equal("Default", Assert.Single(recovered.Projects, p => p.Id == "default").Name);
        using var restored = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
        Assert.Equal(4, restored.RootElement.GetProperty("generation").GetInt64());
        var retained = Assert.Single(Directory.EnumerateFiles(Path.Combine(disk.User, ".workspaces", "catalog-recovery")));
        Assert.Equal(original, await File.ReadAllBytesAsync(retained));
        Assert.Equal("unknown history retained", await File.ReadAllTextAsync(decoy));
        await ThrowsCodeAsync("RecoveryUnavailable", () => catalog.RecoverCatalogAsync());
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"generation\":3,\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Future\",\"isArchived\":false}]}")]
    [InlineData("{\"schemaVersion\":9223372036854775808,\"generation\":3,\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Future\",\"isArchived\":false}]}")]
    [InlineData("{\"schemaVersion\":\"future\",\"generation\":3,\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Future\",\"isArchived\":false}]}")]
    [InlineData("{\"schemaVersion\":1,\"generation\":3,\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Future\",\"isArchived\":false}],\"future\":\"preserve\"}")]
    public async Task UnknownCatalogNeverDowngradesThroughRecovery(string unknown)
    {
        using var disk = new TestDirectory();
        var history = Path.Combine(disk.User, ".workspaces", "catalog-history");
        Directory.CreateDirectory(history);
        await SeedHistoryAsync(history, 1, "Older compatible format");
        await File.WriteAllTextAsync(CatalogPath(disk.User), ValidCatalog);
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        Assert.Equal("Default", Assert.Single((await catalog.OpenAsync()).Projects).Name);
        await File.WriteAllTextAsync(CatalogPath(disk.User), unknown);
        var bytes = await File.ReadAllBytesAsync(CatalogPath(disk.User));
        await ThrowsCodeAsync("RecoveryUnavailable", () => catalog.RecoverCatalogAsync());
        await ThrowsCodeAsync("InvalidCatalog", () => catalog.OpenAsync());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CatalogPath(disk.User)));
        Assert.False(Directory.Exists(Path.Combine(disk.User, ".workspaces", "catalog-recovery")));
    }

    [Fact]
    public async Task ExplicitRecoveryUsesValidatedGenerationRatherThanMtime()
    {
        using var disk = new TestDirectory();
        var history = Path.Combine(disk.User, ".workspaces", "catalog-history");
        Directory.CreateDirectory(history);
        var older = await SeedHistoryAsync(history, 1, "Older");
        var latest = await SeedHistoryAsync(history, 7, "Highest valid");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(1));
        File.SetLastWriteTimeUtc(latest, DateTime.UtcNow.AddDays(-1));
        var badHash = Path.Combine(history, $"{9L:D20}-" + new string('0', 64) + ".json");
        await File.WriteAllTextAsync(badHash, ValidCatalog.Replace("\"generation\":1", "\"generation\":9", StringComparison.Ordinal));
        var catalog = new ProjectWorkspaceCatalog(disk.User);

        var recovered = await catalog.RecoverCatalogAsync();

        Assert.Equal("Highest valid", Assert.Single(recovered.Projects).Name);
        using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(CatalogPath(disk.User)));
        Assert.Equal(8, json.RootElement.GetProperty("generation").GetInt64());
        Assert.True(File.Exists(badHash));
    }

    [Fact]
    public async Task FailedCreateRetainsRecoverableOwnedRoot()
    {
        using var disk = new TestDirectory();
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var initial = await catalog.OpenAsync();
        var bytes = await File.ReadAllBytesAsync(CatalogPath(disk.User));
        Directory.CreateDirectory(Path.Combine(disk.User, ".workspaces", "catalog-history", $"{1L:D20}-{initial.Revision}.json"));

        await ThrowsCodeAsync("UnsafePath", () => catalog.CreateAsync("Retained draft", initial.Revision));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(CatalogPath(disk.User)));
        var orphan = Assert.Single(await catalog.GetRecoverableAsync());
        Assert.Equal("Retained draft", orphan.Name);
        var payload = Path.Combine(catalog.GetProjectRoot(orphan.Id), "owned-note.bin");
        await File.WriteAllBytesAsync(payload, [1, 2, 3]);
        Directory.Delete(Path.Combine(disk.User, ".workspaces", "catalog-history", $"{1L:D20}-{initial.Revision}.json"));
        var recovered = await catalog.RecoverProjectAsync(orphan.Id, initial.Revision);
        Assert.Contains(recovered.Projects, p => p.Id == orphan.Id);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(payload));
        Assert.Empty(await catalog.GetRecoverableAsync());
    }

    [Fact]
    public async Task IdsLinksAliasesAndStaleRevisionsCannotWriteElsewhere()
    {
        using var disk = new TestDirectory();
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var initial = await catalog.OpenAsync();
        var updated = await catalog.RenameAsync("default", "Valid edit", initial.Revision);
        Assert.Equal("Valid edit", Assert.Single(updated.Projects).Name);
        var bytes = await File.ReadAllBytesAsync(CatalogPath(disk.User));
        foreach (var id in new[] { "../escape", "a/b", "a\\b", "C:escape", "DEFAULT", new string('A', 32), "", ".", "default/../" })
        {
            Assert.Equal("InvalidId", Assert.Throws<ProjectWorkspaceStoreException>(() => catalog.GetProjectRoot(id)).ErrorCode);
            await ThrowsCodeAsync("InvalidId", () => catalog.SetActiveAsync(id, updated.Revision));
        }
        await ThrowsCodeAsync("RevisionConflict", () => catalog.RenameAsync("default", "stale", initial.Revision));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CatalogPath(disk.User)));
        var outside = Path.Combine(disk.Root, "outside");
        Directory.CreateDirectory(outside);
        var projects = Path.Combine(disk.User, ".workspaces", "projects");
        Directory.CreateSymbolicLink(projects, outside);
        await ThrowsCodeAsync("UnsafePath", () => catalog.CreateAsync("Never escape", updated.Revision));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(CatalogPath(disk.User)));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"generation\":1,\"activeProjectId\":\"default\",\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Default\",\"isArchived\":false}]}")]
    [InlineData("{\"schemaVersion\":1,\"generation\":1,\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Default\",\"isArchived\":false},{\"id\":\"default\",\"name\":\"Alias\",\"isArchived\":false}]}")]
    [InlineData("{\"schemaVersion\":1,\"generation\":1,\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Default\",\"isArchived\":false,\"root\":\"../escape\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"generation\":0,\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Default\",\"isArchived\":false}]}")]
    [InlineData("{\"schemaVersion\":1,\"generation\":1,\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Default\",\"isArchived\":true}]}")]
    [InlineData("{\"schemaVersion\":1,\"generation\":1,\"activeProjectId\":\"11111111111111111111111111111111\",\"projects\":[{\"id\":\"default\",\"name\":\"Default\",\"isArchived\":false}]}")]
    public async Task CatalogSchemaRefusalsPreserveBytesAfterIndependentValidFixture(string invalid)
    {
        using var disk = new TestDirectory();
        var path = CatalogPath(disk.User);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ValidCatalog);
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        Assert.Equal("Default", Assert.Single((await catalog.OpenAsync()).Projects).Name);
        await File.WriteAllTextAsync(path, invalid);
        var bytes = await File.ReadAllBytesAsync(path);

        await ThrowsCodeAsync("InvalidCatalog", () => catalog.OpenAsync());
        await ThrowsCodeAsync("RecoveryUnavailable", () => catalog.RecoverCatalogAsync());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("line\nbreak")]
    [InlineData("line\u0000break")]
    public async Task InvalidNamesCannotChangeAValidCatalog(string name)
    {
        using var disk = new TestDirectory();
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var initial = await catalog.OpenAsync();
        await ThrowsCodeAsync("InvalidName", () => catalog.CreateAsync(name, initial.Revision));
        await ThrowsCodeAsync("InvalidName", () => catalog.RenameAsync("default", name, initial.Revision));
        Assert.Equal(initial.Revision, (await catalog.OpenAsync()).Revision);
    }

    [Fact]
    public async Task NameCountAndEncodedCatalogLimitsAreEnforcedWithoutChanges()
    {
        using var disk = new TestDirectory();
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var initial = await catalog.OpenAsync();
        var valid = await catalog.RenameAsync("default", new string('a', 80), initial.Revision);
        await ThrowsCodeAsync("InvalidName", () => catalog.CreateAsync(new string('a', 81), valid.Revision));
        await ThrowsCodeAsync("InvalidName", () => catalog.CreateAsync("broken" + (char)0xd800, valid.Revision));
        var path = CatalogPath(disk.User);
        var entries = new List<object> { new { id = "default", name = "Default", isArchived = false } };
        entries.AddRange(Enumerable.Range(1, 127).Select(i => (object)new { id = i.ToString("x32"), name = "Other", isArchived = false }));
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { schemaVersion = 1, generation = 7, activeProjectId = "default", projects = entries }));
        var full = await catalog.OpenAsync();
        Assert.Equal(128, full.Projects.Count);
        await ThrowsCodeAsync("CatalogLimitExceeded", () => catalog.CreateAsync("129th", full.Revision));
        await File.WriteAllTextAsync(path, new string(' ', 256 * 1024) + ValidCatalog);
        await ThrowsCodeAsync("CatalogLimitExceeded", () => catalog.OpenAsync());
        Assert.True(new FileInfo(path).Length > 256 * 1024);
    }

    [Fact]
    public async Task MissingCatalogWithMetadataRequiresExplicitValidatedRecovery()
    {
        using var disk = new TestDirectory();
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var initial = await catalog.OpenAsync();
        await catalog.RenameAsync("default", "Before loss", initial.Revision);
        File.Delete(CatalogPath(disk.User));
        await ThrowsCodeAsync("RecoveryRequired", () => catalog.OpenAsync());
        Assert.False(File.Exists(CatalogPath(disk.User)));
        var recovered = await catalog.RecoverCatalogAsync();
        Assert.Equal("Default", Assert.Single(recovered.Projects).Name);
        Assert.True(File.Exists(CatalogPath(disk.User)));
    }

    [Fact]
    public async Task NonemptyUnknownCatalogLeaseCannotAuthorizeFreshAdoption()
    {
        using var disk = new TestDirectory();
        var valid = new ProjectWorkspaceCatalog(Path.Combine(disk.Root, "valid"));
        Assert.Equal("default", Assert.Single((await valid.OpenAsync()).Projects).Id);
        var metadata = Path.Combine(disk.User, ".workspaces");
        Directory.CreateDirectory(metadata);
        var path = Path.Combine(metadata, "catalog.lock");
        await File.WriteAllBytesAsync(path, [1, 255, 0, 2]);
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        await ThrowsCodeAsync("UnsafePath", () => catalog.OpenAsync());
        Assert.False(File.Exists(CatalogPath(disk.User)));
        Assert.Equal(new byte[] { 1, 255, 0, 2 }, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ActiveArchiveAndGenerationOverflowRefuseWithoutRewriting()
    {
        using var disk = new TestDirectory();
        var path = CatalogPath(disk.User);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ValidCatalog.Replace("\"generation\":1", "\"generation\":9223372036854775807", StringComparison.Ordinal));
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var current = await catalog.OpenAsync();
        await ThrowsCodeAsync("ActiveProjectArchived", () => catalog.SetArchivedAsync("default", true, current.Revision));
        await ThrowsCodeAsync("CatalogLimitExceeded", () => catalog.RenameAsync("default", "Overflow", current.Revision));
        Assert.Equal(current.Revision, (await catalog.OpenAsync()).Revision);
    }

    [Fact]
    public async Task CooperatingCatalogInstancesSerializeExpectedRevisions()
    {
        using var disk = new TestDirectory();
        var first = new ProjectWorkspaceCatalog(disk.User);
        var second = new ProjectWorkspaceCatalog(disk.User);
        var initial = await first.OpenAsync();
        var operations = new[] { first.RenameAsync("default", "One", initial.Revision), second.RenameAsync("default", "Two", initial.Revision) };
        try { await Task.WhenAll(operations); } catch (ProjectWorkspaceStoreException) { }
        _ = Assert.Single(operations, t => t.IsCompletedSuccessfully);
        var failure = Assert.Single(operations, t => t.IsFaulted).Exception!.InnerException;
        Assert.Equal("RevisionConflict", Assert.IsType<ProjectWorkspaceStoreException>(failure).ErrorCode);
        Assert.Contains(Assert.Single((await first.OpenAsync()).Projects).Name, new[] { "One", "Two" });
    }

    [Fact]
    public async Task MissingExpectedRevisionCannotBypassCatalogConflictChecks()
    {
        using var disk = new TestDirectory();
        var catalog = new ProjectWorkspaceCatalog(disk.User);
        var initial = await catalog.OpenAsync();
        var valid = await catalog.RenameAsync("default", "Valid edit", initial.Revision);
        await ThrowsCodeAsync("RevisionConflict", () => catalog.RenameAsync("default", "Bypassed", null!));
        await ThrowsCodeAsync("RevisionConflict", () => catalog.CreateAsync("Bypassed", null!));
        Assert.Equal(valid.Revision, (await catalog.OpenAsync()).Revision);
    }

    internal const string ValidCatalog = "{\"schemaVersion\":1,\"generation\":1,\"activeProjectId\":\"default\",\"projects\":[{\"id\":\"default\",\"name\":\"Default\",\"isArchived\":false}]}";

    private static async Task<string> SeedHistoryAsync(string directory, long generation, string name)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, generation, activeProjectId = "default",
            projects = new[] { new { id = "default", name, isArchived = false } }
        });
        var path = Path.Combine(directory, $"{generation:D20}-{Convert.ToHexStringLower(SHA256.HashData(bytes))}.json");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    internal static async Task ThrowsCodeAsync(string code, Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<ProjectWorkspaceStoreException>(operation);
        Assert.Equal(code, exception.ErrorCode);
        Assert.Equal("Project workspace storage failed.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    internal static string CatalogPath(string root) => Path.Combine(root, ".workspaces", "catalog.json");

    private static async Task<Dictionary<string, byte[]>> SeedLegacyAsync(string root)
    {
        var result = new Dictionary<string, byte[]>();
        foreach (var relative in new[] { "settings/plugin/settings.json", "secrets/plugin/vault.bin", "trust/consents.json", "data/plugin/ledger.bin", "unknown/future.dat" })
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = new byte[] { 0, 255, 13, 10, (byte)result.Count };
            await File.WriteAllBytesAsync(path, bytes);
            result.Add(path, bytes);
        }
        return result;
    }

    private static async Task AssertFilesAsync(Dictionary<string, byte[]> originals)
    {
        foreach (var (path, bytes) in originals)
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    private sealed class SnapshotComparer : IEqualityComparer<ProjectWorkspaceSnapshot>
    {
        internal static SnapshotComparer Instance { get; } = new();
        public bool Equals(ProjectWorkspaceSnapshot? x, ProjectWorkspaceSnapshot? y)
            => x?.Revision == y?.Revision && x?.ActiveProjectId == y?.ActiveProjectId && x!.Projects.SequenceEqual(y!.Projects);
        public int GetHashCode(ProjectWorkspaceSnapshot obj) => obj.Revision.GetHashCode(StringComparison.Ordinal);
    }
}
