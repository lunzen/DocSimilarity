using DocGrouping.Application.DTOs;
using DocGrouping.Infrastructure.Services;
using DocGrouping.Infrastructure.TextProcessing;
using FluentAssertions;

namespace DocGrouping.Infrastructure.Tests;

public class DocumentGeneratorServiceTests
{
	private readonly DocumentGeneratorService _service = new(
		new OcrErrorSimulator(),
		new StampGenerator(),
		new RedactionSimulator());

	[Fact]
	public void GetAvailableTemplates_Returns10Templates()
	{
		var templates = _service.GetAvailableTemplates();
		templates.Should().HaveCount(10);
	}

	[Fact]
	public void GetAvailableTemplates_AllHaveRequiredFields()
	{
		var templates = _service.GetAvailableTemplates();
		foreach (var t in templates)
		{
			t.TemplateId.Should().NotBeNullOrEmpty();
			t.Name.Should().NotBeNullOrEmpty();
			t.Category.Should().NotBeNullOrEmpty();
			t.Description.Should().NotBeNullOrEmpty();
		}
	}

	[Theory]
	[InlineData("lease_agreement")]
	[InlineData("production_report")]
	[InlineData("regulatory_filing")]
	public void PreviewTemplate_ReturnsRenderedContent(string templateId)
	{
		var preview = _service.PreviewTemplate(templateId);
		preview.Should().NotBeNullOrEmpty();
		preview.Should().NotContain("{{"); // All placeholders resolved
	}

	[Fact]
	public void PreviewTemplate_InvalidId_ReturnsNotFound()
	{
		var preview = _service.PreviewTemplate("nonexistent");
		preview.Should().Contain("not found");
	}

	[Fact]
	public void GenerateDocuments_CreatesRequestedCount()
	{
		var request = new GenerateRequest
		{
			TemplateId = "lease_agreement",
			DocumentCount = 5,
		};

		var docs = _service.GenerateDocuments(request);
		docs.Should().HaveCount(5);
	}

	[Fact]
	public void GenerateDocuments_WithOcrErrors_ModifiesText()
	{
		var request = new GenerateRequest
		{
			TemplateId = "lease_agreement",
			DocumentCount = 1,
			OcrErrorLevel = "heavy",
		};

		var baseRequest = new GenerateRequest
		{
			TemplateId = "lease_agreement",
			DocumentCount = 1,
			OcrErrorLevel = "none",
		};

		var clean = _service.GenerateDocuments(baseRequest)[0].Content;
		// Run several times - at heavy level, the text should differ at least sometimes
		var anyDifferent = false;
		for (int i = 0; i < 10; i++)
		{
			var withErrors = _service.GenerateDocuments(request)[0].Content;
			if (withErrors != clean)
			{
				anyDifferent = true;
				break;
			}
		}
		anyDifferent.Should().BeTrue("heavy OCR should produce some errors");
	}

	[Fact]
	public void GenerateDocuments_WithBatesStamp_AddsBatesStamp()
	{
		var request = new GenerateRequest
		{
			TemplateId = "lease_agreement",
			DocumentCount = 1,
			AddBatesStamp = true,
		};

		var doc = _service.GenerateDocuments(request)[0];
		doc.Content.Should().Contain("BATES:");
	}

	[Fact]
	public void GenerateDocuments_WithRedactions_AddsRedactions()
	{
		var request = new GenerateRequest
		{
			TemplateId = "production_report",
			DocumentCount = 1,
			RedactionLevel = "heavy",
		};

		// Run several times - redactions are probabilistic
		var anyRedacted = false;
		for (int i = 0; i < 10; i++)
		{
			var doc = _service.GenerateDocuments(request)[0];
			if (doc.Content.Contains("[REDACTED"))
			{
				anyRedacted = true;
				break;
			}
		}
		anyRedacted.Should().BeTrue("heavy redaction should add markers");
	}

	[Fact]
	public void GenerateDocuments_InvalidTemplate_ReturnsEmpty()
	{
		var request = new GenerateRequest
		{
			TemplateId = "nonexistent",
			DocumentCount = 3,
		};

		var docs = _service.GenerateDocuments(request);
		docs.Should().BeEmpty();
	}

	[Fact]
	public void GenerateDocuments_MultipleVariations_ProduceDifferentContent()
	{
		var request = new GenerateRequest
		{
			TemplateId = "lease_agreement",
			DocumentCount = 3,
		};

		var docs = _service.GenerateDocuments(request);
		// First doc uses defaults; subsequent docs have varied values
		docs[0].Content.Should().NotBe(docs[1].Content);
	}

	[Fact]
	public void GenerateDocuments_FileNamesAreCorrect()
	{
		var request = new GenerateRequest
		{
			TemplateId = "lease_agreement",
			DocumentCount = 3,
		};

		var docs = _service.GenerateDocuments(request);
		docs[0].FileName.Should().Contain("Lease_Agreement");
		docs[0].FileName.Should().EndWith("_v1.txt");
		docs[1].FileName.Should().EndWith("_v2.txt");
	}
}
