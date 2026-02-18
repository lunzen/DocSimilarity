using DocGrouping.Infrastructure.TextProcessing;
using FluentAssertions;

namespace DocGrouping.Infrastructure.Tests;

public class TextNormalizerTests
{
	private readonly TextNormalizer _normalizer = new();

	[Fact]
	public void Normalize_LowercasesText()
	{
		var result = _normalizer.Normalize("THIS IS A TEST");
		result.Should().Be(result.ToLowerInvariant());
	}

	[Fact]
	public void Normalize_CollapsesWhitespace()
	{
		var result = _normalizer.Normalize("hello   world\t\ttab\n\nnewline");
		result.Should().NotContain("  ");
		result.Should().NotContain("\t");
		result.Should().NotContain("\n");
	}

	[Fact]
	public void Normalize_RemovesPageNumbers()
	{
		var result = _normalizer.Normalize("Some text Page 5 of 10 more text");
		result.Should().NotContain("page");
		result.Should().Contain("some text");
		result.Should().Contain("more text");
	}

	[Fact]
	public void Normalize_RemovesDashPageNumbers()
	{
		var result = _normalizer.Normalize("content - 5 - more");
		result.Should().NotContain("- 5 -");
	}

	[Fact]
	public void Normalize_RemovesDateStamps()
	{
		var result = _normalizer.Normalize("text 01/15/2024 more text");
		result.Should().NotContain("01/15/2024");
	}

	[Fact]
	public void Normalize_RemovesConfidentialityFooters()
	{
		var result = _normalizer.Normalize("text confidential and proprietary more text");
		result.Should().NotContain("confidential and proprietary");
	}

	[Fact]
	public void Normalize_CorrectsOcrErrors_ZeroToO()
	{
		var result = _normalizer.Normalize("w0rd");
		result.Should().Contain("word");
	}

	[Fact]
	public void Normalize_CorrectsOcrErrors_OneToL()
	{
		var result = _normalizer.Normalize("fi1e");
		result.Should().Contain("file");
	}

	[Fact]
	public void Normalize_NormalizesSmartQuotes()
	{
		var result = _normalizer.Normalize("\u201CHello\u201D \u2018World\u2019");
		result.Should().NotContain("\u201C");
		result.Should().NotContain("\u201D");
		result.Should().NotContain("\u2018");
		result.Should().NotContain("\u2019");
	}

	[Fact]
	public void Normalize_HandlesHyphenation()
	{
		var result = _normalizer.Normalize("opera- tion");
		result.Should().Contain("operation");
	}

	[Fact]
	public void Normalize_RemovesBatesStamps()
	{
		var result = _normalizer.Normalize("text ABC-001234567 more");
		result.Should().NotContain("ABC-001234567");
	}

	[Fact]
	public void GetTokens_SplitsOnWhitespace()
	{
		var tokens = _normalizer.GetTokens("hello world test");
		tokens.Should().HaveCount(3);
		tokens.Should().Contain("hello");
	}

	[Fact]
	public void Normalize_ProducesDeterministicOutput()
	{
		var text = "This is a sample document with some content.";
		var result1 = _normalizer.Normalize(text);
		var result2 = _normalizer.Normalize(text);
		result1.Should().Be(result2);
	}

	[Fact]
	public void Normalize_EmptyString_ReturnsEmpty()
	{
		var result = _normalizer.Normalize("");
		result.Should().BeEmpty();
	}
}
