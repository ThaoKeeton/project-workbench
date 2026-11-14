using Workbench.Sdk;
using Workbench.Core.Testing;

namespace Workbench.Core.Projects;

/// <summary>One runtime's fixed project root and identity. Rename only after the catalog rename has committed.</summary>
public sealed class ProjectRuntimeScope
{
    private ProjectIdentity _identity;
    public ProjectRuntimeScope(string id, string name, string userRoot)
    {
        ProjectWorkspaceStorage.ValidateId(id);
        Id = id;
        UserRoot = Path.TrimEndingDirectorySeparator(ProjectWorkspaceStorage.NormalizeRoot(userRoot));
        _identity = new(id, ProjectWorkspaceStorage.Name(name));
        Documents = new(UserRoot, id);
        Testing = new(UserRoot, id);
    }
    public string Id { get; }
    public string UserRoot { get; }
    public ProjectIdentity Identity => Volatile.Read(ref _identity);
    public ProjectContextDocumentStore Documents { get; }
    public ProjectTestStore Testing { get; }
    public void Rename(string name) => Volatile.Write(ref _identity, new(Id, ProjectWorkspaceStorage.Name(name)));
}
