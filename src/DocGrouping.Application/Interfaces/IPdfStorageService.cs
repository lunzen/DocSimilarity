namespace DocGrouping.Application.Interfaces;

public interface IPdfStorageService
{
	Task SaveAsync(Guid docId, string dbName, byte[] bytes);
	string GetFilePath(Guid docId, string dbName);
	bool Exists(Guid docId, string dbName);
}
