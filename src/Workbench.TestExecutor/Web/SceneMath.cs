using System.Globalization;
using System.Text.Json;

namespace Workbench.TestExecutor;

public readonly record struct SceneMouse(string Type, double X, double Y, double DeltaX = 0, double DeltaY = 0);

/// <summary>
/// Canvas box measured in the page. CDP mouse events use CSS pixels in the layout viewport.
/// A device-pixel box is divided by <see cref="ViewportFrame.DevicePixelRatio"/> and window zoom.
/// A CSS box already matches those events, so the ratio and zoom are recorded and not applied twice.
/// Visual-viewport offsets shift the origin when the layout viewport is scrolled inside a zoomed view.
/// </summary>
public readonly record struct ViewportFrame(
    double Left,
    double Top,
    double Width,
    double Height,
    double DevicePixelRatio,
    double Zoom,
    double OffsetLeft,
    double OffsetTop,
    bool BoxIsDevicePixels);

public static class SceneProjection
{
    public static (double X, double Y) ToViewport(double ndcX, double ndcY, ViewportFrame frame)
    {
        var zoom = frame.Zoom == 0 ? 1 : frame.Zoom;
        var ratio = frame.DevicePixelRatio == 0 ? 1 : frame.DevicePixelRatio;
        var scale = frame.BoxIsDevicePixels ? ratio * zoom : 1d;
        if (scale == 0) scale = 1;
        var left = frame.Left / scale - frame.OffsetLeft;
        var top = frame.Top / scale - frame.OffsetTop;
        var width = frame.Width / scale;
        var height = frame.Height / scale;
        return (left + (ndcX + 1) * 0.5 * width, top + (1 - ndcY) * 0.5 * height);
    }

    public static bool TryRead(JsonElement value, out double ndcX, out double ndcY, out ViewportFrame frame)
    {
        ndcX = 0;
        ndcY = 0;
        frame = default;
        if (!Try(value, "ndcX", out ndcX) || !Try(value, "ndcY", out ndcY)) return false;
        if (!Try(value, "left", out var left) || !Try(value, "top", out var top) || !Try(value, "width", out var width) || !Try(value, "height", out var height))
            return false;
        Try(value, "dpr", out var ratio);
        Try(value, "zoom", out var zoom);
        Try(value, "offsetLeft", out var offsetLeft);
        Try(value, "offsetTop", out var offsetTop);
        var device = value.TryGetProperty("deviceBox", out var box) && box.ValueKind == JsonValueKind.True;
        frame = new ViewportFrame(left, top, width, height, ratio == 0 ? 1 : ratio, zoom == 0 ? 1 : zoom, offsetLeft, offsetTop, device);
        return width > 0 && height > 0;
    }

    public static bool TryReadFrame(JsonElement value, out ViewportFrame frame)
    {
        frame = default;
        if (!Try(value, "left", out var left) || !Try(value, "top", out var top) || !Try(value, "width", out var width) || !Try(value, "height", out var height))
            return false;
        Try(value, "dpr", out var ratio);
        Try(value, "zoom", out var zoom);
        Try(value, "offsetLeft", out var offsetLeft);
        Try(value, "offsetTop", out var offsetTop);
        var device = value.TryGetProperty("deviceBox", out var box) && box.ValueKind == JsonValueKind.True;
        frame = new ViewportFrame(left, top, width, height, ratio == 0 ? 1 : ratio, zoom == 0 ? 1 : zoom, offsetLeft, offsetTop, device);
        return width > 0 && height > 0;
    }

    public static bool TryReadPoints(JsonElement value, out IReadOnlyList<(double X, double Y)> points)
    {
        points = [];
        if (!TryReadFrame(value, out var frame) || !value.TryGetProperty("points", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() == 0)
            return false;
        var mapped = new List<(double X, double Y)>();
        foreach (var item in list.EnumerateArray())
        {
            if (!Try(item, "ndcX", out var ndcX) || !Try(item, "ndcY", out var ndcY)) return false;
            mapped.Add(ToViewport(ndcX, ndcY, frame));
        }
        points = mapped;
        return true;
    }

    public static bool IsOccluded(JsonElement value, out string detail)
    {
        detail = "";
        if (!value.TryGetProperty("hit", out var hit) || hit.ValueKind != JsonValueKind.False) return false;
        detail = value.TryGetProperty("actual", out var actual) && actual.ValueKind == JsonValueKind.String
            ? actual.GetString() ?? "occluded"
            : "occluded";
        return true;
    }

    private static bool Try(JsonElement value, string name, out double number)
    {
        number = 0;
        return value.TryGetProperty(name, out var property) && property.TryGetDouble(out number);
    }
}

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

    public static IReadOnlyList<SceneMouse> Wheel(double x, double y, double dx, double dy)
        => [new SceneMouse("mouseWheel", x, y, dx, dy)];

    /// <summary>
    /// Press at the first screen point, move through each later point, then release.
    /// A single point still moves one pixel so the page observes a drag.
    /// </summary>
    public static IReadOnlyList<SceneMouse> Along(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count == 0) return [];
        var moves = new List<SceneMouse> { new("mousePressed", points[0].X, points[0].Y) };
        if (points.Count == 1) moves.Add(new SceneMouse("mouseMoved", points[0].X + 1, points[0].Y));
        else
        {
            for (var index = 1; index < points.Count; index++)
                moves.Add(new SceneMouse("mouseMoved", points[index].X, points[index].Y));
        }
        var last = moves[^1];
        moves.Add(new SceneMouse("mouseReleased", last.X, last.Y));
        return moves;
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
                if (path.EndsWith(".hash", StringComparison.Ordinal))
                {
                    var wantedText = split[1].Trim();
                    if (!TryText(document.RootElement, path, out var actualText) || actualText != wantedText)
                        failures.Add(path + "=" + actualText);
                    continue;
                }
                var (wanted, tolerance) = ParseSpec(split[1], defaultTolerance);
                var actual = 0d;
                if (double.IsNaN(wanted) || !TryNumber(document.RootElement, path, out actual) || Math.Abs(actual - wanted) > tolerance)
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
            return parts.Length == 3 && mesh.TryGetProperty(parts[2], out var leaf) && leaf.TryGetDouble(out value);
        }
        JsonElement current = root;
        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) return false;
        }
        return current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out value);
    }

    private static bool TryText(JsonElement root, string path, out string value)
    {
        value = "";
        var parts = path.Split('.');
        if (parts.Length == 3 && parts[0] == "mesh")
        {
            if (!TryMesh(root, parts[1], out var mesh) || !mesh.TryGetProperty(parts[2], out var leaf)) return false;
            value = leaf.ValueKind == JsonValueKind.String ? leaf.GetString() ?? "" : leaf.ToString();
            return true;
        }
        JsonElement current = root;
        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) return false;
        }
        value = current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : current.ToString();
        return true;
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
