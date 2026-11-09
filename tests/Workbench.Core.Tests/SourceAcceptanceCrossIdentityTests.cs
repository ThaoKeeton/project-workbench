using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workbench.App;
using Xunit;

namespace Workbench.Core.Tests;

public sealed class SourceAcceptanceCrossIdentityTests
{
    private static readonly string Plugin = new('a', 40), Host = new('b', 40), Sdk = new('c', 40);
    private static string H(char c) => new(c, 64);
    private static readonly SourceAcceptanceCrossObserved Observed = new(
        Host, H('1'), 101, H('2'), 102, H('3'), H('4'), H('5'), 105, H('6'), 106, H('7'), H('8'), H('9'));

    private static (JsonObject Fixture, JsonObject Production) Documents()
    {
        var host = new JsonObject { ["sourceCommit"] = Host, ["executableSha256"] = H('3'), ["dllSha256"] = H('4') };
        var sdk = new JsonObject {
            ["repository"] = "https://github.com/ThaoKeeton/project-workbench.git", ["sourceCommit"] = Sdk,
            ["sdkVersion"] = "0.4.0", ["referenceOnly"] = false,
            ["packages"] = new JsonArray {
                new JsonObject { ["path"] = "packages/Workbench.Sdk.0.4.0.nupkg", ["length"] = 105, ["sha256"] = H('5') },
                new JsonObject { ["path"] = "packages/Workbench.WinUI.Sdk.0.4.0.nupkg", ["length"] = 106, ["sha256"] = H('6') }
            }
        };
        var production = new JsonObject {
            ["schemaVersion"] = 1, ["pluginId"] = "com.projectworkbench.testing", ["version"] = "0.2.0", ["minimumHostVersion"] = "0.4.0",
            ["sourceRepository"] = "https://github.com/ThaoKeeton/workbench-testing", ["hostRepository"] = "https://github.com/ThaoKeeton/project-workbench",
            ["sourceCommit"] = Plugin, ["sourceTreeDirty"] = false, ["referenceOnly"] = false,
            ["host"] = host.DeepClone(), ["sdk"] = sdk.DeepClone(), ["sdkManifestSha256"] = H('9'),
            ["zip"] = new JsonObject { ["path"] = "workbench-testing-plugin-0.2.0.zip", ["length"] = 102, ["sha256"] = H('2') }
        };
        var fixture = new JsonObject {
            ["schemaVersion"] = 1, ["profile"] = "SourceAcceptance", ["pluginId"] = "com.projectworkbench.testing.source-acceptance",
            ["version"] = "0.2.0", ["apiVersion"] = 1, ["minimumHostVersion"] = "0.4.0",
            ["sourceRepository"] = "https://github.com/ThaoKeeton/workbench-testing", ["hostRepository"] = "https://github.com/ThaoKeeton/project-workbench",
            ["sourceCommit"] = Plugin, ["sourceTreeDirty"] = false, ["referenceOnly"] = false, ["hostSourceCommit"] = Host,
            ["sdkSourceCommit"] = Sdk, ["host"] = host.DeepClone(), ["sdk"] = sdk.DeepClone(), ["sdkManifestSha256"] = H('9'), ["pairedProductionSha256"] = H('2'),
            ["zip"] = new JsonObject { ["path"] = "workbench-testing-source-acceptance-0.2.0.zip", ["length"] = 101, ["sha256"] = H('1') },
            ["managed"] = new JsonObject { ["hostBindings"] = new JsonArray {
                new JsonObject { ["name"] = "Workbench.Sdk", ["sha256"] = H('7') },
                new JsonObject { ["name"] = "Workbench.WinUI.Sdk", ["sha256"] = H('8') }
            }}
        };
        return (fixture, production);
    }

    private static byte[] Bytes(JsonNode value) => JsonSerializer.SerializeToUtf8Bytes(value);

    [Fact]
    public void Independent_host_plugin_and_sdk_commits_are_bound_to_observed_bytes()
    {
        var (fixture, production) = Documents();
        var result = SourceAcceptanceCrossIdentity.Validate(Bytes(fixture), Bytes(production), Bytes(production["sdk"]!), Observed);
        Assert.Equal(Plugin, result.PluginCommit);
        Assert.Equal(Host, result.HostCommit);
        Assert.Equal(Sdk, result.SdkCommit);
        Assert.Equal(H('1'), result.FixtureZipSha256);
        Assert.Equal(H('2'), result.ProductionZipSha256);
        Assert.Equal(H('5'), result.SdkCorePackageSha256);
        Assert.Equal(H('8'), result.HostWinUiSdkSha256);
    }

    [Theory]
    [InlineData("plugin-commit")]
    [InlineData("host-commit")]
    [InlineData("sdk-commit")]
    [InlineData("fixture-zip")]
    [InlineData("production-zip")]
    [InlineData("host-executable")]
    [InlineData("host-dll")]
    [InlineData("sdk-package")]
    [InlineData("sdk-host-dll")]
    [InlineData("sdk-package-length")]
    [InlineData("sdk-manifest-source")]
    [InlineData("repository")]
    [InlineData("dirty-source")]
    [InlineData("fixture-identity")]
    [InlineData("duplicate-sdk")]
    public void Refuses_a_mismatched_or_ambiguous_binding(string mutation)
    {
        var (f, p) = Documents();
        var manifest = Bytes(p["sdk"]!);
        switch (mutation)
        {
            case "plugin-commit": f["sourceCommit"] = new string('d', 40); break;
            case "host-commit": f["hostSourceCommit"] = new string('d', 40); break;
            case "sdk-commit": f["sdkSourceCommit"] = new string('d', 40); break;
            case "fixture-zip": f["zip"]!["sha256"] = H('9'); break;
            case "production-zip": f["pairedProductionSha256"] = H('9'); break;
            case "host-executable": f["host"]!["executableSha256"] = H('9'); p["host"]!["executableSha256"] = H('9'); break;
            case "host-dll": f["host"]!["dllSha256"] = H('9'); p["host"]!["dllSha256"] = H('9'); break;
            case "sdk-package": f["sdk"]!["packages"]![0]!["sha256"] = H('9'); p["sdk"]!["packages"]![0]!["sha256"] = H('9'); break;
            case "sdk-host-dll": f["managed"]!["hostBindings"]![0]!["sha256"] = H('9'); break;
            case "sdk-package-length": f["sdk"]!["packages"]![0]!["length"] = 999; p["sdk"]!["packages"]![0]!["length"] = 999; break;
            case "sdk-manifest-source": f["sdkSourceCommit"] = new string('d', 40); f["sdk"]!["sourceCommit"] = new string('d', 40); p["sdk"]!["sourceCommit"] = new string('d', 40); break;
            case "repository": f["sourceRepository"] = "https://example.invalid/other"; break;
            case "dirty-source": p["sourceTreeDirty"] = true; break;
            case "fixture-identity": f["pluginId"] = "com.example.invalid"; break;
            case "duplicate-sdk": f["sdk"]!["packages"]!.AsArray().Add(f["sdk"]!["packages"]![0]!.DeepClone()); p["sdk"]!["packages"]!.AsArray().Add(p["sdk"]!["packages"]![0]!.DeepClone()); break;
        }
        var error = Assert.Throws<InvalidDataException>(() => SourceAcceptanceCrossIdentity.Validate(Bytes(f), Bytes(p), manifest, Observed));
        Assert.Equal("SourceCrossBindingInvalid", error.Message);
    }

    [Fact]
    public void Refuses_case_insensitive_duplicate_json_fields()
    {
        var (fixture, production) = Documents();
        var text = Encoding.UTF8.GetString(Bytes(fixture)).Insert(1, "\"SOURCECOMMIT\":\"" + Plugin + "\",");
        Assert.Throws<InvalidDataException>(() => SourceAcceptanceCrossIdentity.Validate(Encoding.UTF8.GetBytes(text), Bytes(production), Bytes(production["sdk"]!), Observed));
    }
}
