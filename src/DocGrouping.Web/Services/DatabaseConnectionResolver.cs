using System.Text.RegularExpressions;
using Npgsql;

namespace DocGrouping.Web.Services;

public partial class DatabaseConnectionResolver
{
    private readonly string _templateConnectionString;

    public DatabaseConnectionResolver(IConfiguration configuration)
    {
        _templateConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not configured");
    }

    public string GetConnectionString(string databaseName)
    {
        if (!ValidDatabaseNameRegex().IsMatch(databaseName))
            throw new ArgumentException($"Invalid database name: '{databaseName}'");

        var builder = new NpgsqlConnectionStringBuilder(_templateConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex ValidDatabaseNameRegex();
}
