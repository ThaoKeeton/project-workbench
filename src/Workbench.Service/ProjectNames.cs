using Workbench.Core.Projects;

namespace Workbench.Service;

/// <summary>
/// Desk-only name check. The catalog remains the writer and still accepts duplicate
/// display names; this preview refuses them before any catalog write.
/// </summary>
internal static class ProjectNames
{
    internal static string? Rejection(string? name, IReadOnlyList<ProjectWorkspaceInfo> projects, string? exceptId)
    {
        if (!TryNormalize(name, out var trimmed)) return "InvalidName";
        foreach (var project in projects)
        {
            if (exceptId is not null && project.Id == exceptId) continue;
            if (string.Equals(project.Name, trimmed, StringComparison.OrdinalIgnoreCase)) return "DuplicateName";
        }
        return null;
    }

    internal static bool TryNormalize(string? name, out string trimmed)
    {
        trimmed = "";
        if (name is null || !ValidUnicode(name) || name.Any(char.IsControl)) return false;
        trimmed = name.Trim();
        return trimmed.Length is > 0 and <= 80;
    }

    private static bool ValidUnicode(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i])) continue;
            if (!char.IsHighSurrogate(value[i]) || ++i == value.Length || !char.IsLowSurrogate(value[i])) return false;
        }
        return true;
    }
}
