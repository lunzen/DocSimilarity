using System.Text;
using System.Text.Json;
using DocGrouping.Application.DTOs;
using Microsoft.JSInterop;

namespace DocGrouping.Web.Services;

public static class ExportService
{
	public static string ExportGroupsAsJson(List<GroupDto> groups)
	{
		return JsonSerializer.Serialize(groups, new JsonSerializerOptions { WriteIndented = true });
	}

	public static string ExportGroupsAsCsv(List<GroupDto> groups)
	{
		var sb = new StringBuilder();
		sb.AppendLine("GroupNumber,Confidence,MatchReason,DocumentCount,CanonicalFile,FileName,WordCount,IsCanonical");

		foreach (var group in groups)
		{
			foreach (var doc in group.Documents)
			{
				sb.AppendLine(string.Join(",",
					group.GroupNumber,
					CsvEscape(group.Confidence),
					CsvEscape(group.MatchReason),
					group.DocumentCount,
					CsvEscape(group.CanonicalFileName ?? ""),
					CsvEscape(doc.FileName),
					doc.WordCount,
					doc.IsCanonical));
			}
		}

		return sb.ToString();
	}

	public static async Task DownloadFileAsync(IJSRuntime js, string fileName, string content, string mimeType)
	{
		var bytes = Encoding.UTF8.GetBytes(content);
		var base64 = Convert.ToBase64String(bytes);
		await js.InvokeVoidAsync("eval", $@"
			const a = document.createElement('a');
			a.href = 'data:{mimeType};base64,{base64}';
			a.download = '{fileName}';
			document.body.appendChild(a);
			a.click();
			document.body.removeChild(a);
		");
	}

	private static string CsvEscape(string value)
	{
		if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
			return $"\"{value.Replace("\"", "\"\"")}\"";
		return value;
	}
}
