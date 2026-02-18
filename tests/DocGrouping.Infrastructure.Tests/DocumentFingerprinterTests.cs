using DocGrouping.Infrastructure.TextProcessing;
using FluentAssertions;

namespace DocGrouping.Infrastructure.Tests;

public class DocumentFingerprinterTests
{
	private readonly DocumentFingerprinter _fingerprinter = new();
	private readonly TextNormalizer _normalizer = new();

	[Fact]
	public void GenerateTextHash_SameInput_SameHash()
	{
		var hash1 = _fingerprinter.GenerateTextHash("hello world");
		var hash2 = _fingerprinter.GenerateTextHash("hello world");
		hash1.Should().Be(hash2);
	}

	[Fact]
	public void GenerateTextHash_DifferentInput_DifferentHash()
	{
		var hash1 = _fingerprinter.GenerateTextHash("hello world");
		var hash2 = _fingerprinter.GenerateTextHash("hello earth");
		hash1.Should().NotBe(hash2);
	}

	[Fact]
	public void GenerateTextHash_Returns64CharHex()
	{
		var hash = _fingerprinter.GenerateTextHash("test");
		hash.Should().HaveLength(64);
		hash.Should().MatchRegex("^[0-9a-f]{64}$");
	}

	[Fact]
	public void GenerateFuzzyHash_SameInput_SameHash()
	{
		var text = "the quick brown fox jumps over the lazy dog and continues running through the forest meadow";
		var hash1 = _fingerprinter.GenerateFuzzyHash(text);
		var hash2 = _fingerprinter.GenerateFuzzyHash(text);
		hash1.Should().Be(hash2);
	}

	[Fact]
	public void GenerateFuzzyHash_SimilarInput_SameHash()
	{
		// Two documents that differ only in stopwords/short words should produce same fuzzy hash
		var text1 = "agreement between parties regarding property transfer settlement execution obligations";
		var text2 = "agreement between the parties regarding property transfer and settlement execution obligations";
		var hash1 = _fingerprinter.GenerateFuzzyHash(text1);
		var hash2 = _fingerprinter.GenerateFuzzyHash(text2);
		hash1.Should().Be(hash2);
	}

	[Fact]
	public void GenerateAllFingerprints_ReturnsBoth()
	{
		var (textHash, fuzzyHash) = _fingerprinter.GenerateAllFingerprints("test content here");
		textHash.Should().NotBeNullOrEmpty();
		fuzzyHash.Should().NotBeNullOrEmpty();
	}

	[Fact]
	public void GetFuzzySignature_ReturnsAlphabeticallySorted()
	{
		var text = "implementation documentation verification authentication authorization configuration";
		var signature = _fingerprinter.GetFuzzySignature(text);
		var sorted = signature.OrderBy(s => s).ToList();
		signature.Should().ContainInOrder(sorted);
	}

	[Fact]
	public void GetFuzzySignature_ExcludesStopwords()
	{
		var text = "the agreement between parties is for the settlement of property";
		var signature = _fingerprinter.GetFuzzySignature(text);
		signature.Should().NotContain("the");
		signature.Should().NotContain("is");
		signature.Should().NotContain("for");
	}

	[Fact]
	public void GetFuzzySignature_ExcludesShortWords()
	{
		var text = "big cat sat hat mat authentication verification implementation";
		var signature = _fingerprinter.GetFuzzySignature(text);
		signature.Should().NotContain("big");
		signature.Should().NotContain("cat");
		signature.Should().Contain("authentication");
	}

	[Fact]
	public void CalculateSimilarityMetrics_IdenticalTexts_PerfectScore()
	{
		var text = "this is a test document with some content words";
		var metrics = _fingerprinter.CalculateSimilarityMetrics(text, text);
		metrics.JaccardSimilarity.Should().Be(1.0);
		metrics.OverlapCoefficient.Should().Be(1.0);
	}

	[Fact]
	public void CalculateSimilarityMetrics_CompletelyDifferent_ZeroJaccard()
	{
		var text1 = "alpha bravo charlie";
		var text2 = "delta echo foxtrot";
		var metrics = _fingerprinter.CalculateSimilarityMetrics(text1, text2);
		metrics.JaccardSimilarity.Should().Be(0);
	}

	[Fact]
	public void CalculateSimilarityMetrics_PartialOverlap_MiddleScore()
	{
		var text1 = "alpha bravo charlie delta";
		var text2 = "alpha bravo echo foxtrot";
		var metrics = _fingerprinter.CalculateSimilarityMetrics(text1, text2);
		metrics.JaccardSimilarity.Should().BeGreaterThan(0);
		metrics.JaccardSimilarity.Should().BeLessThan(1);
	}

	[Fact]
	public void GenerateFileHash_SameBytes_SameHash()
	{
		var bytes = "test content"u8.ToArray();
		var hash1 = _fingerprinter.GenerateFileHash(bytes);
		var hash2 = _fingerprinter.GenerateFileHash(bytes);
		hash1.Should().Be(hash2);
	}
}
