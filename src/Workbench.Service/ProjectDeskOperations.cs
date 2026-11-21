using Workbench.Core.Projects;

namespace Workbench.Service;

internal static class ProjectDeskOperations
{
    internal static async Task<(bool Opened, ProjectWorkspaceSnapshot Snapshot)> TryOpenAsync(
        ProjectWorkspaceCatalog catalog, string id, string revision, CancellationToken cancellationToken)
    {
        try
        {
            return (true, await catalog.SetActiveAsync(id, revision, cancellationToken));
        }
        catch (ProjectWorkspaceStoreException)
        {
            return (false, await catalog.OpenAsync(cancellationToken));
        }
    }
}
