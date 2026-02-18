using DocGrouping.Application.DTOs;

namespace DocGrouping.Application.Interfaces;

public interface IDocumentGeneratorService
{
	List<TemplateInfo> GetAvailableTemplates();
	string PreviewTemplate(string templateId);
	List<GeneratedDocument> GenerateDocuments(GenerateRequest request);
	List<GeneratedDocument> GenerateBulkDocuments(int totalCount);
}
