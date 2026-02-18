using DocGrouping.Application.Interfaces;
using DocGrouping.Domain.Enums;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.TextProcessing;
using Microsoft.AspNetCore.Mvc;

namespace DocGrouping.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GroupsController(
	IDocumentGroupRepository groupRepository,
	IGroupingOrchestrator groupingOrchestrator,
	DocumentFingerprinter fingerprinter) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> GetAll(
		[FromQuery] int page = 1,
		[FromQuery] int pageSize = 20,
		[FromQuery] string? confidence = null,
		CancellationToken ct = default)
	{
		MatchConfidence? confidenceFilter = confidence switch
		{
			"very_high" => MatchConfidence.VeryHigh,
			"high" => MatchConfidence.High,
			"medium" => MatchConfidence.Medium,
			"none" => MatchConfidence.None,
			_ => null
		};

		var groups = await groupRepository.GetPagedAsync(page, pageSize, confidenceFilter, ct);
		var totalCount = await groupRepository.GetCountAsync(confidenceFilter, ct);

		return Ok(new
		{
			groups = groups.Select(g => new
			{
				g.Id,
				group_number = g.GroupNumber,
				confidence = g.Confidence.ToString().ToLowerInvariant(),
				g.MatchReason,
				document_count = g.DocumentCount,
				canonical = g.CanonicalDocument?.FileName,
				documents = g.Memberships.Select(m => new
				{
					m.Document.Id,
					m.Document.FileName,
					m.Document.WordCount,
					m.IsCanonical,
					similarity_score = m.SimilarityScore
				})
			}),
			total_count = totalCount,
			page,
			page_size = pageSize
		});
	}

	[HttpGet("{groupNumber:int}")]
	public async Task<IActionResult> GetByGroupNumber(int groupNumber, CancellationToken ct)
	{
		var group = await groupRepository.GetByGroupNumberAsync(groupNumber, ct);
		if (group == null) return NotFound();

		object? matchExplanation = null;
		if (group.Memberships.Count >= 2)
		{
			var docs = group.Memberships.OrderByDescending(m => m.IsCanonical).ToList();
			var metrics = fingerprinter.CalculateSimilarityMetrics(
				docs[0].Document.NormalizedText,
				docs[1].Document.NormalizedText);

			matchExplanation = new
			{
				jaccard_similarity = Math.Round(metrics.JaccardSimilarity * 100, 1),
				overlap_coefficient = Math.Round(metrics.OverlapCoefficient * 100, 1),
				fuzzy_signature_match = Math.Round(metrics.FuzzySignatureJaccard * 100, 1),
				common_tokens_count = metrics.CommonTokens,
				total_tokens_doc1 = metrics.TokenCount1,
				total_tokens_doc2 = metrics.TokenCount2
			};
		}

		return Ok(new
		{
			group.Id,
			group_number = group.GroupNumber,
			confidence = group.Confidence.ToString().ToLowerInvariant(),
			group.MatchReason,
			document_count = group.DocumentCount,
			canonical = group.CanonicalDocument?.FileName,
			documents = group.Memberships.Select(m => new
			{
				m.Document.Id,
				m.Document.FileName,
				m.Document.WordCount,
				m.IsCanonical,
				similarity_score = m.SimilarityScore,
				preview_text = m.Document.OriginalText.Length > 200
					? m.Document.OriginalText[..200] + "..."
					: m.Document.OriginalText
			}),
			match_explanation = matchExplanation
		});
	}

	[HttpGet("statistics")]
	public async Task<IActionResult> GetStatistics(CancellationToken ct)
	{
		var stats = await groupingOrchestrator.GetStatisticsAsync(ct);
		return Ok(stats);
	}
}
