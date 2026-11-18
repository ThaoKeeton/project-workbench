using System.Text.Json;
using Workbench.Core.Projects;

namespace Workbench.Service;

public sealed partial class WorkbenchServiceHost
{
    private void MapProjectDesk(WebApplication app)
    {
        app.MapPost("/api/v2/projects", CreateProjectAsync);
        app.MapPost("/api/v2/projects/name", RenameProjectAsync);
        app.MapGet("/api/v2/projects/active/context", ReadProjectContextAsync);
        app.MapPut("/api/v2/projects/active/context", WriteProjectContextAsync);
    }

    private async Task<IResult> CreateProjectAsync(ProjectNameBody body, CancellationToken cancellationToken)
    {
        var catalog = new ProjectWorkspaceCatalog(_userRoot);
        ProjectWorkspaceSnapshot current;
        try { current = await catalog.OpenAsync(cancellationToken); }
        catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }

        var rejection = ProjectNames.Rejection(body.Name, current.Projects, null);
        if (rejection is not null) return NameError(rejection);
        if (string.IsNullOrWhiteSpace(body.ExpectedRevision)) return NameError("RevisionConflict");

        try
        {
            var created = await catalog.CreateAsync(body.Name!, body.ExpectedRevision!, cancellationToken);
            var (opened, snapshot) = await ProjectDeskOperations.TryOpenAsync(catalog, created.Project.Id, created.Snapshot.Revision, cancellationToken);
            PublishProject(snapshot, current.Revision);
            return Results.Json(CreatedBody(snapshot, opened), Json);
        }
        catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }
    }

    private async Task<IResult> RenameProjectAsync(ProjectNameBody body, CancellationToken cancellationToken)
    {
        var catalog = new ProjectWorkspaceCatalog(_userRoot);
        ProjectWorkspaceSnapshot current;
        try { current = await catalog.OpenAsync(cancellationToken); }
        catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }

        var active = current.Projects.FirstOrDefault(project => project.Id == current.ActiveProjectId);
        if (active is null) return NameError("ProjectNotFound");
        var rejection = ProjectNames.Rejection(body.Name, current.Projects, active.Id);
        if (rejection is not null) return NameError(rejection);
        if (string.IsNullOrWhiteSpace(body.ExpectedRevision)) return NameError("RevisionConflict");

        try
        {
            var snapshot = await catalog.RenameAsync(active.Id, body.Name!, body.ExpectedRevision!, cancellationToken);
            PublishProject(snapshot, current.Revision);
            return Results.Json(ProjectBody(snapshot), Json);
        }
        catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }
    }

    private async Task<IResult> ReadProjectContextAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (store, _) = await ActiveContextAsync(cancellationToken);
            return Results.Json(ContextBody(await store.ReadAsync(cancellationToken)), Json);
        }
        catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }
    }

    private async Task<IResult> WriteProjectContextAsync(ProjectContextBody body, CancellationToken cancellationToken)
    {
        if (body.Text is null) return NameError("InvalidContext");
        if (string.IsNullOrWhiteSpace(body.ExpectedRevision)) return NameError("RevisionConflict");
        try
        {
            var (store, _) = await ActiveContextAsync(cancellationToken);
            var document = await store.WriteAsync(body.Text, body.ExpectedRevision, cancellationToken);
            return Results.Json(ContextBody(document), Json);
        }
        catch (ProjectWorkspaceStoreException error) { return ProjectError(error); }
    }

    private async Task<(ProjectContextDocumentStore Store, ProjectWorkspaceSnapshot Snapshot)> ActiveContextAsync(CancellationToken cancellationToken)
    {
        var catalog = new ProjectWorkspaceCatalog(_userRoot);
        var snapshot = await catalog.OpenAsync(cancellationToken);
        var store = new ProjectContextDocumentStore(catalog.GetProjectRoot(snapshot.ActiveProjectId), snapshot.ActiveProjectId);
        return (store, snapshot);
    }

    private void PublishProject(ProjectWorkspaceSnapshot snapshot, string previousRevision)
    {
        if (string.Equals(snapshot.Revision, previousRevision, StringComparison.Ordinal)) return;
        _events.Publish(JsonSerializer.Serialize(new { type = "project-changed", activeProjectId = snapshot.ActiveProjectId }, Json));
    }

    private static object CreatedBody(ProjectWorkspaceSnapshot snapshot, bool opened) => new
    {
        schemaVersion = 1,
        revision = snapshot.Revision,
        activeProjectId = snapshot.ActiveProjectId,
        projects = snapshot.Projects.Select(project => new { id = project.Id, name = project.Name, isArchived = project.IsArchived }).ToArray(),
        opened
    };

    private static object ContextBody(ProjectContextDocument document) => new
    {
        schemaVersion = 1,
        revision = document.Revision,
        text = document.Text,
        missing = document.IsMissing
    };

    private static IResult NameError(string code) => Results.Json(new { error = code }, Json, statusCode: code switch
    {
        "DuplicateName" or "RevisionConflict" => StatusCodes.Status409Conflict,
        "ProjectNotFound" => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest
    });

    private sealed record ProjectNameBody(string? Name, string? ExpectedRevision);
    private sealed record ProjectContextBody(string? Text, string? ExpectedRevision);
}
