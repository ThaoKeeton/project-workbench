using System.Text;
using Workbench.Service.Abstractions;

namespace Workbench.Service;

public sealed partial class WorkbenchServiceHost
{
    private void MapTestExecutor(WebApplication app)
    {
        app.MapGet("/api/v2/plugins/{id}/suites", (string id, CancellationToken cancellationToken) =>
            HandlePluginAsync(id, "suites.list", null, cancellationToken));
        app.MapPost("/api/v2/plugins/{id}/runs", async (string id, HttpRequest request, CancellationToken cancellationToken) =>
        {
            var body = await ReadLimitedBodyAsync(request, 8192, cancellationToken);
            if (body is null) return Results.Json(new { error = "PayloadTooLarge" }, Json, statusCode: StatusCodes.Status413PayloadTooLarge);
            return await HandlePluginAsync(id, "runs.start", body, cancellationToken);
        });
        app.MapPost("/api/v2/plugins/{id}/runs/export", async (string id, HttpRequest request, CancellationToken cancellationToken) =>
        {
            var body = await ReadLimitedBodyAsync(request, 8192, cancellationToken);
            if (body is null) return Results.Json(new { error = "PayloadTooLarge" }, Json, statusCode: StatusCodes.Status413PayloadTooLarge);
            return await HandlePluginAsync(id, "runs.export", body, cancellationToken);
        });
        app.MapGet("/api/v2/plugins/{id}/runs/{runId}/events", StreamRunEventsAsync);
    }

    private async Task StreamRunEventsAsync(string id, string runId, HttpContext context, CancellationToken cancellationToken)
    {
        if (!PluginV2Catalog.IsSafePluginId(id) || !IsRunId(runId))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "NotFound" }, Json, cancellationToken);
            return;
        }
        var package = _v2Packages.SingleOrDefault(candidate => candidate.Id == id);
        if (package is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "PluginNotFound" }, Json, cancellationToken);
            return;
        }
        if (!package.Loaded || package.Module is null)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { error = "PluginUnavailable" }, Json, cancellationToken);
            return;
        }
        if (package.Module is not IWorkbenchRunEventSource source)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "NotFound" }, Json, cancellationToken);
            return;
        }
        if (!source.TryOpenRun(runId))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { error = "RunNotFound" }, Json, cancellationToken);
            return;
        }
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await foreach (var json in source.ReadEventsAsync(runId, cancellationToken))
        {
            var line = "data: " + json + "\n\n";
            await context.Response.WriteAsync(line, cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static async Task<string?> ReadLimitedBodyAsync(HttpRequest request, int limit, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        return body.Length > limit ? null : body;
    }

    private static bool IsRunId(string value)
        => value.Length == 32 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
