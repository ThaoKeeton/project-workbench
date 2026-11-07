using System.Text;
using Workbench.Core.Projects;
using Workbench.Sdk;

namespace Workbench.Core.Testing;

// Pure bounded input capture, reusable by the retained-admission service facade.
// Never enumerate caller collections while holding a Core/coordinator lock.
internal static class TestValidation
{
    internal const int EnvelopeLimit = 4 * 1024 * 1024;
    internal const int OriginalLimit = 1024 * 1024;
    internal static string SafeErrorCode(string? code) => code is
        "TestingContextClosed" or "InvalidTestSource" or "InvalidCaseSet" or "InvalidRun" or "InvalidEvidence" or
        "RevisionConflict" or "StoreLimitExceeded" or "StoreIoFailure" or "UnsafePath" or "TestStoreCorrupt" or
        "RecoveryRequired" or "ManualStepUnsupported" or "TargetUnavailable" or "AssertionFailed" or "AdapterFailed" or
        "TestRecordNotFound" or "StoreBusy" or "InvalidId" or "InvalidProducer" or "TestingFailed" ? code : "TestingFailed";
    internal static PluginTestingException Error(string code) => new(SafeErrorCode(code));
    internal static void Id(string? id) { if (id is not { Length: 32 } || !ProjectWorkspaceStorage.IsLowerHex(id)) throw Error("InvalidId"); }
    internal static void Hash(string? hash) { if (hash is not { Length: 64 } || !ProjectWorkspaceStorage.IsLowerHex(hash)) throw Error("InvalidId"); }
    internal static void Producer(string? id) { if (!PackageValidator.IsSafePluginId(id)) throw Error("InvalidProducer"); }

    internal static void Text(string? text, int max, bool required, string code)
    {
        if (text is null || !ProjectWorkspaceStorage.ValidUnicode(text) || required && string.IsNullOrWhiteSpace(text)) throw Error(code);
        if (text.Length > max) throw Error("StoreLimitExceeded");
    }

    private static T[] CopyList<T>(IReadOnlyList<T>? values, int max, string code, Func<T, T> copy)
    {
        if (values is null || values.Count == 0) throw Error(code);
        var count = values.Count;
        if (count > max) throw Error("StoreLimitExceeded");
        var result = new T[count];
        for (var i = 0; i < count; i++) result[i] = copy(values[i]);
        return result;
    }

    internal static TestSourceDraft CopySourceDraft(TestSourceDraft draft)
    {
        const string code = "InvalidTestSource";
        if (draft is null || draft.OriginalBytes is null || draft.OriginalBytes.Length == 0) throw Error(code);
        if (draft.OriginalBytes.Length > OriginalLimit) throw Error("StoreLimitExceeded");
        var result = draft with
        {
            OriginalBytes = draft.OriginalBytes.ToArray(),
            Items = CopyList(draft.Items, 256, code, item => item is null ? throw Error(code) : item with { AcceptanceCriteria = CopyList(item.AcceptanceCriteria, 16, code, criterion => criterion) })
        };
        if (!Enum.IsDefined(result.Kind)) throw Error(code);
        Text(result.ProviderId, 100, true, code);
        Text(result.ProviderVersion, 40, true, code);
        Text(result.DocumentId, 200, true, code);
        Text(result.Title, 200, true, code);
        try { _ = ProjectWorkspaceStorage.Utf8.GetCharCount(result.OriginalBytes); }
        catch (DecoderFallbackException) { throw Error(code); }
        var locators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in result.Items)
        {
            Text(item.Locator, 512, true, code);
            Text(item.Title, 200, true, code);
            Text(item.Description, 4096, false, code);
            if (!locators.Add(item.Locator) || item.VirtualScenario is { } scenario && !Enum.IsDefined(scenario)) throw Error(code);
            foreach (var criterion in item.AcceptanceCriteria) Text(criterion, 4096, true, code);
        }
        return result;
    }

    internal static TestSourceSnapshot CopySourceSnapshot(TestSourceSnapshot snapshot)
    {
        var draft = CopySourceDraft(new(snapshot.Kind, snapshot.ProviderId, snapshot.ProviderVersion, snapshot.DocumentId, snapshot.Title, snapshot.OriginalBytes, snapshot.Items));
        return snapshot with { OriginalBytes = draft.OriginalBytes, Items = draft.Items };
    }

    internal static TestCaseSetDraft CopyCaseSetDraft(TestCaseSetDraft draft)
    {
        const string code = "InvalidCaseSet";
        if (draft is null) throw Error(code);
        Text(draft.Name, 200, true, code);
        return draft with { Cases = CopyList(draft.Cases, 200, code, item =>
        {
            if (item is null) throw Error(code);
            CaseText(item.Title, item.Prerequisites);
            return item with { Traces = CopyTraces(item.Traces), Steps = CopyList(item.Steps, 20, code, step =>
            {
                if (step is null) throw Error(code);
                StepText(step.Instruction, step.Expected, step.Action);
                return step with { Traces = CopyTraces(step.Traces) };
            }) };
        }) };
    }

    internal static TestCaseSetContent CopyCaseSetContent(TestCaseSetContent content)
    {
        const string code = "InvalidCaseSet";
        if (content is null) throw Error(code);
        Text(content.Name, 200, true, code);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return content with { Cases = CopyList(content.Cases, 200, code, item =>
        {
            if (item is null) throw Error(code);
            Id(item.Id);
            if (!ids.Add(item.Id)) throw Error(code);
            CaseText(item.Title, item.Prerequisites);
            return item with { Traces = CopyTraces(item.Traces), Steps = CopyList(item.Steps, 20, code, step =>
            {
                if (step is null) throw Error(code);
                Id(step.Id);
                if (!ids.Add(step.Id)) throw Error(code);
                StepText(step.Instruction, step.Expected, step.Action);
                return step with { Traces = CopyTraces(step.Traces) };
            }) };
        }) };
    }

    private static TestTrace[] CopyTraces(IReadOnlyList<TestTrace> traces) => CopyList(traces, 16, "InvalidCaseSet", trace =>
    {
        if (trace is null) throw Error("InvalidCaseSet");
        Id(trace.SnapshotId);
        Hash(trace.SnapshotRevision);
        Text(trace.Locator, 512, true, "InvalidCaseSet");
        if (trace.CriterionIndex < 0) throw Error("InvalidCaseSet");
        return trace;
    });

    private static void CaseText(string title, string prerequisites)
    {
        Text(title, 200, true, "InvalidCaseSet");
        Text(prerequisites, 4096, false, "InvalidCaseSet");
    }

    private static void StepText(string instruction, string expected, TestAction? action)
    {
        Text(instruction, 4096, true, "InvalidCaseSet");
        Text(expected, 4096, true, "InvalidCaseSet");
        if (action is null) return;
        Text(action.ElementId, 100, true, "InvalidCaseSet");
        var valid = action.Kind switch
        {
            TestActionKind.SetText => action.ElementId == "task.title" && action.Value is not null,
            TestActionKind.Click => action.ElementId == "task.save" && action.Value is null,
            TestActionKind.AssertText => action.ElementId is "task.toast" or "task.validation" or "task.createdCount" && action.Value is not null,
            _ => false
        };
        if (!valid) throw Error("InvalidCaseSet");
        if (action.Value is not null) Text(action.Value, 4096, false, "InvalidCaseSet");
    }
}
