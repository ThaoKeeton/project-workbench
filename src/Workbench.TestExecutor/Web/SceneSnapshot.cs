using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Workbench.TestExecutor;

public static class SceneSnapshot
{
    public const string Schema = "workbench.test-executor.scene-snapshot.v1";
    public const int Version = 1;

    public static string Normalize(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }
        if (node is not JsonObject root) return json;
        if (root["schema"]?.GetValue<string>() == Schema) return json;
        var source = root["nodes"] as JsonArray ?? root["meshes"] as JsonArray ?? new JsonArray();
        var meshes = new JsonArray();
        foreach (var item in source)
        {
            if (item is not JsonObject obj) continue;
            var type = obj["type"]?.GetValue<string>();
            if (type is "Mesh" || obj["vertexCount"] is not null)
                meshes.Add(obj.DeepClone());
        }
        var mount = root["mount"]?.GetValue<string>() ?? "unknown";
        var library = meshes.Count > 0 || mount is "devtools" or "r3f" or "window" ? "three" : "none";
        var output = new JsonObject
        {
            ["schema"] = Schema,
            ["schemaVersion"] = Version,
            ["library"] = library,
            ["mount"] = mount,
            ["camera"] = root["camera"]?.DeepClone() ?? new JsonObject(),
            ["meshes"] = meshes,
            ["objects"] = source.DeepClone(),
            ["diagnostics"] = root["diagnostics"]?.DeepClone() ?? new JsonArray()
        };
        if (root["webgl"] is not null) output["webgl"] = root["webgl"]!.DeepClone();
        output["text"] = Describe(output);
        return output.ToJsonString();
    }

    public static string AttachDiff(string snapshotJson, string diffJson)
    {
        try
        {
            if (JsonNode.Parse(snapshotJson) is not JsonObject snapshot || JsonNode.Parse(diffJson) is not JsonNode diff)
                return snapshotJson;
            snapshot["actionDiff"] = diff;
            return snapshot.ToJsonString();
        }
        catch (JsonException)
        {
            return snapshotJson;
        }
    }

    public static string TextOf(string json)
    {
        try
        {
            var node = JsonNode.Parse(json) as JsonObject;
            if (node?["text"]?.GetValue<string>() is { Length: > 0 } text) return text;
        }
        catch (JsonException)
        {
            return "";
        }
        return "";
    }

    private static string Describe(JsonObject snapshot)
    {
        var builder = new StringBuilder();
        builder.Append("library=").Append(Value(snapshot, "library"));
        builder.Append(" mount=").Append(Value(snapshot, "mount"));
        if (snapshot["objects"] is JsonArray objects)
        {
            foreach (var item in objects)
            {
                if (item is not JsonObject obj) continue;
                builder.Append('\n').Append(Value(obj, "path"));
                builder.Append(" type=").Append(Value(obj, "type"));
                builder.Append(" uuid=").Append(Value(obj, "uuid"));
                builder.Append(" visible=").Append(Value(obj, "visible"));
                AppendVec(builder, " pos", obj["position"] as JsonObject);
                AppendVec(builder, " rot", obj["rotation"] as JsonObject);
                if (obj["vertexCount"] is not null)
                {
                    builder.Append(" verts=").Append(Value(obj, "vertexCount"));
                    builder.Append(" faces=").Append(Value(obj, "faceCount"));
                    builder.Append(" vol=").Append(Value(obj, "volume"));
                    builder.Append(" hash=").Append(Value(obj, "hash"));
                }
            }
        }
        return builder.ToString();
    }

    private static void AppendVec(StringBuilder builder, string label, JsonObject? point)
    {
        builder.Append(label).Append("=(");
        builder.Append(Number(point, "x")).Append(", ");
        builder.Append(Number(point, "y")).Append(", ");
        builder.Append(Number(point, "z")).Append(')');
    }

    private static string Number(JsonObject? obj, string name)
    {
        if (obj?[name] is not JsonValue value || !value.TryGetValue<double>(out var number)) return "0";
        return number.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Value(JsonObject obj, string name)
    {
        var node = obj[name];
        if (node is null) return "";
        if (node is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        return node.ToString();
    }
}

public static class SceneDiff
{
    public const string Schema = "workbench.test-executor.scene-diff.v1";
    public const int Version = 1;

    public static string Compare(string beforeJson, string afterJson, double tolerance)
    {
        var before = Index(beforeJson);
        var after = Index(afterJson);
        var added = new JsonArray();
        var removed = new JsonArray();
        var moved = new JsonArray();
        var deformed = new JsonArray();
        foreach (var pair in after)
        {
            if (!before.TryGetValue(pair.Key, out var prior))
            {
                added.Add(pair.Value.Brief());
                continue;
            }
            if (Moved(prior, pair.Value, tolerance)) moved.Add(pair.Value.Brief());
            if (Deformed(prior, pair.Value, tolerance)) deformed.Add(pair.Value.Brief());
        }
        foreach (var pair in before)
        {
            if (!after.ContainsKey(pair.Key)) removed.Add(pair.Value.Brief());
        }
        var output = new JsonObject
        {
            ["schema"] = Schema,
            ["schemaVersion"] = Version,
            ["counts"] = new JsonObject
            {
                ["added"] = added.Count,
                ["removed"] = removed.Count,
                ["moved"] = moved.Count,
                ["deformed"] = deformed.Count
            },
            ["added"] = added,
            ["removed"] = removed,
            ["moved"] = moved,
            ["deformed"] = deformed,
            ["text"] = "added " + added.Count.ToString(CultureInfo.InvariantCulture)
                + ", removed " + removed.Count.ToString(CultureInfo.InvariantCulture)
                + ", moved " + moved.Count.ToString(CultureInfo.InvariantCulture)
                + ", deformed " + deformed.Count.ToString(CultureInfo.InvariantCulture)
        };
        return output.ToJsonString();
    }

    private static bool Moved(MeshSnap prior, MeshSnap next, double tolerance)
        => Math.Abs(prior.X - next.X) > tolerance || Math.Abs(prior.Y - next.Y) > tolerance || Math.Abs(prior.Z - next.Z) > tolerance;

    private static bool Deformed(MeshSnap prior, MeshSnap next, double tolerance)
        => prior.Hash != next.Hash
            || prior.Vertices != next.Vertices
            || prior.Faces != next.Faces
            || Math.Abs(prior.Volume - next.Volume) > tolerance
            || Math.Abs(prior.Rx - next.Rx) > tolerance
            || Math.Abs(prior.Ry - next.Ry) > tolerance
            || Math.Abs(prior.Rz - next.Rz) > tolerance;

    private static Dictionary<string, MeshSnap> Index(string json)
    {
        var map = new Dictionary<string, MeshSnap>(StringComparer.Ordinal);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(SceneSnapshot.Normalize(json));
        }
        catch (JsonException)
        {
            return map;
        }
        if (node is not JsonObject root || root["meshes"] is not JsonArray meshes) return map;
        foreach (var item in meshes)
        {
            if (item is not JsonObject obj) continue;
            var snap = MeshSnap.Read(obj);
            if (snap.Key.Length == 0 || map.ContainsKey(snap.Key)) continue;
            map[snap.Key] = snap;
        }
        return map;
    }

    private sealed record MeshSnap(string Key, string Name, string Uuid, string Path, double X, double Y, double Z, double Rx, double Ry, double Rz, double Volume, int Vertices, int Faces, string Hash)
    {
        public JsonObject Brief() => new()
        {
            ["name"] = Name,
            ["uuid"] = Uuid,
            ["path"] = Path
        };

        public static MeshSnap Read(JsonObject obj)
        {
            var uuid = Text(obj, "uuid");
            var name = Text(obj, "name");
            var position = obj["position"] as JsonObject;
            var rotation = obj["rotation"] as JsonObject;
            return new MeshSnap(
                uuid.Length > 0 ? uuid : name,
                name,
                uuid,
                Text(obj, "path"),
                Number(position, "x"),
                Number(position, "y"),
                Number(position, "z"),
                Number(rotation, "x"),
                Number(rotation, "y"),
                Number(rotation, "z"),
                Number(obj, "volume"),
                Whole(obj, "vertexCount"),
                Whole(obj, "faceCount"),
                Text(obj, "hash"));
        }

        private static string Text(JsonObject obj, string name)
        {
            if (obj[name] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
            return "";
        }

        private static double Number(JsonObject? obj, string name)
        {
            if (obj?[name] is JsonValue value && value.TryGetValue<double>(out var number)) return number;
            return 0;
        }

        private static int Whole(JsonObject obj, string name)
        {
            if (obj[name] is JsonValue value && value.TryGetValue<int>(out var number)) return number;
            if (obj[name] is JsonValue other && other.TryGetValue<double>(out var wide)) return (int)wide;
            return 0;
        }
    }
}

public static class SceneProbeCli
{
    public static async Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null, CancellationToken cancellationToken = default)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        var port = 0;
        var file = Path.Combine(Directory.GetCurrentDirectory(), "scene-probe.json");
        var title = (string?)null;
        var reload = false;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg == "--reload")
            {
                reload = true;
                continue;
            }
            if (index + 1 >= args.Length) return 2;
            var value = args[++index];
            switch (arg)
            {
                case "--port":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port)) return 2;
                    break;
                case "--out":
                    file = value;
                    break;
                case "--title":
                    title = value;
                    break;
                default:
                    return 2;
            }
        }
        if (port is < 1 or > 65535) return 2;
        var session = await CdpWebSession.ConnectAsync(port, title, null, cancellationToken);
        if (session is null)
        {
            await error.WriteLineAsync("debug-port-unreachable");
            return 1;
        }
        await using (session)
        {
            await session.InstallSceneHookAsync(reload, cancellationToken);
            var report = await session.ProbeAsync(cancellationToken);
            var full = Path.GetFullPath(file);
            await File.WriteAllTextAsync(full, report, cancellationToken);
            await output.WriteLineAsync(full);
        }
        return 0;
    }
}

public static class SceneProbeReport
{
    public const string Schema = "workbench.test-executor.scene-probe.v1";
    public const int Version = 1;
    private static readonly string[] SceneFields = ["tree", "name", "uuid", "transform", "boundingBox", "vertexCount", "faceCount", "volume", "hash", "visibility"];
    private static readonly string[] WebGlFields = ["drawCalls", "vertices", "bufferBytes"];

    public static string FromSignals(string json)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }
        root ??= new JsonObject();
        var signals = root["signals"] as JsonObject ?? root;
        var devtools = Whole(signals, "devtoolsScenes");
        var fiber = Whole(signals, "r3fCanvases");
        var onWindow = Flag(signals, "windowScene") || Flag(signals, "windowCamera") || Flag(signals, "windowRenderer");
        var contexts = Whole(signals, "webglContexts");
        var draws = Whole(signals, "drawCalls");
        var globals = signals["globals"] as JsonObject ?? new JsonObject
        {
            ["THREE"] = false,
            ["scene"] = false,
            ["workbenchHook"] = false
        };
        var diagnostics = new JsonArray();
        if (root["diagnostics"] is JsonArray notes)
        {
            foreach (var note in notes)
                if (note is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0)
                    diagnostics.Add(text);
        }
        string recommended;
        string library;
        string[] available;
        if (devtools > 0)
        {
            recommended = "devtools";
            library = "three";
            available = SceneFields;
        }
        else if (fiber > 0)
        {
            recommended = "r3f";
            library = "three";
            available = SceneFields;
        }
        else if (onWindow)
        {
            recommended = "window";
            library = "three";
            available = SceneFields;
        }
        else if (contexts > 0 || draws > 0)
        {
            recommended = "webgl";
            library = "webgl";
            available = WebGlFields;
            diagnostics.Add("no three.js scene was observed; WebGL counters are the fallback");
        }
        else
        {
            recommended = "none";
            library = "none";
            available = [];
            diagnostics.Add("no three.js scene on the devtools hook, window, or canvas.__r3f, and no WebGL context was wrapped");
        }
        var listed = new JsonArray();
        foreach (var field in available) listed.Add(field);
        var output = new JsonObject
        {
            ["schema"] = Schema,
            ["schemaVersion"] = Version,
            ["recommended"] = recommended,
            ["globals"] = globals.DeepClone(),
            ["library"] = library,
            ["available"] = listed,
            ["diagnostics"] = diagnostics,
            ["signals"] = signals.DeepClone()
        };
        output["text"] = "recommended=" + recommended + " library=" + library + " available=" + string.Join(",", available);
        return output.ToJsonString();
    }

    private static int Whole(JsonObject obj, string name)
    {
        if (obj[name] is JsonValue value && value.TryGetValue<int>(out var number)) return number;
        if (obj[name] is JsonValue other && other.TryGetValue<double>(out var wide)) return (int)wide;
        return 0;
    }

    private static bool Flag(JsonObject obj, string name)
        => obj[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
