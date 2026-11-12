using Workbench.TestExecutor;
using Xunit;

namespace Workbench.TestExecutor.Tests;

public sealed class SuiteAndSettingsTests
{
    [Fact]
    public void Builtin_suite_uses_schema_2_and_the_decided_defaults()
    {
        var suite = new SuiteLoader().LoadBuiltin();
        Assert.Equal(ExecutorSchemas.BuiltinSuiteId, suite.Id);
        Assert.False(suite.StopOnFirstFailure);
        Assert.True(suite.MaskPasswordControls);
        Assert.Contains(suite.Cases, item => item.Steps.Any(step => step.Kind == StepKind.FlaUi && step.TimeoutSeconds == 15));
        Assert.Contains(suite.Cases, item => item.Steps.Any(step => step.Kind == StepKind.Ai && step.TimeoutSeconds == 60 && step.Sensitive));
        Assert.Contains("\"schemaVersion\": { \"const\": 2 }", SuiteLoader.SchemaText, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", SuiteLoader.SchemaText, StringComparison.Ordinal);
    }

    [Fact]
    public void Yaml_and_json_load_the_same_suite()
    {
        const string Yaml = """
            schema: workbench.test-executor.suite.v2.preview
            schemaVersion: 2
            id: yaml-suite
            name: 合成套件
            stopOnFirstFailure: false
            maskPasswordControls: true
            application:
              processName: TestExecutor.SampleApp
              windowTitle: 合成被测窗口
            masks:
              - kind: rectangle
                x: 1
                y: 2
                width: 3
                height: 4
              - kind: selector
                name: 密码
            cases:
              - id: CASE-1
                name: 点击
                steps:
                  - id: step-1
                    type: flaui
                    action: click
                    target:
                      automationId: sample-ok
                    expected: 按钮被点击
            """;
        var loaded = new SuiteLoader().Load(Yaml);
        Assert.Equal("yaml-suite", loaded.Id);
        Assert.False(loaded.StopOnFirstFailure);
        Assert.IsType<RectangleMask>(loaded.Masks[0]);
        Assert.Equal(15, loaded.Cases[0].Steps[0].TimeoutSeconds);
        Assert.Equal("sample-ok", loaded.Cases[0].Steps[0].Target!.AutomationId);
    }

    [Theory]
    [InlineData("""{"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":1,"id":"a","name":"n","cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"ai","action":"read"}]}]}""")]
    [InlineData("""{"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"a","name":"n","extra":1,"cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"ai","action":"read"}]}]}""")]
    [InlineData("""{"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"a","name":"n","cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"flaui","action":"click"}]}]}""")]
    [InlineData("""{"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"a","name":"bad\nname","cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"ai","action":"read"}]}]}""")]
    [InlineData("")]
    public void Invalid_documents_are_rejected_without_echoing_the_body(string text)
    {
        var error = Assert.Throws<ExecutorDataException>(() => new SuiteLoader().Load(text));
        Assert.Equal(ExecutorError.InvalidSuite, error.Code);
        Assert.Equal(ExecutorError.InvalidSuite, error.Message);
    }

    [Fact]
    public void Duplicate_case_ids_are_rejected()
    {
        const string Json = """
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"a","name":"n","cases":[
              {"id":"c","name":"n","steps":[{"id":"s","type":"ai","action":"read"}]},
              {"id":"c","name":"n","steps":[{"id":"s2","type":"ai","action":"read"}]}
            ]}
            """;
        Assert.Throws<ExecutorDataException>(() => new SuiteLoader().Load(Json));
    }

    [Fact]
    public void Catalog_lists_files_and_keeps_the_builtin_available()
    {
        var root = Fixtures.Temp();
        var suites = Path.Combine(root, "suites");
        Directory.CreateDirectory(suites);
        File.WriteAllText(Path.Combine(suites, "extra.json"), """
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"extra","name":"附加","cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"ai","action":"read"}]}]}
            """);
        File.WriteAllText(Path.Combine(suites, "broken.json"), "{");
        var catalog = new SuiteCatalog(new SuiteLoader());
        var listed = catalog.List(root);
        Assert.Contains(listed, item => item.Id == "extra" && item.Loadable && item.Format == "json");
        Assert.Contains(listed, item => item.Id == "broken" && !item.Loadable && item.Error == ExecutorError.InvalidSuite);
        Assert.Contains(listed, item => item.Id == ExecutorSchemas.BuiltinSuiteId && item.Format == "builtin");
        Assert.Equal("extra", catalog.Require(root, "extra").Id);
        Assert.Equal(ExecutorSchemas.BuiltinSuiteId, catalog.Require(root, ExecutorSchemas.BuiltinSuiteId).Id);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Duplicate_suite_ids_fail_at_start()
    {
        var root = Fixtures.Temp();
        var suites = Path.Combine(root, "suites");
        Directory.CreateDirectory(suites);
        const string Body = """
            {"schema":"workbench.test-executor.suite.v2.preview","schemaVersion":2,"id":"same","name":"n","cases":[{"id":"c","name":"n","steps":[{"id":"s","type":"ai","action":"read"}]}]}
            """;
        File.WriteAllText(Path.Combine(suites, "a.json"), Body);
        File.WriteAllText(Path.Combine(suites, "b.yaml"), "schema: workbench.test-executor.suite.v2.preview\nschemaVersion: 2\nid: same\nname: n\ncases:\n  - id: c\n    name: n\n    steps:\n      - id: s\n        type: ai\n        action: read\n");
        var catalog = new SuiteCatalog(new SuiteLoader());
        var error = Assert.Throws<ExecutorDataException>(() => catalog.Require(root, "same"));
        Assert.Equal(ExecutorError.DuplicateSuiteId, error.Code);
        Assert.Contains(catalog.List(root), item => item.Id == "same" && !item.Loadable);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Missing_settings_default_to_ask_and_computer_use_off()
    {
        var root = Fixtures.Temp();
        var settings = new ExecutorSettingsStore().Load(root);
        Assert.False(settings.ComputerUseEnabled);
        Assert.False(settings.LocalOnly);
        Assert.Equal(ExportMode.Ask, settings.Export);
        var policy = settings.ToPolicy(false);
        Assert.False(policy.CanUseComputer);
        Assert.False(policy.CanPush);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Local_only_cannot_be_turned_off_by_a_run_request()
    {
        var root = Fixtures.Temp();
        File.WriteAllText(Path.Combine(root, ExecutorSettingsStore.FileName), """
            {"schema":"workbench.test-executor.settings.v2.preview","schemaVersion":2,"computerUseEnabled":true,"localOnly":true,"export":"push","linearTeamId":"team-1","computerUseModel":"computer-use-preview"}
            """);
        var policy = new ExecutorSettingsStore().Load(root).ToPolicy(false);
        Assert.True(policy.LocalOnly);
        Assert.True(policy.ComputerUseEnabled);
        Assert.False(policy.CanUseComputer);
        Assert.False(policy.CanPush);
        Assert.Equal(ExportMode.Push, policy.ForConfirmedExport().Export);
        Assert.False(policy.ForConfirmedExport().CanPush);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Settings_reject_unexpected_fields_without_echoing_them()
    {
        var root = Fixtures.Temp();
        File.WriteAllText(Path.Combine(root, ExecutorSettingsStore.FileName), """
            {"schema":"workbench.test-executor.settings.v2.preview","schemaVersion":2,"apiKey":"synthetic-credential-value"}
            """);
        var error = Assert.Throws<ExecutorDataException>(() => new ExecutorSettingsStore().Load(root));
        Assert.Equal(ExecutorError.InvalidSettings, error.Message);
        Assert.DoesNotContain("synthetic-credential-value", error.Message, StringComparison.Ordinal);
        Directory.Delete(root, true);
    }

    [Fact]
    public void Confirmed_export_turns_ask_into_push_when_a_team_is_configured()
    {
        var policy = new ExecutionPolicy(false, true, ExportMode.Ask, "team-1", null, "computer-use-preview").ForConfirmedExport();
        Assert.Equal(ExportMode.Push, policy.Export);
        Assert.True(policy.CanPush);
        Assert.False(policy.CanUseComputer);
        Assert.True(policy with { ManualComputerUse = true } is { CanUseComputer: true });
    }
}
