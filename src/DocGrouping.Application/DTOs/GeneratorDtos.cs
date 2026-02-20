namespace DocGrouping.Application.DTOs;

public class TemplateInfo
{
	public string TemplateId { get; set; } = "";
	public string Name { get; set; } = "";
	public string Category { get; set; } = "";
	public string Description { get; set; } = "";
}

public class GenerateRequest
{
	public string TemplateId { get; set; } = "";
	public int DocumentCount { get; set; } = 1;
	public string OcrErrorLevel { get; set; } = "none";
	public bool AddBatesStamp { get; set; }
	public bool AddReceivedStamp { get; set; }
	public bool AddFaxHeader { get; set; }
	public bool AddPageNumbers { get; set; }
	public string RedactionLevel { get; set; } = "none";
}

public class GeneratedDocument
{
	public string FileName { get; set; } = "";
	public string Content { get; set; } = "";
	public string? DocumentType { get; set; }
}
