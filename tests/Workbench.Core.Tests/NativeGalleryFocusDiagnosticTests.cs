using System.Text;
using System.Text.Json;
using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class NativeGalleryFocusDiagnosticTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string FileName = "upcam-preview.focus-diagnostic.json";
    private static RunOptions Options(string directory) => new(false, "private-user-root", Path.Combine(directory, "seed.json"))
        { BusinessProbe = "seed" };
    private static NativeGalleryFocusObservation Observation => new(
        NativeGalleryFocusPhase.InitialOwner, NativeGalleryFocusSite.InitialOwnerNull,
        NativeGalleryFocusCheck.RootReachedOwnerMissing, true, true, 3, 7, -1, -1, false);
    private static NativeGalleryFocusDiagnosticData Data => new(Commit, 5904, 134386349807924268,
        true, false, false, false, Observation, Observation, NativeGalleryAboveRootResult.NotSampled, 0, null);

    private static bool Write(RunOptions options, NativeGalleryFocusDiagnosticData? data = null,
        string shot = "upcam-preview", string page = "view", string? plugin = "com.upcam.task-digest",
        string? view = "digest", string status = "blocked", string reason = "focus-detached") =>
        NativeGalleryFocusDiagnostic.TryWrite(options, shot, page, plugin, view, status, reason, data ?? Data);

    [Fact]
    public void Writes_exact_fixed_schema_at_report_sibling_with_bounded_scalars()
    {
        using var directory = new TestDirectory();
        var options = Options(directory.Root);
        File.WriteAllText(options.ReportPath!, "original-probe-evidence");
        var terminal = Observation with { RootDepth = 255, DepthCapped = true };
        var data = Data with { TerminalFailure = terminal, AboveRootResult = NativeGalleryAboveRootResult.Found,
            AboveRootVisitedCount = 16, AboveRootOwnerDistance = 16 };

        Assert.True(Write(options, data));

        var bytes = File.ReadAllBytes(Path.Combine(directory.Root, FileName));
        Assert.InRange(bytes.Length, 1, 8192);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        AssertKeys(root, "schemaVersion", "kind", "shotId", "probePhase", "sourceCommit", "processId",
            "processCreationFileTimeUtc", "focusProvided", "initialOwnerFound", "positionEntered", "positionReturned",
            "firstFailure", "terminalFailure", "aboveRootResult", "aboveRootVisitedCount", "aboveRootOwnerDistance");
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("gallery-focus-boundary", root.GetProperty("kind").GetString());
        Assert.Equal("upcam-preview", root.GetProperty("shotId").GetString());
        Assert.Equal("business-seed", root.GetProperty("probePhase").GetString());
        Assert.Equal(Commit, root.GetProperty("sourceCommit").GetString());
        Assert.Equal(5904, root.GetProperty("processId").GetInt32());
        Assert.Equal(134386349807924268, root.GetProperty("processCreationFileTimeUtc").GetInt64());
        Assert.True(root.GetProperty("focusProvided").GetBoolean());
        Assert.False(root.GetProperty("initialOwnerFound").GetBoolean());
        Assert.False(root.GetProperty("positionEntered").GetBoolean());
        Assert.False(root.GetProperty("positionReturned").GetBoolean());
        foreach (var key in new[] { "firstFailure", "terminalFailure" })
        {
            var observation = root.GetProperty(key);
            AssertKeys(observation, "phase", "site", "check", "sawPage", "sawRoot", "pageDepth", "rootDepth",
                "nearestOwnerDepth", "hiddenDepth", "depthCapped");
            Assert.Equal((int)NativeGalleryFocusPhase.InitialOwner, observation.GetProperty("phase").GetInt32());
            Assert.Equal((int)NativeGalleryFocusSite.InitialOwnerNull, observation.GetProperty("site").GetInt32());
            Assert.Equal((int)NativeGalleryFocusCheck.RootReachedOwnerMissing, observation.GetProperty("check").GetInt32());
            Assert.True(observation.GetProperty("sawPage").GetBoolean());
            Assert.True(observation.GetProperty("sawRoot").GetBoolean());
            Assert.Equal(3, observation.GetProperty("pageDepth").GetInt32());
            Assert.Equal(-1, observation.GetProperty("nearestOwnerDepth").GetInt32());
            Assert.Equal(-1, observation.GetProperty("hiddenDepth").GetInt32());
        }
        Assert.Equal(255, root.GetProperty("terminalFailure").GetProperty("rootDepth").GetInt32());
        Assert.True(root.GetProperty("terminalFailure").GetProperty("depthCapped").GetBoolean());
        Assert.Equal((int)NativeGalleryAboveRootResult.Found, root.GetProperty("aboveRootResult").GetInt32());
        Assert.Equal(16, root.GetProperty("aboveRootVisitedCount").GetInt32());
        Assert.Equal(16, root.GetProperty("aboveRootOwnerDistance").GetInt32());
        Assert.InRange(JsonDepth(root), 1, 8);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain(directory.Root, text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-user-root", text, StringComparison.Ordinal);
        Assert.Equal("original-probe-evidence", File.ReadAllText(options.ReportPath!));
        Assert.False(Directory.Exists(Path.Combine(directory.Root, "gallery")));
        Assert.Equal(2, Directory.GetFiles(directory.Root).Length);
    }

    [Fact]
    public void Required_null_preserves_not_evaluated_flags_and_unseen_depths()
    {
        using var directory = new TestDirectory();
        var terminal = new NativeGalleryFocusObservation(NativeGalleryFocusPhase.RequiredInput,
            NativeGalleryFocusSite.RequiredNull, NativeGalleryFocusCheck.NotCalled, null, null, -1, -1, -1, -1, false);
        Assert.True(Write(Options(directory.Root), Data with { FocusProvided = false, FirstFailure = null, TerminalFailure = terminal }));
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory.Root, FileName)));
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("firstFailure").ValueKind);
        var observation = root.GetProperty("terminalFailure");
        Assert.Equal(JsonValueKind.Null, observation.GetProperty("sawPage").ValueKind);
        Assert.Equal(JsonValueKind.Null, observation.GetProperty("sawRoot").ValueKind);
        foreach (var name in new[] { "pageDepth", "rootDepth", "nearestOwnerDepth", "hiddenDepth" })
            Assert.Equal(-1, observation.GetProperty(name).GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("aboveRootOwnerDistance").ValueKind);
    }

    [Fact]
    public void Supplemental_first_failure_and_terminal_owner_change_remain_distinct()
    {
        using var directory = new TestDirectory();
        var first = Observation with { Phase = NativeGalleryFocusPhase.PositionLive,
            Site = NativeGalleryFocusSite.FocusAttachedHelperNull, Check = NativeGalleryFocusCheck.AncestorHidden,
            SawPage = null, SawRoot = null, PageDepth = -1, RootDepth = -1, HiddenDepth = 0 };
        var terminal = Observation with { Phase = NativeGalleryFocusPhase.PostPosition,
            Site = NativeGalleryFocusSite.FocusAttachedOwnerChanged, Check = NativeGalleryFocusCheck.OwnerReturned,
            NearestOwnerDepth = 0 };
        Assert.True(Write(Options(directory.Root), Data with { FirstFailure = first, TerminalFailure = terminal,
            InitialOwnerFound = true, PositionEntered = true, PositionReturned = true }));
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory.Root, FileName)));
        var root = document.RootElement;
        Assert.Equal((int)NativeGalleryFocusPhase.PositionLive, root.GetProperty("firstFailure").GetProperty("phase").GetInt32());
        Assert.Equal((int)NativeGalleryFocusPhase.PostPosition, root.GetProperty("terminalFailure").GetProperty("phase").GetInt32());
        Assert.Equal((int)NativeGalleryFocusSite.FocusAttachedOwnerChanged, root.GetProperty("terminalFailure").GetProperty("site").GetInt32());
        Assert.Equal((int)NativeGalleryFocusCheck.OwnerReturned, root.GetProperty("terminalFailure").GetProperty("check").GetInt32());
        Assert.True(root.GetProperty("initialOwnerFound").GetBoolean());
        Assert.True(root.GetProperty("positionEntered").GetBoolean());
        Assert.True(root.GetProperty("positionReturned").GetBoolean());
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 15, null)]
    [InlineData(3, 16, null)]
    [InlineData(4, 7, null)]
    public void Above_root_fixed_results_serialize_without_dynamic_data(int result, int visited, int? distance)
    {
        using var directory = new TestDirectory();
        Assert.True(Write(Options(directory.Root), Data with { AboveRootResult = (NativeGalleryAboveRootResult)result,
            AboveRootVisitedCount = visited, AboveRootOwnerDistance = distance }));
        var bytes = File.ReadAllBytes(Path.Combine(directory.Root, FileName));
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        Assert.Equal(result, document.RootElement.GetProperty("aboveRootResult").GetInt32());
        Assert.InRange(bytes.Length, 1, 8192);
    }

    [Theory]
    [InlineData("isolated", "false")]
    [InlineData("business", "restart")]
    [InlineData("business", "acceptance-seed")]
    [InlineData("shot", "upcam-preferences")]
    [InlineData("shot", "UPCAM-preview")]
    [InlineData("page", "home")]
    [InlineData("plugin", null)]
    [InlineData("plugin", "com.upcam.task-digest.acceptance")]
    [InlineData("view", null)]
    [InlineData("view", "other")]
    [InlineData("status", "captured")]
    [InlineData("status", "failed")]
    [InlineData("reason", "none")]
    [InlineData("reason", "capture-error")]
    public void Successful_or_nonmatching_attempts_have_no_side_effects(string field, string? value)
    {
        using var directory = new TestDirectory();
        var output = Path.Combine(directory.Root, "uncreated");
        var options = Options(output);
        if (field == "isolated") options = options with { BusinessProbe = null };
        if (field == "business") options = options with { BusinessProbe = value };
        Assert.False(Write(options, shot: field == "shot" ? value! : "upcam-preview",
            page: field == "page" ? value! : "view", plugin: field == "plugin" ? value : "com.upcam.task-digest",
            view: field == "view" ? value : "digest", status: field == "status" ? value! : "blocked",
            reason: field == "reason" ? value! : "focus-detached"));
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void CreateNew_refuses_overwrite_and_preserves_original_bytes()
    {
        using var directory = new TestDirectory();
        var options = Options(directory.Root);
        Assert.True(Write(options));
        var path = Path.Combine(directory.Root, FileName);
        var original = File.ReadAllBytes(path);
        Assert.False(Write(options, Data with { ProcessId = 123 }));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void Actual_write_failure_is_swallowed_without_altering_evidence()
    {
        using var directory = new TestDirectory();
        var blocker = Path.Combine(directory.Root, "parent-is-a-file");
        File.WriteAllText(blocker, "preserved");
        Assert.False(Write(Options(blocker)));
        Assert.Equal("preserved", File.ReadAllText(blocker));
        Assert.False(File.Exists(Path.Combine(blocker, FileName)));
        Assert.False(Write(Options(directory.Root) with { ReportPath = null }));
    }

    [Theory]
    [MemberData(nameof(InvalidData))]
    public void Invalid_fixed_scalar_data_is_refused_before_creating_output(object value)
    {
        var data = Assert.IsType<NativeGalleryFocusDiagnosticData>(value);
        using var directory = new TestDirectory();
        var output = Path.Combine(directory.Root, "uncreated");
        Assert.False(Write(Options(output), data));
        Assert.False(Directory.Exists(output));
    }

    public static IEnumerable<object[]> InvalidData()
    {
        yield return [Data with { SourceCommit = null! }];
        yield return [Data with { SourceCommit = "" }];
        yield return [Data with { SourceCommit = Commit.ToUpperInvariant() }];
        yield return [Data with { SourceCommit = new string('a', 8193) }];
        yield return [Data with { SourceCommit = new string('g', 40) }];
        yield return [Data with { ProcessId = 0 }];
        yield return [Data with { ProcessId = -1 }];
        yield return [Data with { ProcessCreationFileTimeUtc = 0 }];
        yield return [Data with { ProcessCreationFileTimeUtc = -1 }];
        yield return [Data with { ProcessCreationFileTimeUtc = long.MaxValue }];
        yield return [Data with { TerminalFailure = null }];
        yield return [Data with { TerminalFailure = Observation with { Phase = NativeGalleryFocusPhase.PositionLive,
            Site = NativeGalleryFocusSite.FocusAttachedHelperNull } }];
        yield return [Data with { TerminalFailure = Observation with { Phase = NativeGalleryFocusPhase.RestoreLive,
            Site = NativeGalleryFocusSite.FocusAttachedHelperNull } }];
        yield return [Data with { TerminalFailure = Observation with { Phase = (NativeGalleryFocusPhase)255 } }];
        yield return [Data with { FirstFailure = Observation with { Site = (NativeGalleryFocusSite)(-1) } }];
        yield return [Data with { TerminalFailure = Observation with { Check = (NativeGalleryFocusCheck)255 } }];
        yield return [Data with { AboveRootResult = (NativeGalleryAboveRootResult)255 }];
        foreach (var invalid in new[] { -2, 256, int.MaxValue })
        {
            yield return [Data with { TerminalFailure = Observation with { PageDepth = invalid } }];
            yield return [Data with { TerminalFailure = Observation with { RootDepth = invalid } }];
            yield return [Data with { FirstFailure = Observation with { NearestOwnerDepth = invalid } }];
            yield return [Data with { FirstFailure = Observation with { HiddenDepth = invalid } }];
        }
        yield return [Data with { AboveRootVisitedCount = -1 }];
        yield return [Data with { AboveRootVisitedCount = 17 }];
        yield return [Data with { AboveRootResult = NativeGalleryAboveRootResult.Found, AboveRootVisitedCount = 1, AboveRootOwnerDistance = 0 }];
        yield return [Data with { AboveRootResult = NativeGalleryAboveRootResult.Found, AboveRootVisitedCount = 16, AboveRootOwnerDistance = 17 }];
        yield return [Data with { AboveRootResult = NativeGalleryAboveRootResult.Found, AboveRootVisitedCount = 1 }];
        yield return [Data with { AboveRootVisitedCount = 1 }];
        yield return [Data with { AboveRootResult = NativeGalleryAboveRootResult.Capped, AboveRootVisitedCount = 15 }];
        yield return [Data with { AboveRootResult = NativeGalleryAboveRootResult.ChainEnded, AboveRootOwnerDistance = 1 }];
    }

    private static void AssertKeys(JsonElement value, params string[] names) =>
        Assert.Equal(names.Order(StringComparer.Ordinal), value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

    private static int JsonDepth(JsonElement value) => value.ValueKind == JsonValueKind.Object
        ? 1 + value.EnumerateObject().Max(p => JsonDepth(p.Value))
        : value.ValueKind == JsonValueKind.Array ? 1 + value.EnumerateArray().Max(JsonDepth) : 0;
}
