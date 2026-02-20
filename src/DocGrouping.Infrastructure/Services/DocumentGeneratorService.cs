using System.Text.RegularExpressions;
using DocGrouping.Application.DTOs;
using DocGrouping.Application.Interfaces;
using DocGrouping.Infrastructure.TextProcessing;

namespace DocGrouping.Infrastructure.Services;

public class DocumentGeneratorService(
	OcrErrorSimulator ocrSimulator,
	StampGenerator stampGenerator,
	RedactionSimulator redactionSimulator) : IDocumentGeneratorService
{
	private static readonly Random Rng = new();

	public List<TemplateInfo> GetAvailableTemplates()
	{
		return DocumentTemplates.All.Select(t => new TemplateInfo
		{
			TemplateId = t.TemplateId,
			Name = t.Name,
			Category = t.Category,
			Description = t.Description,
		}).ToList();
	}

	public string PreviewTemplate(string templateId)
	{
		var template = DocumentTemplates.GetById(templateId);
		if (template is null) return "Template not found.";
		return RenderTemplate(template, template.DefaultVariables);
	}

	public List<GeneratedDocument> GenerateDocuments(GenerateRequest request)
	{
		var template = DocumentTemplates.GetById(request.TemplateId);
		if (template is null) return [];

		var docs = new List<GeneratedDocument>();

		for (int i = 0; i < request.DocumentCount; i++)
		{
			var variables = VaryVariables(template.DefaultVariables, i);
			var content = RenderTemplate(template, variables);

			// Apply stamps
			if (request.AddBatesStamp)
			{
				var stamp = stampGenerator.GenerateBatesStamp();
				content = stampGenerator.AddStampToDocument(content, stamp, "top");
			}

			if (request.AddReceivedStamp)
			{
				var stamp = stampGenerator.GenerateReceivedStamp();
				content = stampGenerator.AddStampToDocument(content, stamp, "top");
			}

			if (request.AddFaxHeader)
			{
				var header = stampGenerator.GenerateFaxHeader();
				content = stampGenerator.AddStampToDocument(content, header, "top");
			}

			if (request.AddPageNumbers)
			{
				content = stampGenerator.InsertPageNumbersThroughout(content);
			}

			// Apply redactions
			if (request.RedactionLevel != "none")
			{
				content = redactionSimulator.ApplyRedactions(content, request.RedactionLevel);
			}

			// Apply OCR errors last
			if (request.OcrErrorLevel != "none")
			{
				content = ocrSimulator.ApplyErrors(content, request.OcrErrorLevel);
			}

			var suffix = request.DocumentCount > 1 ? $"_v{i + 1}" : "";
			var fileName = $"{template.Name.Replace(" ", "_")}{suffix}.txt";

			docs.Add(new GeneratedDocument
			{
				FileName = fileName,
				Content = content,
				DocumentType = template.TemplateId,
			});
		}

		return docs;
	}

	public List<GeneratedDocument> GenerateBulkDocuments(int totalCount)
	{
		var templates = DocumentTemplates.All;
		var allDocs = new List<GeneratedDocument>();
		var ocrLevels = new[] { "none", "light", "moderate", "heavy" };
		var redactionLevels = new[] { "none", "light", "moderate", "heavy" };
		var docNumber = 0;

		// Distribute evenly across templates
		var perTemplate = totalCount / templates.Count;
		var remainder = totalCount % templates.Count;

		for (int t = 0; t < templates.Count; t++)
		{
			var template = templates[t];
			var count = perTemplate + (t < remainder ? 1 : 0);

			for (int i = 0; i < count; i++)
			{
				docNumber++;
				var variables = VaryVariables(template.DefaultVariables, i);
				var content = RenderTemplate(template, variables);

				// Randomly apply stamps (~30% chance each)
				if (Rng.NextDouble() < 0.3)
				{
					content = stampGenerator.AddStampToDocument(
						content, stampGenerator.GenerateBatesStamp(), "top");
				}
				if (Rng.NextDouble() < 0.2)
				{
					content = stampGenerator.AddStampToDocument(
						content, stampGenerator.GenerateReceivedStamp(), "top");
				}
				if (Rng.NextDouble() < 0.15)
				{
					content = stampGenerator.AddStampToDocument(
						content, stampGenerator.GenerateFaxHeader(), "top");
				}
				if (Rng.NextDouble() < 0.25)
				{
					content = stampGenerator.InsertPageNumbersThroughout(content);
				}

				// Random redaction level (~40% get some redaction)
				var redaction = Rng.NextDouble() < 0.4
					? redactionLevels[Rng.Next(1, redactionLevels.Length)]
					: "none";
				if (redaction != "none")
				{
					content = redactionSimulator.ApplyRedactions(content, redaction);
				}

				// Random OCR error level (~50% get some OCR errors)
				var ocr = Rng.NextDouble() < 0.5
					? ocrLevels[Rng.Next(1, ocrLevels.Length)]
					: "none";
				if (ocr != "none")
				{
					content = ocrSimulator.ApplyErrors(content, ocr);
				}

				var fileName = $"{template.Name.Replace(" ", "_")}_{docNumber:D4}.txt";
				allDocs.Add(new GeneratedDocument
				{
					FileName = fileName,
					Content = content,
					DocumentType = template.TemplateId,
				});
			}
		}

		return allDocs;
	}

	private static string RenderTemplate(TemplateDefinition template, Dictionary<string, string> variables)
	{
		var content = template.Content;
		foreach (var (key, value) in variables)
		{
			content = content.Replace($"{{{{{key}}}}}", value);
		}
		// Remove any remaining unresolved placeholders
		content = Regex.Replace(content, @"\{\{\w+\}\}", "[DATA]");
		return content.Trim();
	}

	private static Dictionary<string, string> VaryVariables(Dictionary<string, string> defaults, int index)
	{
		var varied = new Dictionary<string, string>(defaults);

		if (index == 0) return varied;

		// Vary dates slightly
		foreach (var key in varied.Keys.Where(k => k.Contains("date", StringComparison.OrdinalIgnoreCase)).ToList())
		{
			if (DateTime.TryParse(varied[key], out var dt))
			{
				varied[key] = dt.AddDays(Rng.Next(-30, 31)).ToString("MMMM d, yyyy");
			}
		}

		// Vary numeric values slightly
		foreach (var key in varied.Keys.ToList())
		{
			if (decimal.TryParse(varied[key].Replace(",", ""), out var num) && num > 0)
			{
				var factor = 1.0m + (decimal)(Rng.NextDouble() * 0.2 - 0.1); // +/- 10%
				varied[key] = (num * factor).ToString("N0");
			}
		}

		return varied;
	}
}
