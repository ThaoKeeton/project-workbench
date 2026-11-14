using System.Text;
using Workbench.TestExecutor;
using Xunit;

namespace Workbench.TestExecutor.Tests;

public sealed class MaskCredentialReportTests
{
    [Fact]
    public void Rectangles_and_selector_regions_are_painted_before_the_image_can_leave()
    {
        var raw = SyntheticFrames.PasswordField();
        var masked = ScreenshotMasker.Apply(raw.Png, [new PixelRect(8, 8, 16, 8), new PixelRect(0, 0, 2, 2)]);
        var before = PngImage.Decode(raw.Png);
        var after = PngImage.Decode(masked.Png);
        Assert.Equal(new Rgba8(240, 20, 20, 255), before[10, 10]);
        Assert.Equal(ScreenshotMasker.Ink, after[10, 10]);
        Assert.Equal(ScreenshotMasker.Ink, after[0, 0]);
        Assert.Equal(new Rgba8(10, 20, 200, 255), after[30, 30]);
        Assert.NotEqual(raw.Png, masked.Png);
    }

    [Fact]
    public void Png_filters_round_trip_for_the_color_types_a_window_capture_uses()
    {
        var scanlines = new byte[]
        {
            1, 10, 20, 30, 1, 2, 3,
            2, 4, 5, 6, 7, 8, 9,
            4, 1, 1, 1, 2, 2, 2
        };
        var image = PngImage.FromFiltered(2, 3, 2, scanlines);
        Assert.Equal(2, image.Width);
        Assert.Equal(255, image[0, 0].A);
        Assert.NotEqual(0, image[1, 2].R);
        var gray = PngImage.FromFiltered(1, 1, 0, [0, 40]);
        Assert.Equal(new Rgba8(40, 40, 40, 255), gray[0, 0]);
        var grayAlpha = PngImage.FromFiltered(1, 1, 4, [3, 9, 8]);
        Assert.Equal(8, grayAlpha[0, 0].A);
        Assert.Throws<ExecutorDataException>(() => PngImage.Decode([1, 2, 3]));
    }

    [Fact]
    public async Task Originals_stay_in_the_store_and_expire_after_seven_days()
    {
        var root = Fixtures.Temp();
        var clock = new ManualClock();
        var store = new OriginalScreenshotStore(root, clock);
        var png = SyntheticFrames.PasswordField().Png;
        await store.SaveAsync(png, CancellationToken.None);
        Assert.Single(Directory.GetFiles(root, "*.png"));
        clock.Now = clock.Now.AddDays(7).AddSeconds(-1);
        Assert.Equal(0, store.PurgeExpired());
        clock.Now = clock.Now.AddSeconds(1);
        Assert.Equal(1, store.PurgeExpired());
        Assert.Empty(Directory.GetFiles(root, "*.png"));
        Directory.Delete(root, true);
    }

    [Fact]
    public void Credential_blobs_round_trip_and_the_windows_reader_is_a_no_op_off_windows()
    {
        var store = new WindowsCredentialStore(new MemoryReader("synthetic-credential-value"));
        Assert.Equal("synthetic-credential-value", store.Read(CredentialTargets.OpenAi));
        Assert.Null(store.Read(CredentialTargets.Linear));
        Assert.Null(store.Read("bad\nname"));
        Assert.Null(new WindowsCredentialStore(new MemoryReader(null)).Read(CredentialTargets.OpenAi));
        Assert.Null(new WindowsCredentialStore(new OddReader()).Read(CredentialTargets.OpenAi));
        if (!OperatingSystem.IsWindows()) Assert.Null(new Advapi32CredentialReader().Read(CredentialTargets.OpenAi));
        Assert.Null(UiAutomationFactory.TryLoad(typeof(MaskCredentialReportTests).Assembly.Location));
    }

    [Fact]
    public async Task Failure_report_is_json_and_markdown_and_only_mentions_the_masked_file()
    {
        var root = Fixtures.Temp();
        var raw = SyntheticFrames.PasswordField();
        var masked = ScreenshotMasker.Apply(raw.Png, [new PixelRect(8, 8, 16, 8)]);
        var report = new FailureReport
        {
            CaseId = "CASE-1",
            StepId = "step-1",
            Expected = "按钮可点击",
            Actual = "未找到控件",
            Engine = "flaui",
            ErrorType = ExecutorError.ControlNotFound,
            TimeoutSeconds = 15,
            Attempts = 2,
            NeedsHumanReview = false,
            Log = ["attempt=1 engine=flaui click"],
            Fingerprint = FailureReport.FingerprintFor("CASE-1", "step-1", ExecutorError.ControlNotFound)
        };
        var stored = await RunArtifactStore.WriteFailureAsync(root, report, masked, CancellationToken.None);
        var json = File.ReadAllText(Directory.GetFiles(root, "*.json").Single(path => !path.EndsWith(".evidence.json", StringComparison.Ordinal)));
        var markdown = File.ReadAllText(Directory.GetFiles(root, "*.md").Single());
        Assert.Contains("\"schemaVersion\":2", json, StringComparison.Ordinal);
        Assert.Contains(stored.Fingerprint, json, StringComparison.Ordinal);
        Assert.Contains("期望：按钮可点击", markdown, StringComparison.Ordinal);
        Assert.Contains("实际：未找到控件", markdown, StringComparison.Ordinal);
        Assert.Contains(stored.MaskedScreenshotFile!, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(root, markdown, StringComparison.Ordinal);
        Assert.NotEqual(raw.Png, File.ReadAllBytes(Path.Combine(root, stored.MaskedScreenshotFile!)));
        var read = RunArtifactStore.ReadFailure(root, stored.Fingerprint);
        Assert.Equal(stored.Fingerprint, read.Report.Fingerprint);
        Assert.NotNull(read.Png);
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Repeated_fingerprints_comment_and_ask_or_local_only_never_calls()
    {
        var client = new FakeLinearIssueClient();
        var exporter = new LinearFailureExporter(client);
        var report = SampleReport();
        var masked = new byte[] { 1, 2, 3, 4 };
        var ask = await exporter.ExportAsync(report, masked, Fixtures.Policy(), CancellationToken.None);
        Assert.Equal("skipped-ask", ask.Status);
        Assert.Equal(0, client.Calls);
        var local = await exporter.ExportAsync(report, masked, Fixtures.Policy(localOnly: true, export: ExportMode.Push, team: "team-1"), CancellationToken.None);
        Assert.Equal("skipped-local-only", local.Status);
        Assert.Equal(0, client.Calls);
        var push = Fixtures.Policy(computer: true, export: ExportMode.Push, team: "team-1");
        var created = await exporter.ExportAsync(report, masked, push, CancellationToken.None);
        var repeated = await exporter.ExportAsync(report, masked, push, CancellationToken.None);
        Assert.Equal("created", created.Status);
        Assert.Equal("commented", repeated.Status);
        Assert.Equal(created.IssueId, repeated.IssueId);
        Assert.Single(client.Comments);
        Assert.Equal(masked, client.CreatedImages[0]);
    }

    [Fact]
    public async Task Linear_http_client_creates_once_then_comments_and_uploads_only_masked_bytes()
    {
        var handler = new StubHandler();
        var masked = new byte[] { 9, 8, 7, 6, 5 };
        handler.Responder = (request, body) =>
        {
            if (request.Method == HttpMethod.Put)
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            if (body.Contains("fileUpload", StringComparison.Ordinal))
                return Json("""{"data":{"fileUpload":{"success":true,"uploadFile":{"uploadUrl":"https://uploads.example.test/masked","assetUrl":"https://uploads.example.test/asset"}}}}""");
            if (body.Contains("issues", StringComparison.Ordinal))
                return Json("""{"data":{"issues":{"nodes":[]}}}""");
            if (body.Contains("commentCreate", StringComparison.Ordinal))
                return Json("""{"data":{"commentCreate":{"success":true,"comment":{"id":"comment-1"}}}}""");
            Assert.Contains("CASE-1|step-1|ControlNotFound", body, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-credential-value", body, StringComparison.Ordinal);
            return Json("""{"data":{"issueCreate":{"success":true,"issue":{"id":"issue-1","identifier":"SYN-1"}}}}""");
        };
        var client = new LinearHttpIssueClient(new StaticKeys("synthetic-credential-value"), handler);
        var exporter = new LinearFailureExporter(client);
        var first = await exporter.ExportAsync(SampleReport(), masked, Fixtures.Policy(export: ExportMode.Push, team: "team-1"), CancellationToken.None);
        handler.Responder = (request, body) =>
        {
            if (body.Contains("issues", StringComparison.Ordinal))
                return Json("""{"data":{"issues":{"nodes":[{"id":"issue-1","identifier":"SYN-1","title":"自动化测试失败 CASE-1|step-1|ControlNotFound"}]}}}""");
            return Json("""{"data":{"commentCreate":{"success":true,"comment":{"id":"comment-2"}}}}""");
        };
        var second = await exporter.ExportAsync(SampleReport(), null, Fixtures.Policy(export: ExportMode.Push, team: "team-1"), CancellationToken.None);
        Assert.Equal("created", first.Status);
        Assert.Equal("commented", second.Status);
        Assert.Contains(handler.ByteBodies, bytes => bytes.AsSpan().SequenceEqual(masked));
        Assert.Contains(handler.Authorizations, value => value.Contains("synthetic-credential-value", StringComparison.Ordinal));
        Assert.All(handler.Bodies, body => Assert.DoesNotContain("synthetic-credential-value", body, StringComparison.Ordinal));
        Assert.Contains(handler.Uris, uri => uri?.Host == "uploads.example.test");
    }

    [Fact]
    public async Task Missing_linear_credential_makes_no_http_call()
    {
        var handler = new StubHandler();
        var client = new LinearHttpIssueClient(new StaticKeys(null), handler);
        var result = await new LinearFailureExporter(client).ExportAsync(SampleReport(), [1], Fixtures.Policy(export: ExportMode.Push, team: "team-1"), CancellationToken.None);
        Assert.Equal("skipped-no-credential", result.Status);
        Assert.Equal(0, handler.Calls);
    }

    private static FailureReport SampleReport() => new()
    {
        CaseId = "CASE-1",
        StepId = "step-1",
        Expected = "按钮可点击",
        Actual = "未找到控件",
        Engine = "flaui",
        ErrorType = ExecutorError.ControlNotFound,
        TimeoutSeconds = 15,
        Attempts = 2,
        NeedsHumanReview = false,
        Log = ["click"],
        Fingerprint = FailureReport.FingerprintFor("CASE-1", "step-1", ExecutorError.ControlNotFound),
        Markdown = FailureReport.ToMarkdown(new FailureReport
        {
            CaseId = "CASE-1",
            StepId = "step-1",
            Expected = "按钮可点击",
            Actual = "未找到控件",
            Engine = "flaui",
            ErrorType = ExecutorError.ControlNotFound,
            TimeoutSeconds = 15,
            Attempts = 2,
            NeedsHumanReview = false,
            Log = ["click"],
            Fingerprint = "CASE-1|step-1|ControlNotFound"
        })
    };

    private static HttpResponseMessage Json(string body)
        => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class MemoryReader(string? value) : ICredentialBlobReader
    {
        public byte[]? Read(string target)
            => target == CredentialTargets.OpenAi && value is not null ? Encoding.Unicode.GetBytes(value) : null;
    }

    private sealed class OddReader : ICredentialBlobReader
    {
        public byte[]? Read(string target) => [1];
    }

    private sealed class StaticKeys(string? value) : IApiKeyStore
    {
        public string? Read(string target) => value;
    }
}
