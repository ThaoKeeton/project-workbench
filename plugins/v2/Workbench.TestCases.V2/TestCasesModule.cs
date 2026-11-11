using System.Text;
using System.Text.Json;
using Workbench.Service.Abstractions;

namespace Workbench.TestCases.V2;

/// <summary>
/// Preview backend for the bundled test-case sample. Rows stay synthetic.
/// Nothing in this module opens a socket, webhook, or message send.
/// </summary>
public sealed class TestCasesModule : IWorkbenchPluginModule
{
    public const string PluginId = "com.projectworkbench.test-cases.v2";
    public const string DataSchema = "workbench.test-cases.v2.preview";
    private static readonly string[] RowIds = ["EX-001", "EX-002", "EX-003"];
    private static readonly string[] CsvHeader = ["suite,id,action,expected"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();

    public string Id => PluginId;

    public Task StartAsync(PluginModuleStart start, CancellationToken cancellationToken)
    {
        Require(start.GrantedPermissions, "project.read");
        Require(start.GrantedPermissions, "plugin.data.read");
        Require(start.GrantedPermissions, "plugin.data.write");
        Require(start.GrantedPermissions, "local.file.export");
        return Task.CompletedTask;
    }

    public async Task<PluginModuleResponse> HandleAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return request.Action switch
            {
                "cases.get" => JsonResponse(200, await ReadAsync(request, cancellationToken)),
                "cases.put" => JsonResponse(200, await WriteAsync(request, cancellationToken)),
                "cases.export" => await ExportAsync(request, cancellationToken),
                _ => Error(404, "NotFound")
            };
        }
        catch (PermissionDenied)
        {
            return Error(403, "PermissionDenied");
        }
        catch (Exception error) when (error is InvalidPluginData or JsonException)
        {
            return Error(400, "InvalidPluginData");
        }
    }

    private async Task<CaseDocument> ReadAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        Require(request.GrantedPermissions, "project.read");
        Require(request.GrantedPermissions, "plugin.data.read");
        var path = DataPath(request.DataDirectory);
        if (!File.Exists(path)) return Defaults();
        await using var stream = OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<CaseDocument>(stream, Json, cancellationToken);
        return Validate(document);
    }

    private static async Task<CaseDocument> WriteAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        Require(request.GrantedPermissions, "project.read");
        Require(request.GrantedPermissions, "plugin.data.write");
        if (request.JsonBody is not { Length: > 0 and <= 32768 }) throw new InvalidPluginData();
        var document = Validate(JsonSerializer.Deserialize<CaseDocument>(request.JsonBody, Json));
        var path = DataPath(request.DataDirectory);
        Directory.CreateDirectory(request.DataDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        File.Move(temporary, path, overwrite: true);
        return document;
    }

    private async Task<PluginModuleResponse> ExportAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        Require(request.GrantedPermissions, "local.file.export");
        var document = await ReadAsync(request, cancellationToken);
        var lines = CsvHeader.Concat(document.Rows.Select(row =>
            string.Join(",", new[] { document.SuiteName, row.Id, row.Action, row.Expected }.Select(Csv))));
        var csv = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(string.Join("\r\n", lines) + "\r\n");
        var path = Path.Combine(request.DataDirectory, "sample-test-cases.csv");
        Directory.CreateDirectory(request.DataDirectory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(temporary, csv, cancellationToken);
        File.Move(temporary, path, overwrite: true);
        return new PluginModuleResponse(200, "text/csv; charset=utf-8", csv);
    }

    private static CaseDocument Defaults() => new()
    {
        Schema = DataSchema,
        SchemaVersion = 2,
        SuiteName = "原生工作台演示",
        Rows =
        [
            new() { Id = "EX-001", Action = "启用本地插件", Expected = "页面出现在主窗口" },
            new() { Id = "EX-002", Action = "编辑摘要示例", Expected = "草稿保存到本地数据目录" },
            new() { Id = "EX-003", Action = "停用示例插件", Expected = "页面与命令入口撤销" }
        ]
    };

    private static CaseDocument Validate(CaseDocument? document)
    {
        if (document is null || document.Schema != DataSchema || document.SchemaVersion != 2
            || !ValidText(document.SuiteName, 80) || document.Rows is not { Count: 3 })
            throw new InvalidPluginData();
        for (var index = 0; index < RowIds.Length; index++)
        {
            var row = document.Rows[index];
            if (row.Id != RowIds[index] || !ValidText(row.Action, 500) || !ValidText(row.Expected, 500))
                throw new InvalidPluginData();
        }
        return document;
    }

    private static bool ValidText(string? value, int limit)
        => value is { Length: > 0 and var length } && length <= limit && !value.Any(char.IsControl);

    private static string DataPath(string directory)
    {
        var root = Path.GetFullPath(directory);
        var path = Path.GetFullPath(Path.Combine(root, "cases.v2.json"));
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidPluginData();
        return path;
    }

    private FileStream OpenRead(string path)
    {
        lock (_gate)
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
    }

    private static string Csv(string value)
        => value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;

    private static void Require(IReadOnlySet<string> granted, string permission)
    {
        if (!granted.Contains(permission)) throw new PermissionDenied();
    }

    private static PluginModuleResponse JsonResponse(int status, CaseDocument document)
        => new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(document, Json));

    private static PluginModuleResponse Error(int status, string code)
        => new(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(new { error = code }, Json));

    private sealed class PermissionDenied : Exception;
    private sealed class InvalidPluginData : Exception;

    private sealed record CaseDocument
    {
        public required string Schema { get; init; }
        public required int SchemaVersion { get; init; }
        public required string SuiteName { get; init; }
        public required IReadOnlyList<CaseRow> Rows { get; init; }
    }

    private sealed record CaseRow
    {
        public required string Id { get; init; }
        public required string Action { get; init; }
        public required string Expected { get; init; }
    }
}
