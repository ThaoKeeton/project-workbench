using System.Globalization;
using System.Text.Json;

namespace Workbench.TestExecutor;

public readonly record struct SceneMouse(string Type, double X, double Y);

/// <summary>
/// Projects a scene point to a viewport position, then builds the press, move, and release sequence.
/// The page applies the drag; this type only describes the pointer path.
/// </summary>
public static class ScenePointer
{
    public static IReadOnlyList<SceneMouse> Drag(double x, double y, double dx, double dy)
    {
        var midX = x + dx / 2;
        var midY = y + dy / 2;
        return
        [
            new SceneMouse("mousePressed", x, y),
            new SceneMouse("mouseMoved", midX, midY),
            new SceneMouse("mouseMoved", x + dx, y + dy),
            new SceneMouse("mouseReleased", x + dx, y + dy)
        ];
    }

    public static (double Dx, double Dy) ParseDelta(string? input)
    {
        if (input is not { Length: > 0 }) return (80, 0);
        var parts = input.Split(',', 2);
        if (parts.Length != 2) return (80, 0);
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var dx)) return (80, 0);
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dy)) return (80, 0);
        return (dx, dy);
    }
}

/// <summary>
/// Compares camera and mesh numbers read over CDP. Clauses look like <c>camera.y=4±0.2</c> or <c>mesh.cube.size=2</c>.
/// </summary>
public static class SceneAssert
{
    public static bool Matches(string actualJson, string expected, double defaultTolerance, out string detail)
    {
        detail = actualJson;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(actualJson);
        }
        catch (JsonException)
        {
            detail = "scene-unreadable";
            return false;
        }
        using (document)
        {
            var failures = new List<string>();
            foreach (var clause in expected.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var split = clause.Split('=', 2);
                if (split.Length != 2)
                {
                    failures.Add(clause);
                    continue;
                }
                var path = split[0].Trim();
                var (wanted, tolerance) = ParseSpec(split[1], defaultTolerance);
                if (!TryNumber(document.RootElement, path, out var actual) || Math.Abs(actual - wanted) > tolerance)
                    failures.Add(path + "=" + actual.ToString("0.###", CultureInfo.InvariantCulture));
            }
            if (failures.Count == 0) return true;
            detail = string.Join("; ", failures);
            return false;
        }
    }

    private static (double Wanted, double Tolerance) ParseSpec(string spec, double defaultTolerance)
    {
        var text = spec.Trim();
        var mark = text.IndexOf('±');
        if (mark > 0
            && double.TryParse(text[..mark], NumberStyles.Float, CultureInfo.InvariantCulture, out var wanted)
            && double.TryParse(text[(mark + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var tolerance))
            return (wanted, tolerance);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out wanted))
            return (wanted, defaultTolerance);
        return (double.NaN, defaultTolerance);
    }

    private static bool TryNumber(JsonElement root, string path, out double value)
    {
        value = 0;
        var parts = path.Split('.');
        if (parts.Length >= 3 && parts[0] == "mesh")
        {
            if (!TryMesh(root, parts[1], out var mesh)) return false;
            if (parts[2] == "size")
            {
                if (!TryVector(mesh, "min", out var min) || !TryVector(mesh, "max", out var max)) return false;
                value = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
                return true;
            }
            if (parts[2] == "vertex" && parts.Length == 5 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                return TryVertex(mesh, index, parts[4], out value);
            if (parts.Length == 4)
                return TryAxis(mesh, parts[2], parts[3], out value);
            return false;
        }
        JsonElement current = root;
        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) return false;
        }
        return current.TryGetDouble(out value);
    }

    private static bool TryMesh(JsonElement root, string name, out JsonElement mesh)
    {
        mesh = default;
        if (!root.TryGetProperty("meshes", out var meshes) || meshes.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in meshes.EnumerateArray())
        {
            if (item.TryGetProperty("name", out var found) && found.GetString() == name)
            {
                mesh = item;
                return true;
            }
        }
        return false;
    }

    private static bool TryVector(JsonElement mesh, string name, out (double X, double Y, double Z) vector)
    {
        vector = default;
        if (!mesh.TryGetProperty(name, out var point)) return false;
        if (!point.TryGetProperty("x", out var x) || !x.TryGetDouble(out var xv)) return false;
        if (!point.TryGetProperty("y", out var y) || !y.TryGetDouble(out var yv)) return false;
        if (!point.TryGetProperty("z", out var z) || !z.TryGetDouble(out var zv)) return false;
        vector = (xv, yv, zv);
        return true;
    }

    private static bool TryAxis(JsonElement mesh, string corner, string axis, out double value)
    {
        value = 0;
        return mesh.TryGetProperty(corner, out var point)
            && point.TryGetProperty(axis, out var number)
            && number.TryGetDouble(out value);
    }

    private static bool TryVertex(JsonElement mesh, int index, string axis, out double value)
    {
        value = 0;
        if (!mesh.TryGetProperty("vertices", out var vertices) || vertices.ValueKind != JsonValueKind.Array) return false;
        var current = 0;
        foreach (var vertex in vertices.EnumerateArray())
        {
            if (current == index)
                return vertex.TryGetProperty(axis, out var number) && number.TryGetDouble(out value);
            current++;
        }
        return false;
    }
}
