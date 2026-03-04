namespace DocGrouping.Web.Services;

public class DatabaseSelectorState
{
    private string _activeDatabaseName;
    private readonly List<string> _availableDatabases;

    public DatabaseSelectorState(IConfiguration configuration)
    {
        _availableDatabases = configuration.GetSection("Databases").Get<List<string>>()
            ?? ["docgrouping"];
        _activeDatabaseName = _availableDatabases[0];
    }

    public string ActiveDatabaseName
    {
        get => _activeDatabaseName;
        set
        {
            if (_activeDatabaseName == value) return;
            _activeDatabaseName = value;
            OnChanged?.Invoke();
        }
    }

    public IReadOnlyList<string> AvailableDatabases => _availableDatabases;

    public event Action? OnChanged;
}
