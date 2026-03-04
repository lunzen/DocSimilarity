using DocGrouping.Web.Services;

namespace DocGrouping.Web.Middleware;

public class DatabaseSelectionMiddleware
{
    private readonly RequestDelegate _next;

    public DatabaseSelectionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, DatabaseSelectorState dbState)
    {
        // Check X-Database header first, then ?db= query param
        var dbName = context.Request.Headers["X-Database"].FirstOrDefault()
            ?? context.Request.Query["db"].FirstOrDefault();

        context.Items["ActiveDatabase"] = dbName ?? dbState.ActiveDatabaseName;

        await _next(context);
    }
}
