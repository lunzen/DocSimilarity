using System.Collections.Concurrent;
using DocGrouping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocGrouping.Web.Services;

public class DatabaseInitializer
{
    private readonly DatabaseConnectionResolver _resolver;
    private readonly ConcurrentDictionary<string, bool> _initialized = new();
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        DatabaseConnectionResolver resolver,
        ILogger<DatabaseInitializer> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public async Task EnsureCreatedAsync(string databaseName)
    {
        if (_initialized.ContainsKey(databaseName))
            return;

        var connectionString = _resolver.GetConnectionString(databaseName);
        var optionsBuilder = new DbContextOptionsBuilder<DocGroupingDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        await using var dbContext = new DocGroupingDbContext(optionsBuilder.Options);
        var created = await dbContext.Database.EnsureCreatedAsync();

        _initialized[databaseName] = true;
        _logger.LogInformation("Database '{Database}' initialized (created: {Created})", databaseName, created);
    }
}
