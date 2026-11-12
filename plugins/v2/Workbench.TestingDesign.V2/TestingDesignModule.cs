using System.Text;
using System.Text.Json;
using Workbench.Service.Abstractions;

namespace Workbench.TestingDesign.V2;

/// <summary>
/// Serves a synthetic-check dashboard. It does not execute tests and does not replace human acceptance.
/// </summary>
public sealed class TestingDesignModule : IWorkbenchPluginModule
{
    public const string PluginId = "com.projectworkbench.testing-design.v2";
    public const string DataSchema = "workbench.testing.dashboard.v2.preview";
    private const string BoardFile = "dashboard.v2.json";

    public string Id => PluginId;

    public Task StartAsync(PluginModuleStart start, CancellationToken cancellationToken)
    {
        RequireData(start.GrantedPermissions);
        _ = Canonical();
        return Task.CompletedTask;
    }

    public Task<PluginModuleResponse> HandleAsync(PluginModuleRequest request, CancellationToken cancellationToken)
        => PluginResponses.Run(() => request.Action switch
        {
            "dashboard" => PublishAsync(request, cancellationToken),
            _ => Task.FromResult(PluginResponses.Fail(404, "NotFound"))
        });

    private static async Task<PluginModuleResponse> PublishAsync(PluginModuleRequest request, CancellationToken cancellationToken)
    {
        RequireData(request.GrantedPermissions);
        var document = Canonical();
        Directory.CreateDirectory(request.DataDirectory);
        var path = PluginDataFile.Contain(request.DataDirectory, BoardFile);
        await PluginDataFile.WriteAtomicAsync(path, JsonSerializer.SerializeToUtf8Bytes(document, PluginResponses.Json), cancellationToken);
        return PluginResponses.Ok(document);
    }

    private static Dashboard Canonical()
    {
        using var stream = typeof(TestingDesignModule).Assembly.GetManifestResourceStream("dashboard-sample.json")
            ?? throw new InvalidOperationException("missing-sample");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var document = JsonSerializer.Deserialize<Dashboard>(reader.ReadToEnd(), PluginResponses.Json) ?? throw new PluginDataException();
        return Validate(document);
    }

    private static Dashboard Validate(Dashboard document)
    {
        if (document.Schema != DataSchema || document.SchemaVersion != 2 || document.AutoSend || document.HumanAcceptance)
            throw new PluginDataException();
        if (document.Label is null || !document.Label.Contains("不替代人工验收", StringComparison.Ordinal))
            throw new PluginDataException();
        if (document.Notice is null || !document.Notice.Contains("人工验收", StringComparison.Ordinal))
            throw new PluginDataException();
        if (document.Cases is not { Count: > 0 and <= 32 } || document.Runs is not { Count: > 0 and <= 32 })
            throw new PluginDataException();
        if (document.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != document.Cases.Count)
            throw new PluginDataException();
        var caseIds = document.Cases.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var run in document.Runs)
        {
            if (run.HumanAcceptance != "未验收" || run.Kind != "合成检查" || !caseIds.Contains(run.CaseId))
                throw new PluginDataException();
            if (run.Status is not ("通过" or "未通过" or "未执行")) throw new PluginDataException();
        }
        var passed = document.Runs.Count(run => run.Status == "通过");
        var failed = document.Runs.Count(run => run.Status == "未通过");
        return document with
        {
            Summary = new DashboardSummary { Passed = passed, Failed = failed, Total = document.Runs.Count }
        };
    }

    private static void RequireData(IReadOnlySet<string> granted)
    {
        PluginPermissions.Require(granted, "project.read");
        PluginPermissions.Require(granted, "plugin.data.read");
        PluginPermissions.Require(granted, "plugin.data.write");
    }

    private sealed record Dashboard
    {
        public required string Schema { get; init; }
        public required int SchemaVersion { get; init; }
        public required bool AutoSend { get; init; }
        public required string Label { get; init; }
        public required string Notice { get; init; }
        public required bool HumanAcceptance { get; init; }
        public required IReadOnlyList<TestCase> Cases { get; init; }
        public required IReadOnlyList<TestRun> Runs { get; init; }
        public DashboardSummary? Summary { get; init; }
    }

    private sealed record TestCase
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Suite { get; init; }
    }

    private sealed record TestRun
    {
        public required string Id { get; init; }
        public required string CaseId { get; init; }
        public required string Status { get; init; }
        public required string Kind { get; init; }
        public required string HumanAcceptance { get; init; }
    }

    private sealed record DashboardSummary
    {
        public required int Passed { get; init; }
        public required int Failed { get; init; }
        public required int Total { get; init; }
    }
}
