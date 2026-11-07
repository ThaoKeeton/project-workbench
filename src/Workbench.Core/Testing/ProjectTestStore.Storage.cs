using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

public sealed partial class ProjectTestStore
{
    private static readonly JsonSerializerOptions WireJson = CreateWireOptions();
    private sealed record StoreHeader(int SchemaVersion, string ProjectId);
    private sealed record ProjectRootHeader(int SchemaVersion, string Id, string Name);

    /// <summary>Acquires and validates once. All *Locked helpers require this exact live lease; never reacquire it.</summary>
    internal async Task<StoreLease?> AcquireStoreLeaseAsync(bool create, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProjectWorkspaceStorage.DirectoryPath(Path.GetDirectoryName(_directory)!, create: false);
        ProjectWorkspaceStorage.DirectoryPath(_directory, create: false);
        await ValidateProjectRootAsync(cancellationToken).ConfigureAwait(false);
        if (!create && !ProjectWorkspaceStorage.Exists(_directory)) return null;
        var stream = await ProjectWorkspaceStorage.LeaseAsync(_leasePath, cancellationToken).ConfigureAwait(false);
        var lease = new StoreLease(this, stream);
        try
        {
            await ValidateProjectRootAsync(cancellationToken).ConfigureAwait(false);
            if (!ProjectWorkspaceStorage.Exists(_directory))
            {
                if (!create) { await lease.DisposeAsync().ConfigureAwait(false); return null; }
                ProjectWorkspaceStorage.DirectoryPath(_directory, create: true);
                foreach (var area in new[] { "sources", "cases", "case-revisions" })
                    ProjectWorkspaceStorage.DirectoryPath(ProjectWorkspaceStorage.PathIn(_directory, area), create: true);
                await ProjectWorkspaceStorage.AtomicWriteAsync(ProjectWorkspaceStorage.PathIn(_directory, "store.json"),
                    Encode(new StoreHeader(1, _projectId)), overwrite: false, cancellationToken).ConfigureAwait(false);
            }
            await ValidateStoreLockedAsync(lease, cancellationToken).ConfigureAwait(false);
            return lease;
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task ValidateProjectRootAsync(CancellationToken cancellationToken)
    {
        // Legacy/default roots need no workspace marker. If one exists, its
        // identity must agree; never initialize testing inside a foreign root.
        var path = ProjectWorkspaceStorage.PathIn(Path.GetDirectoryName(_directory)!, "workspace.json");
        var bytes = await ProjectWorkspaceStorage.ReadAsync(path, 4096, "StoreLimitExceeded", cancellationToken).ConfigureAwait(false);
        if (bytes is null) return;
        var header = Decode<ProjectRootHeader>(bytes);
        if (header.SchemaVersion != 1 || header.Id != _projectId || header.Name != ProjectWorkspaceStorage.Name(header.Name, "TestStoreCorrupt"))
            throw TestValidation.Error("TestStoreCorrupt");
    }

    private async Task ValidateStoreLockedAsync(StoreLease lease, CancellationToken cancellationToken)
    {
        RequireLease(lease);
        var expected = new HashSet<string>(["store.json", "sources", "cases", "case-revisions"], StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(_directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = ProjectWorkspaceStorage.PathIn(_directory, Path.GetFileName(path));
            if (!expected.Remove(Path.GetFileName(path))) throw TestValidation.Error("TestStoreCorrupt");
        }
        if (expected.Count != 0) throw TestValidation.Error("TestStoreCorrupt");
        var headerPath = ProjectWorkspaceStorage.PathIn(_directory, "store.json");
        var header = Decode<StoreHeader>((await ProjectWorkspaceStorage.ReadAsync(headerPath, 4096, "StoreLimitExceeded", cancellationToken).ConfigureAwait(false))!);
        if (header.SchemaVersion != 1 || header.ProjectId != _projectId) throw TestValidation.Error("TestStoreCorrupt");
        foreach (var area in new[] { "sources", "cases", "case-revisions" })
            ProjectWorkspaceStorage.DirectoryPath(ProjectWorkspaceStorage.PathIn(_directory, area), create: false);
        foreach (var (id, path) in EnumerateManagedFiles("sources", 64, cancellationToken))
        {
            var bytes = (await ProjectWorkspaceStorage.ReadAsync(path, TestValidation.EnvelopeLimit, "StoreLimitExceeded", cancellationToken).ConfigureAwait(false))!;
            lease.Sources.Add(id, ParseSource(id, bytes));
        }
        foreach (var (id, path) in EnumerateManagedFiles("cases", 128, cancellationToken))
        {
            var bytes = (await ProjectWorkspaceStorage.ReadAsync(path, TestValidation.EnvelopeLimit, "StoreLimitExceeded", cancellationToken).ConfigureAwait(false))!;
            var snapshot = ParseCaseSet(id, bytes);
            ValidateTracesLocked(lease, snapshot.Cases, "TestStoreCorrupt");
            lease.Cases.Add(id, snapshot);
            lease.CaseBytes.Add(id, bytes);
        }
        var archiveRoot = ProjectWorkspaceStorage.PathIn(_directory, "case-revisions");
        foreach (var path in Directory.EnumerateFileSystemEntries(archiveRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(path);
            _ = ProjectWorkspaceStorage.PathIn(archiveRoot, id);
            if (!lease.Cases.TryGetValue(id, out var current)) throw TestValidation.Error("TestStoreCorrupt");
            ProjectWorkspaceStorage.DirectoryPath(path, create: false);
            var count = 0;
            foreach (var archivePath in Directory.EnumerateFileSystemEntries(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = Path.GetFileName(archivePath);
                _ = ProjectWorkspaceStorage.PathIn(path, file);
                ProjectWorkspaceStorage.RegularFile(archivePath);
                if (!file.EndsWith(".json", StringComparison.Ordinal) || file.Length != 69) throw TestValidation.Error("TestStoreCorrupt");
                var revision = file[..^5];
                if (!ProjectWorkspaceStorage.IsLowerHex(revision)) throw TestValidation.Error("TestStoreCorrupt");
                if (++count > 256 || lease.Archives.Count >= 256) throw TestValidation.Error("StoreLimitExceeded");
                var bytes = (await ProjectWorkspaceStorage.ReadAsync(archivePath, TestValidation.EnvelopeLimit, "StoreLimitExceeded", cancellationToken).ConfigureAwait(false))!;
                var archived = ParseCaseSet(id, bytes);
                if (archived.Revision != revision || archived.ProducerPluginId != current.ProducerPluginId) throw TestValidation.Error("TestStoreCorrupt");
                ValidateTracesLocked(lease, archived.Cases, "TestStoreCorrupt");
                lease.Archives.Add(id + "/" + revision, archived);
            }
            if (count == 0) throw TestValidation.Error("TestStoreCorrupt");
        }
    }

    private IEnumerable<(string Id, string Path)> EnumerateManagedFiles(string area, int limit, CancellationToken cancellationToken)
    {
        var directory = ProjectWorkspaceStorage.PathIn(_directory, area);
        var count = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = Path.GetFileName(path);
            _ = ProjectWorkspaceStorage.PathIn(directory, file);
            ProjectWorkspaceStorage.RegularFile(path);
            if (!file.EndsWith(".json", StringComparison.Ordinal) || file.Length != 37 || !ProjectWorkspaceStorage.IsLowerHex(file[..^5]))
                throw TestValidation.Error("TestStoreCorrupt");
            if (++count > limit) throw TestValidation.Error("StoreLimitExceeded");
            yield return (file[..^5], path);
        }
    }

    private static JsonSerializerOptions CreateWireOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            MaxDepth = 32
        };
        options.Converters.Add(new ExactEnumConverter<TestSourceKind>());
        options.Converters.Add(new ExactEnumConverter<TestVirtualScenario>());
        options.Converters.Add(new ExactEnumConverter<TestActionKind>());
        return options;
    }

    private static T Decode<T>(byte[] bytes)
    {
        try
        {
            _ = ProjectWorkspaceStorage.Utf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            ValidateJson(document.RootElement);
            return JsonSerializer.Deserialize<T>(bytes, WireJson) ?? throw TestValidation.Error("TestStoreCorrupt");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or DecoderFallbackException or ArgumentException)
        {
            throw TestValidation.Error("TestStoreCorrupt");
        }
    }

    private static void ValidateJson(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!ProjectWorkspaceStorage.ValidUnicode(property.Name) || !names.Add(property.Name)) throw TestValidation.Error("TestStoreCorrupt");
                ValidateJson(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) ValidateJson(item);
        }
        else if (value.ValueKind == JsonValueKind.String && !ProjectWorkspaceStorage.ValidUnicode(value.GetString()!))
            throw TestValidation.Error("TestStoreCorrupt");
    }

    private static byte[] Encode<T>(T value)
    {
        using var stream = new EnvelopeStream();
        JsonSerializer.Serialize(stream, value, WireJson);
        return stream.ToArray();
    }

    private sealed class EnvelopeStream : MemoryStream
    {
        private void Check(int count) { if (Length + count > TestValidation.EnvelopeLimit) throw TestValidation.Error("StoreLimitExceeded"); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    }

    private sealed class ExactEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || !Enum.TryParse<T>(reader.GetString(), ignoreCase: false, out var value)
                || !Enum.IsDefined(value) || reader.GetString() != value.ToString()) throw new JsonException();
            return value;
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (!Enum.IsDefined(value)) throw new JsonException();
            writer.WriteStringValue(value.ToString());
        }
    }
}
