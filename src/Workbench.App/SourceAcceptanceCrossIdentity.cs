using System.Text;
using System.Text.Json;

namespace Workbench.App;

// Only the isolated synthetic source-acceptance lane uses this binding. It does not authorize a plugin.
internal sealed record SourceAcceptanceCrossObserved(
    string HostCommit, string FixtureZipSha256, long FixtureZipLength,
    string ProductionZipSha256, long ProductionZipLength, string HostExeSha256, string HostDllSha256,
    string SdkCorePackageSha256, long SdkCorePackageLength, string SdkWinUiPackageSha256, long SdkWinUiPackageLength,
    string HostCoreSdkSha256, string HostWinUiSdkSha256, string SdkManifestSha256);

internal sealed record SourceAcceptanceCrossBinding(
    string PluginCommit, string HostCommit, string SdkCommit, string FixtureZipSha256, string ProductionZipSha256,
    string HostExeSha256, string HostDllSha256, string SdkCorePackageSha256, string SdkWinUiPackageSha256,
    string HostCoreSdkSha256, string HostWinUiSdkSha256, string SdkManifestSha256);

internal static class SourceAcceptanceCrossIdentity
{
    private const string TestingRepository = "https://github.com/ThaoKeeton/workbench-testing";
    private const string HostRepository = "https://github.com/ThaoKeeton/project-workbench";
    private static InvalidDataException Fail() => new("SourceCrossBindingInvalid");
    private static bool Hex(string? value, int length) => value?.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString() ?? throw Fail();
    private static void Require(bool condition) { if (!condition) throw Fail(); }
    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in element.EnumerateObject()) { Require(names.Add(item.Name)); Unique(item.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Unique(item);
    }
    private static JsonDocument Parse(byte[] bytes)
    {
        Require(bytes.Length is > 0 and <= 2 * 1024 * 1024);
        _ = new UTF8Encoding(false, true).GetString(bytes);
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        try { Require(document.RootElement.ValueKind == JsonValueKind.Object); Unique(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    private static void CheckZip(JsonElement metadata, string expectedPath, string expectedHash, long expectedLength)
    {
        var zip = metadata.GetProperty("zip");
        Require(Text(zip, "path") == expectedPath && Text(zip, "sha256") == expectedHash && zip.GetProperty("length").GetInt64() == expectedLength);
    }
    private static void CheckPackage(JsonElement packages, string path, string hash, long length)
    {
        var matches = packages.EnumerateArray().Where(p => Text(p, "path") == path).ToArray();
        Require(matches.Length == 1 && Text(matches[0], "sha256") == hash && matches[0].GetProperty("length").GetInt64() == length);
    }
    private static void CheckHostBinding(JsonElement bindings, string name, string hash)
    {
        var matches = bindings.EnumerateArray().Where(b => Text(b, "name") == name).ToArray();
        Require(matches.Length == 1 && Text(matches[0], "sha256") == hash);
    }
    internal static SourceAcceptanceCrossBinding Validate(byte[] fixtureBytes, byte[] productionBytes, byte[] sdkManifestBytes, SourceAcceptanceCrossObserved observed)
    {
        try
        {
            foreach (var hash in new[] { observed.FixtureZipSha256, observed.ProductionZipSha256, observed.HostExeSha256, observed.HostDllSha256,
                observed.SdkCorePackageSha256, observed.SdkWinUiPackageSha256, observed.HostCoreSdkSha256, observed.HostWinUiSdkSha256, observed.SdkManifestSha256 }) Require(Hex(hash, 64));
            Require(Hex(observed.HostCommit, 40) && observed.FixtureZipLength > 0 && observed.ProductionZipLength > 0 && observed.SdkCorePackageLength > 0 && observed.SdkWinUiPackageLength > 0);
            using var fixtureDoc = Parse(fixtureBytes); using var productionDoc = Parse(productionBytes); using var sdkManifestDoc = Parse(sdkManifestBytes);
            var fixture = fixtureDoc.RootElement; var production = productionDoc.RootElement;
            Require(fixture.GetProperty("schemaVersion").GetInt32() == 1 && production.GetProperty("schemaVersion").GetInt32() == 1 &&
                Text(fixture, "profile") == "SourceAcceptance" && Text(fixture, "pluginId") == "com.projectworkbench.testing.source-acceptance" &&
                Text(production, "pluginId") == "com.projectworkbench.testing" && Text(fixture, "version") == "0.2.0" && Text(production, "version") == "0.2.0" &&
                fixture.GetProperty("apiVersion").GetInt32() == 1 && Text(fixture, "minimumHostVersion") == "0.4.0" && Text(production, "minimumHostVersion") == "0.4.0");
            Require(Text(fixture, "sourceRepository") == TestingRepository && Text(production, "sourceRepository") == TestingRepository &&
                Text(fixture, "hostRepository") == HostRepository && Text(production, "hostRepository") == HostRepository &&
                !fixture.GetProperty("sourceTreeDirty").GetBoolean() && !production.GetProperty("sourceTreeDirty").GetBoolean() &&
                !fixture.GetProperty("referenceOnly").GetBoolean() && !production.GetProperty("referenceOnly").GetBoolean());
            string pluginCommit = Text(fixture, "sourceCommit"), hostCommit = Text(fixture, "hostSourceCommit"), sdkCommit = Text(fixture, "sdkSourceCommit");
            Require(Hex(pluginCommit, 40) && Hex(hostCommit, 40) && Hex(sdkCommit, 40) && pluginCommit == Text(production, "sourceCommit") && hostCommit == observed.HostCommit);
            var host = fixture.GetProperty("host"); var sdk = fixture.GetProperty("sdk");
            Require(JsonElement.DeepEquals(host, production.GetProperty("host")) && JsonElement.DeepEquals(sdk, production.GetProperty("sdk")) && JsonElement.DeepEquals(sdk, sdkManifestDoc.RootElement) &&
                Text(fixture, "sdkManifestSha256") == observed.SdkManifestSha256 && Text(production, "sdkManifestSha256") == observed.SdkManifestSha256 &&
                Text(host, "sourceCommit") == hostCommit && Text(host, "executableSha256") == observed.HostExeSha256 && Text(host, "dllSha256") == observed.HostDllSha256 &&
                Text(sdk, "repository") == HostRepository + ".git" && Text(sdk, "sourceCommit") == sdkCommit && Text(sdk, "sdkVersion") == "0.4.0" && !sdk.GetProperty("referenceOnly").GetBoolean());
            var packages = sdk.GetProperty("packages"); Require(packages.ValueKind == JsonValueKind.Array && packages.GetArrayLength() == 2);
            CheckPackage(packages, "packages/Workbench.Sdk.0.4.0.nupkg", observed.SdkCorePackageSha256, observed.SdkCorePackageLength);
            CheckPackage(packages, "packages/Workbench.WinUI.Sdk.0.4.0.nupkg", observed.SdkWinUiPackageSha256, observed.SdkWinUiPackageLength);
            CheckZip(fixture, "workbench-testing-source-acceptance-0.2.0.zip", observed.FixtureZipSha256, observed.FixtureZipLength);
            CheckZip(production, "workbench-testing-plugin-0.2.0.zip", observed.ProductionZipSha256, observed.ProductionZipLength);
            Require(Text(fixture, "pairedProductionSha256") == observed.ProductionZipSha256);
            var bindings = fixture.GetProperty("managed").GetProperty("hostBindings");
            CheckHostBinding(bindings, "Workbench.Sdk", observed.HostCoreSdkSha256);
            CheckHostBinding(bindings, "Workbench.WinUI.Sdk", observed.HostWinUiSdkSha256);
            return new(pluginCommit, hostCommit, sdkCommit, observed.FixtureZipSha256, observed.ProductionZipSha256, observed.HostExeSha256, observed.HostDllSha256,
                observed.SdkCorePackageSha256, observed.SdkWinUiPackageSha256, observed.HostCoreSdkSha256, observed.HostWinUiSdkSha256, observed.SdkManifestSha256);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { throw Fail(); }
    }
}
