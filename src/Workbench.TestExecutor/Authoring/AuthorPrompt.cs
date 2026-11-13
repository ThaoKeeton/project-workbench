using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Workbench.TestExecutor;

public sealed record AuthorRequest(string Goal, string DomText, string SceneSnapshot, string? FailedStep = null, string? DiffText = null);

public static class AuthorPrompt
{
    public const string Rules = """
        只输出一个 JSON 对象，不要输出解释。
        schema 必须是 workbench.test-executor.authored-script.v1，schemaVersion 必须是 1，并保持对 schema 1 的兼容。
        步骤 type 只能是 web 或 scene3d。action 只能是执行器已经实现的动作：click、type、set-value、invoke、expand、wait-for、read、assert-text、assert-visible、assert-network、screenshot、open-file、switch-window、read-scene、project、drag、orbit、assert-scene、probe、snapshot、diff、wheel、compare-pixels。
        web 步骤走 CDP。scene3d 步骤用对象名、uuid 或路径定位，沿 world-path 做路径切削，并用体积、顶点数、包围盒做数值断言。
        不要要求修改目标应用。不要添加测试钩子。
        不要输出截图、图像或像素。截图只留在本机。
        场景输入是 workbench.test-executor.scene-snapshot.v1 的文字，schemaVersion 为 1。
        """;

    public static bool ContainsImage(string? value)
        => value is not null
           && (value.Contains("data:image", StringComparison.OrdinalIgnoreCase)
               || value.Contains("input_image", StringComparison.OrdinalIgnoreCase)
               || value.Contains("image_url", StringComparison.OrdinalIgnoreCase));

    public static string Build(AuthorRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Rules.Trim());
        builder.AppendLine("目标:");
        builder.AppendLine(TextRules.Clip(request.Goal, 500));
        builder.AppendLine("DOM:");
        builder.AppendLine(TextRules.Clip(request.DomText, 8000));
        builder.AppendLine("SCENE:");
        builder.AppendLine(SceneForPrompt(request.SceneSnapshot));
        if (!string.IsNullOrWhiteSpace(request.FailedStep))
        {
            builder.AppendLine("失败步骤:");
            builder.AppendLine(TextRules.Clip(request.FailedStep, 2000));
            builder.AppendLine("差异:");
            builder.AppendLine(TextRules.Clip(request.DiffText, 8000));
        }
        return builder.ToString();
    }

    public static string SceneForPrompt(string snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot) || ContainsImage(snapshot)) return "";
        var normalized = SceneSnapshot.Normalize(snapshot);
        if (ContainsImage(normalized)) return "";
        var text = SceneSnapshot.TextOf(normalized);
        var boxes = Boxes(normalized);
        var body = string.IsNullOrEmpty(text) ? normalized : text + "\n" + boxes + "\n" + normalized;
        return TextRules.Clip(body, 12000);
    }

    private static string Boxes(string json)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }
        var source = root?["objects"] as JsonArray ?? root?["meshes"] as JsonArray;
        if (source is null) return "";
        var builder = new StringBuilder();
        foreach (var item in source)
        {
            if (item is not JsonObject obj || obj["min"] is not JsonObject min || obj["max"] is not JsonObject max) continue;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(Read(obj, "path"));
            if (builder.Length == 0 || builder[^1] == '\n') builder.Append(Read(obj, "name"));
            builder.Append(" uuid=").Append(Read(obj, "uuid"));
            builder.Append(" boxMin=(").Append(Num(min, "x")).Append(", ").Append(Num(min, "y")).Append(", ").Append(Num(min, "z")).Append(')');
            builder.Append(" boxMax=(").Append(Num(max, "x")).Append(", ").Append(Num(max, "y")).Append(", ").Append(Num(max, "z")).Append(')');
            builder.Append(" verts=").Append(Read(obj, "vertexCount"));
            builder.Append(" vol=").Append(Read(obj, "volume"));
            builder.Append(" hash=").Append(Read(obj, "hash"));
        }
        return builder.ToString();
    }

    private static string Read(JsonObject obj, string name)
    {
        var node = obj[name];
        if (node is null) return "";
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        return node.ToJsonString();
    }

    private static string Num(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value) return "0";
        if (value.TryGetValue<double>(out var number)) return number.ToString("0.###", CultureInfo.InvariantCulture);
        if (value.TryGetValue<int>(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
        return "0";
    }
}
