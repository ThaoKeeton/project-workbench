using System.Text;

namespace Workbench.TestExecutor;

public static class TextRules
{
    public static bool IsSafeId(string? value)
        => value is { Length: > 0 and <= 64 } && value[0] is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
           && value.All(character => character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-');

    public static bool IsSafeText(string? value, int limit)
        => value is { Length: > 0 } && value.Length <= limit && value.All(character => !char.IsControl(character));

    public static bool IsOptionalText(string? value, int limit)
        => value is null || (value.Length <= limit && value.All(character => !char.IsControl(character)));

    public static string Clip(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var builder = new StringBuilder(Math.Min(value.Length, limit));
        foreach (var character in value)
        {
            if (builder.Length >= limit) break;
            builder.Append(char.IsControl(character) ? ' ' : character);
        }
        return builder.ToString();
    }

    public static string Redact(string? value, string? secret, int limit)
    {
        var text = value ?? "";
        if (!string.IsNullOrEmpty(secret) && secret.Length >= 4)
            text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return Clip(text, limit);
    }
}

public static class ContainedPath
{
    public static string Combine(string root, params string[] parts)
    {
        var fullRoot = Path.GetFullPath(root);
        var combined = fullRoot;
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part) || part.IndexOfAny(['/', '\\', ':']) >= 0 || part is "." or "..")
                throw new ExecutorDataException(ExecutorError.InvalidSuite);
            combined = Path.Combine(combined, part);
        }
        var full = Path.GetFullPath(combined);
        var relative = Path.GetRelativePath(fullRoot, full);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new ExecutorDataException(ExecutorError.InvalidSuite);
        return full;
    }

    public static bool IsInside(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(fullRoot, full);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}

public sealed class ExecutorDataException : Exception
{
    public ExecutorDataException(string code) : base(code) => Code = code;

    public string Code { get; }
}
