using DocGrouping.Application.Interfaces;
using DocGrouping.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DocGrouping.Infrastructure.Services;

public class PdfStorageService(IOptions<PdfStorageOptions> options) : IPdfStorageService
{
	private readonly string _rootPath = options.Value.RootPath;

	public async Task SaveAsync(Guid docId, string dbName, byte[] bytes)
	{
		var dir = Path.Combine(_rootPath, dbName);
		Directory.CreateDirectory(dir);
		var filePath = Path.Combine(dir, $"{docId}.pdf");
		await File.WriteAllBytesAsync(filePath, bytes);
	}

	public string GetFilePath(Guid docId, string dbName)
	{
		return Path.Combine(_rootPath, dbName, $"{docId}.pdf");
	}

	public bool Exists(Guid docId, string dbName)
	{
		return File.Exists(GetFilePath(docId, dbName));
	}
}
