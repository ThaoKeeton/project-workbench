using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Workbench.TestExecutor;

public sealed record ExecutionPolicy(
    bool LocalOnly,
    bool ComputerUseEnabled,
    ExportMode Export,
    string? LinearTeamId,
    string? LinearProjectId,
    string ComputerUseModel,
    bool ManualComputerUse = false,
    bool LocatorRepairEnabled = false,
    int RunTokenCap = 4000,
    int DailyTokenCap = 20000,
    decimal RunCostCap = 0.20m,
    decimal DailyCostCap = 1.00m,
    decimal CostPerThousandTokens = 0.01m,
    string RepairModel = "gpt-4.1-mini")
{
    public bool CanUseComputer => ComputerUseEnabled && ManualComputerUse && !LocalOnly;

    public bool CanRepair => LocatorRepairEnabled && !LocalOnly;

    public bool CanPush => !LocalOnly && Export == ExportMode.Push && !string.IsNullOrWhiteSpace(LinearTeamId);

    public ExecutionPolicy ForConfirmedExport() => this with { Export = ExportMode.Push };
}

public sealed record ExecutorSettings(
    bool ComputerUseEnabled,
    bool LocalOnly,
    ExportMode Export,
    string? LinearTeamId,
    string? LinearProjectId,
    string ComputerUseModel,
    bool LocatorRepairEnabled = false,
    int RunTokenCap = 4000,
    int DailyTokenCap = 20000,
    decimal RunCostCap = 0.20m,
    decimal DailyCostCap = 1.00m,
    decimal CostPerThousandTokens = 0.01m,
    string RepairModel = "gpt-4.1-mini")
{
    public static ExecutorSettings Defaults { get; } = new(false, false, ExportMode.Ask, null, null, ExecutorSchemas.DefaultComputerUseModel);

    public ExecutionPolicy ToPolicy(bool requestLocalOnly, bool manualComputerUse = false)
    {
        var localOnly = LocalOnly || requestLocalOnly;
        return new ExecutionPolicy(
            localOnly,
            ComputerUseEnabled,
            localOnly ? ExportMode.Local : Export,
            LinearTeamId,
            LinearProjectId,
            ComputerUseModel,
            manualComputerUse,
            LocatorRepairEnabled,
            RunTokenCap,
            DailyTokenCap,
            RunCostCap,
            DailyCostCap,
            CostPerThousandTokens,
            RepairModel);
    }
}

public sealed class ExecutorSettingsStore
{
    public const string FileName = "settings.v2.json";
    private static readonly JsonSchema Schema = JsonSchema.FromText(SuiteLoader.ReadResource("Workbench.TestExecutor.settings.v2.schema.json"));

    public static ExecutorSettings Load(string dataDirectory)
    {
        var path = Path.Combine(Path.GetFullPath(dataDirectory), FileName);
        if (!File.Exists(path)) return ExecutorSettings.Defaults;
        var info = new FileInfo(path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > 8192)
            throw new ExecutorDataException(ExecutorError.InvalidSettings);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            throw new ExecutorDataException(ExecutorError.InvalidSettings);
        }
        if (node is null || !SchemaCheck.Matches(Schema, node)) throw new ExecutorDataException(ExecutorError.InvalidSettings);
        return Map(node);
    }

    private static ExecutorSettings Map(JsonNode node)
    {
        var computer = node["computerUseEnabled"]?.GetValue<bool>() ?? false;
        var localOnly = node["localOnly"]?.GetValue<bool>() ?? false;
        var export = node["export"]?.GetValue<string>() switch
        {
            "push" => ExportMode.Push,
            "local" => ExportMode.Local,
            _ => ExportMode.Ask
        };
        var team = EmptyToNull(node["linearTeamId"]?.GetValue<string>());
        var project = EmptyToNull(node["linearProjectId"]?.GetValue<string>());
        var model = EmptyToNull(node["computerUseModel"]?.GetValue<string>()) ?? ExecutorSchemas.DefaultComputerUseModel;
        var repairModel = EmptyToNull(node["locatorRepairModel"]?.GetValue<string>()) ?? "gpt-4.1-mini";
        if (!TextRules.IsSafeId(model) || !TextRules.IsSafeId(repairModel)) throw new ExecutorDataException(ExecutorError.InvalidSettings);
        return new ExecutorSettings(
            computer,
            localOnly,
            export,
            team,
            project,
            model,
            node["locatorRepairEnabled"]?.GetValue<bool>() ?? false,
            node["runTokenCap"]?.GetValue<int>() ?? 4000,
            node["dailyTokenCap"]?.GetValue<int>() ?? 20000,
            ReadDecimal(node["runCostCap"], 0.20m),
            ReadDecimal(node["dailyCostCap"], 1.00m),
            ReadDecimal(node["costPerThousandTokens"], 0.01m),
            repairModel);
    }

    private static decimal ReadDecimal(JsonNode? node, decimal fallback)
        => node is null ? fallback : node.GetValue<decimal>();

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
