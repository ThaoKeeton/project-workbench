namespace Workbench.Core.Projects;

public sealed record ProjectWorkspaceInfo(string Id, string Name, bool IsArchived);
public sealed record ProjectWorkspaceSnapshot(string Revision, string ActiveProjectId, IReadOnlyList<ProjectWorkspaceInfo> Projects);
public sealed record ProjectWorkspaceCreation(ProjectWorkspaceInfo Project, ProjectWorkspaceSnapshot Snapshot);
public sealed record ProjectContextDocument(string Revision, string Text, bool IsMissing);

public sealed class ProjectWorkspaceStoreException(string errorCode) : Exception("Project workspace storage failed.")
{
    public string ErrorCode { get; } = errorCode;
}
