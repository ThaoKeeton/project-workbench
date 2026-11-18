using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Workbench.Core.Projects;
using Workbench.Service;
using Xunit;

namespace Workbench.Service.Tests;

public sealed class ProjectDeskApiTests
{
    [Fact]
    public void Names_reject_blanks_controls_length_and_duplicates_before_any_write()
    {
        var projects = new[]
        {
            new ProjectWorkspaceInfo("default", "星河协作", false),
            new ProjectWorkspaceInfo("lamp", "灯塔实验", true)
        };
        Assert.Equal("InvalidName", ProjectNames.Rejection(null, projects, null));
        Assert.Equal("InvalidName", ProjectNames.Rejection("   ", projects, null));
        Assert.Equal("InvalidName", ProjectNames.Rejection("北\n港", projects, null));
        Assert.Equal("InvalidName", ProjectNames.Rejection("北" + (char)0xD800, projects, null));
        Assert.Equal("InvalidName", ProjectNames.Rejection(new string('名', 81), projects, null));
        Assert.Null(ProjectNames.Rejection(new string('名', 80), projects, null));
        Assert.Null(ProjectNames.Rejection("海岸 🧪", projects, null));
        Assert.Equal("DuplicateName", ProjectNames.Rejection(" 灯塔实验 ", projects, null));
        Assert.Equal("DuplicateName", ProjectNames.Rejection("星河协作", projects, "lamp"));
        Assert.Null(ProjectNames.Rejection("星河协作", projects, "default"));
    }

    [Fact]
    public async Task Failed_open_keeps_the_current_project_and_the_new_entry()
    {
        var root = TempRoot();
        try
        {
            var catalog = new ProjectWorkspaceCatalog(root);
            var opened = await catalog.OpenAsync();
            var created = await catalog.CreateAsync("北港记录", opened.Revision);
            Assert.Equal("default", created.Snapshot.ActiveProjectId);

            var (didOpen, snapshot) = await ProjectDeskOperations.TryOpenAsync(catalog, created.Project.Id, "stale", CancellationToken.None);
            Assert.False(didOpen);
            Assert.Equal("default", snapshot.ActiveProjectId);
            Assert.Contains(snapshot.Projects, project => project.Name == "北港记录");
            Assert.Equal("default", (await catalog.OpenAsync()).ActiveProjectId);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Invalid_create_and_rename_do_not_change_the_catalog()
    {
        await using var running = await StartAsync();
        var catalogPath = Path.Combine(running.Root, ".workspaces", "catalog.json");
        var before = await File.ReadAllBytesAsync(catalogPath);
        var listed = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects");
        var revision = listed.GetProperty("revision").GetString();

        foreach (var name in new[] { "", "   ", "北\n港", "北\u0000港", new string('名', 81), "default" })
        {
            var response = await running.Client.PostAsJsonAsync("/api/v2/projects", new { name, expectedRevision = revision });
            Assert.Equal(name.Equals("default", StringComparison.OrdinalIgnoreCase) ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(name.Equals("default", StringComparison.OrdinalIgnoreCase) ? "DuplicateName" : "InvalidName", body.GetProperty("error").GetString());
            Assert.Single(body.EnumerateObject());
        }

        var blank = await running.Client.PostAsJsonAsync("/api/v2/projects", new { name = "海岸记录", expectedRevision = " " });
        Assert.Equal(HttpStatusCode.Conflict, blank.StatusCode);
        Assert.Equal("RevisionConflict", (await blank.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var stale = await running.Client.PostAsJsonAsync("/api/v2/projects", new { name = "海岸记录", expectedRevision = "0" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var rename = await running.Client.PostAsJsonAsync("/api/v2/projects/name", new { name = "", expectedRevision = revision });
        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        var same = await running.Client.PostAsJsonAsync("/api/v2/projects/name", new { name = "  Default  ", expectedRevision = revision });
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);

        Assert.Equal(before, await File.ReadAllBytesAsync(catalogPath));
        var after = await running.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects");
        Assert.Equal("default", after.GetProperty("activeProjectId").GetString());
        Assert.Equal("Default", after.GetProperty("projects")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Broken_catalog_is_not_replaced_when_create_is_refused()
    {
        await using var running = await StartAsync();
        var metadata = Path.Combine(running.Root, ".workspaces");
        File.Delete(Path.Combine(metadata, "catalog.json"));
        await File.WriteAllTextAsync(Path.Combine(metadata, "stray.txt"), "keep");

        var response = await running.Client.PostAsJsonAsync("/api/v2/projects", new { name = "海岸记录", expectedRevision = "missing" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("RecoveryRequired", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.False(File.Exists(Path.Combine(metadata, "catalog.json")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(metadata, "stray.txt")));
    }

    [Fact]
    public async Task Create_rename_and_restart_keep_each_projects_note()
    {
        var root = TempRoot();
        try
        {
            string coastId;
            string starRevision;
            await using (var first = await StartAsync(root))
            {
                var planted = Path.Combine(root, "preferences", "audience.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(planted)!);
                await File.WriteAllTextAsync(planted, "only-star");

                var listed = await first.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects");
                var renamed = await first.Client.PostAsJsonAsync("/api/v2/projects/name", new { name = "星河协作", expectedRevision = listed.GetProperty("revision").GetString() });
                Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
                var star = await renamed.Content.ReadFromJsonAsync<JsonElement>();
                var missing = await first.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects/active/context");
                Assert.True(missing.GetProperty("missing").GetBoolean());
                Assert.False(File.Exists(Path.Combine(root, ".workbench-project", "context.json")));

                var written = await first.Client.PutAsJsonAsync("/api/v2/projects/active/context", new { text = "只在星河", expectedRevision = "missing" });
                Assert.Equal(HttpStatusCode.OK, written.StatusCode);
                var staleNote = await first.Client.PutAsJsonAsync("/api/v2/projects/active/context", new { text = "不要覆盖", expectedRevision = "missing" });
                Assert.Equal(HttpStatusCode.Conflict, staleNote.StatusCode);
                Assert.Equal("只在星河", (await first.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects/active/context")).GetProperty("text").GetString());

                var created = await first.Client.PostAsJsonAsync("/api/v2/projects", new { name = "  海岸记录  ", expectedRevision = star.GetProperty("revision").GetString() });
                Assert.Equal(HttpStatusCode.OK, created.StatusCode);
                var coast = await created.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(coast.GetProperty("opened").GetBoolean());
                coastId = coast.GetProperty("activeProjectId").GetString()!;
                Assert.Equal("海岸记录", coast.GetProperty("projects").EnumerateArray().Single(project => project.GetProperty("id").GetString() == coastId).GetProperty("name").GetString());
                var coastNote = await first.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects/active/context");
                Assert.True(coastNote.GetProperty("missing").GetBoolean());
                Assert.False(File.Exists(Path.Combine(root, ".workspaces", "projects", coastId, "preferences", "audience.txt")));
                Assert.False(File.Exists(Path.Combine(root, ".workspaces", "projects", coastId, ".workbench-project", "context.json")));
                using (var starContext = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, ".workbench-project", "context.json"))))
                {
                    Assert.Equal(1, starContext.RootElement.GetProperty("schemaVersion").GetInt32());
                    Assert.Equal("default", starContext.RootElement.GetProperty("projectId").GetString());
                    Assert.Equal("只在星河", starContext.RootElement.GetProperty("text").GetString());
                }

                var coastWrite = await first.Client.PutAsJsonAsync("/api/v2/projects/active/context", new { text = "只在海岸", expectedRevision = "missing" });
                Assert.Equal(HttpStatusCode.OK, coastWrite.StatusCode);
                var back = await first.Client.PostAsJsonAsync("/api/v2/projects/active", new { id = "default", expectedRevision = coast.GetProperty("revision").GetString() });
                var backBody = await back.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("default", backBody.GetProperty("activeProjectId").GetString());
                Assert.Equal("只在星河", (await first.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects/active/context")).GetProperty("text").GetString());

                var forward = await first.Client.PostAsJsonAsync("/api/v2/projects/active", new { id = coastId, expectedRevision = backBody.GetProperty("revision").GetString() });
                var forwardBody = await forward.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("只在海岸", (await first.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects/active/context")).GetProperty("text").GetString());

                var renamedCoast = await first.Client.PostAsJsonAsync("/api/v2/projects/name", new { name = "北港记录", expectedRevision = forwardBody.GetProperty("revision").GetString() });
                var renamedBody = await renamedCoast.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(coastId, renamedBody.GetProperty("activeProjectId").GetString());
                Assert.Equal("只在海岸", (await first.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects/active/context")).GetProperty("text").GetString());
                var duplicate = await first.Client.PostAsJsonAsync("/api/v2/projects/name", new { name = "星河协作", expectedRevision = renamedBody.GetProperty("revision").GetString() });
                Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
                Assert.Equal("DuplicateName", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
                starRevision = renamedBody.GetProperty("revision").GetString()!;

                using var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, ".workspaces", "catalog.json")));
                Assert.Equal(1, catalog.RootElement.GetProperty("schemaVersion").GetInt32());
                foreach (var project in catalog.RootElement.GetProperty("projects").EnumerateArray())
                    Assert.Equal(new[] { "id", "isArchived", "name" }, project.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            }

            await using var second = await StartAsync(root);
            var restored = await second.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects");
            Assert.Equal(coastId, restored.GetProperty("activeProjectId").GetString());
            Assert.Equal(starRevision, restored.GetProperty("revision").GetString());
            Assert.Equal("北港记录", restored.GetProperty("projects").EnumerateArray().Single(project => project.GetProperty("id").GetString() == coastId).GetProperty("name").GetString());
            Assert.Equal("只在海岸", (await second.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects/active/context")).GetProperty("text").GetString());
            var starAgain = await second.Client.PostAsJsonAsync("/api/v2/projects/active", new { id = "default", expectedRevision = restored.GetProperty("revision").GetString() });
            Assert.Equal(HttpStatusCode.OK, starAgain.StatusCode);
            Assert.Equal("只在星河", (await second.Client.GetFromJsonAsync<JsonElement>("/api/v2/projects/active/context")).GetProperty("text").GetString());
            Assert.Equal("only-star", await File.ReadAllTextAsync(Path.Combine(root, "preferences", "audience.txt")));
        }
        finally { Delete(root); }
    }

    private static async Task<Running> StartAsync(string? root = null)
    {
        root ??= TempRoot();
        var plugins = Path.Combine(root, "plugins-v2");
        CopySample(plugins);
        var host = await WorkbenchServiceHost.StartAsync(new WorkbenchServiceOptions
        {
            UserRoot = root,
            V1PluginsRoot = Path.Combine(root, "plugins-v1"),
            V2PluginsRoot = plugins,
            AllowLan = false
        });
        var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.SessionToken);
        return new Running(host, client, root);
    }

    private static void CopySample(string destination)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "preview-plugins");
        if (!Directory.Exists(source)) throw new InvalidOperationException("The preview plugin was not staged.");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "workbench-desk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
    }

    private sealed class Running(WorkbenchServiceHost host, HttpClient client, string root) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public string Root { get; } = root;
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.DisposeAsync();
        }
    }
}
