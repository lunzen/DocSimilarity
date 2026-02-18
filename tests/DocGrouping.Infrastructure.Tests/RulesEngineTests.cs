using DocGrouping.Infrastructure.Rules;
using FluentAssertions;

namespace DocGrouping.Infrastructure.Tests;

public class RulesEngineTests
{
	private readonly RulesEngine _engine = new();

	[Fact]
	public void DefaultRules_AreLoaded()
	{
		var rules = _engine.GetAllRules();
		rules.Should().NotBeEmpty();
		rules.Should().Contain(r => r.RuleId == "default_version_priority");
		rules.Should().Contain(r => r.RuleId == "default_redaction_separator");
	}

	[Fact]
	public void DefaultRules_SortedByPriority()
	{
		var rules = _engine.GetAllRules();
		for (var i = 1; i < rules.Count; i++)
		{
			rules[i].Priority.Should().BeGreaterThanOrEqualTo(rules[i - 1].Priority);
		}
	}

	[Fact]
	public void EvaluateDocument_DetectsVersionKeyword()
	{
		var result = _engine.EvaluateDocument("This is a FINAL version of the document.", null);
		result.RuleFlags.Should().ContainKey("version");
		result.RuleFlags["version"].Should().Be("FINAL");
	}

	[Fact]
	public void EvaluateDocument_DetectsDraft()
	{
		var result = _engine.EvaluateDocument("This is a DRAFT version.", null);
		result.RuleFlags.Should().ContainKey("version");
		result.RuleFlags["version"].Should().Be("DRAFT");
	}

	[Fact]
	public void EvaluateDocument_DetectsRedactions()
	{
		var result = _engine.EvaluateDocument("Name: [REDACTED] signed the contract.", null);
		result.RuleFlags.Should().ContainKey("has_redactions");
		result.RuleFlags["has_redactions"].Should().Be(true);
	}

	[Fact]
	public void EvaluateDocument_NoRedaction_NoFlag()
	{
		var result = _engine.EvaluateDocument("Name: John Smith signed the contract.", null);
		result.RuleFlags.Should().NotContainKey("has_redactions");
	}

	[Fact]
	public void ShouldGroup_VeryHigh_DefaultTrue()
	{
		var decision = _engine.ShouldGroup("doc1 text", null, "doc2 text", null, "very_high");
		decision.ShouldGroup.Should().BeTrue();
	}

	[Fact]
	public void ShouldGroup_None_DefaultFalse()
	{
		var decision = _engine.ShouldGroup("doc1 text", null, "doc2 text", null, "none");
		decision.ShouldGroup.Should().BeFalse();
	}

	[Fact]
	public void ShouldGroup_RedactionMismatch_PreventsGrouping()
	{
		var decision = _engine.ShouldGroup(
			"This document has [REDACTED] content.", null,
			"This document has full content.", null,
			"very_high");
		decision.ShouldGroup.Should().BeFalse();
		decision.RuleModified.Should().BeTrue();
	}

	[Fact]
	public void ShouldGroup_BothRedacted_AllowsGrouping()
	{
		var decision = _engine.ShouldGroup(
			"This document has [REDACTED] content.", null,
			"This other document also [REDACTED] portions.", null,
			"very_high");
		decision.ShouldGroup.Should().BeTrue();
	}

	[Fact]
	public void ShouldGroup_DifferentDocumentTypes_PreventsGrouping()
	{
		var meta1 = new Dictionary<string, object?> { ["document_type"] = "Agreement" };
		var meta2 = new Dictionary<string, object?> { ["document_type"] = "Report" };

		var decision = _engine.ShouldGroup("doc text", meta1, "doc text", meta2, "very_high");
		decision.ShouldGroup.Should().BeFalse();
	}

	[Fact]
	public void ShouldGroup_SameDocumentTypes_AllowsGrouping()
	{
		var meta1 = new Dictionary<string, object?> { ["document_type"] = "Agreement" };
		var meta2 = new Dictionary<string, object?> { ["document_type"] = "Agreement" };

		var decision = _engine.ShouldGroup("doc text", meta1, "doc text", meta2, "very_high");
		decision.ShouldGroup.Should().BeTrue();
	}

	[Fact]
	public void SelectCanonicalIndex_PrefersFinal()
	{
		var docs = new List<(string OriginalText, Dictionary<string, object?>? Metadata)>
		{
			("This is a DRAFT document.", null),
			("This is the FINAL document.", null),
		};

		var index = _engine.SelectCanonicalIndex(docs);
		index.Should().Be(1); // FINAL should be preferred
	}

	[Fact]
	public void SelectCanonicalIndex_PenalizesRedacted()
	{
		var docs = new List<(string OriginalText, Dictionary<string, object?>? Metadata)>
		{
			("Full version [REDACTED] with redactions.", null),
			("Full version without any redactions.", null),
		};

		var index = _engine.SelectCanonicalIndex(docs);
		index.Should().Be(1); // Non-redacted should be preferred
	}

	[Fact]
	public void AddRule_IncreasesCount()
	{
		var initial = _engine.GetAllRules().Count;
		_engine.AddRule(new RuleDefinition
		{
			RuleId = "test_rule",
			Name = "Test Rule",
			Priority = 50
		});
		_engine.GetAllRules().Count.Should().Be(initial + 1);
	}

	[Fact]
	public void RemoveRule_DecreasesCount()
	{
		var initial = _engine.GetAllRules().Count;
		_engine.RemoveRule("default_version_priority");
		_engine.GetAllRules().Count.Should().Be(initial - 1);
	}

	[Fact]
	public void GetRule_ReturnsCorrectRule()
	{
		var rule = _engine.GetRule("default_version_priority");
		rule.Should().NotBeNull();
		rule!.Name.Should().Contain("Version Priority");
	}

	[Fact]
	public void GetRule_NonExistent_ReturnsNull()
	{
		var rule = _engine.GetRule("nonexistent");
		rule.Should().BeNull();
	}
}
